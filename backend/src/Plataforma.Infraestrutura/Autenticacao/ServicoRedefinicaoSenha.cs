using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Cadastro;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>
/// "Esqueci minha senha" do painel. Como o login, busca por e-mail em todos os negócios
/// (<c>IgnoreQueryFilters()</c>): ainda não existe token, e o e-mail é único globalmente.
/// </summary>
public sealed class ServicoRedefinicaoSenha : IServicoRedefinicaoSenha
{
    public const int ValidadeMinutos = 60;

    /// <summary>O convite de acesso (seção 7) vale mais que o link de "esqueci a senha": quem recebe pode demorar a abrir.</summary>
    public const int ValidadeConviteHoras = 72;

    /// <summary>Pedidos por usuário por hora — além disso, nada é enviado (a resposta não muda).</summary>
    public const int MaximoPedidosPorHora = 3;

    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly INotificador _notificador;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly ILogger<ServicoRedefinicaoSenha> _logger;

    public ServicoRedefinicaoSenha(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, INotificador notificador,
        IOptions<OpcoesMarca> opcoesMarca, ILogger<ServicoRedefinicaoSenha> logger)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _notificador = notificador;
        _opcoesMarca = opcoesMarca.Value;
        _logger = logger;
    }

    public async Task SolicitarAsync(string email, CancellationToken cancellationToken = default)
    {
        var emailNormalizado = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (emailNormalizado.Length == 0)
            return;

        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Email == emailNormalizado && !u.Excluido && u.Ativo, cancellationToken);

        if (usuario is null)
            return;

        var agora = DateTimeOffset.UtcNow;
        var recentes = await _dbContext.RedefinicoesSenha
            .IgnoreQueryFilters()
            .Where(r => r.UsuarioId == usuario.Id && r.CriadoEm >= agora.AddHours(-1))
            .ToListAsync(cancellationToken);

        if (recentes.Count >= MaximoPedidosPorHora)
        {
            _logger.LogInformation("Limite de pedidos de redefinição de senha atingido para o usuário {UsuarioId}.", usuario.Id);
            return;
        }

        // Um link novo invalida os anteriores (inclusive os de mais de 1 h, ainda que já vencidos).
        var anteriores = await _dbContext.RedefinicoesSenha
            .IgnoreQueryFilters()
            .Where(r => r.UsuarioId == usuario.Id && r.UsadaEm == null && r.InvalidadaEm == null && r.ExpiraEm > agora)
            .ToListAsync(cancellationToken);
        foreach (var anterior in anteriores)
            anterior.Invalidar(agora);

        var tokenBruto = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _dbContext.RedefinicoesSenha.Add(
            RedefinicaoSenha.Criar(usuario.NegocioId, usuario.Id, HashDoToken(tokenBruto), agora.AddMinutes(ValidadeMinutos)));
        await _dbContext.SaveChangesAsync(cancellationToken);

        var link = ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, $"/painel/redefinir-senha?token={tokenBruto}");
        await _notificador.EnviarRedefinicaoSenhaAsync(usuario.Email, usuario.Nome, link, ValidadeMinutos, cancellationToken);
    }

    public async Task EnviarConviteAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Excluido && u.Ativo, cancellationToken);
        if (usuario is null)
            return;

        var agora = DateTimeOffset.UtcNow;
        var anteriores = await _dbContext.RedefinicoesSenha
            .Where(r => r.UsuarioId == usuario.Id && r.UsadaEm == null && r.InvalidadaEm == null && r.ExpiraEm > agora)
            .ToListAsync(cancellationToken);
        foreach (var anterior in anteriores)
            anterior.Invalidar(agora);

        var tokenBruto = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        _dbContext.RedefinicoesSenha.Add(
            RedefinicaoSenha.Criar(usuario.NegocioId, usuario.Id, HashDoToken(tokenBruto), agora.AddHours(ValidadeConviteHoras)));
        await _dbContext.SaveChangesAsync(cancellationToken);

        var link = ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, $"/painel/redefinir-senha?token={tokenBruto}");
        await _notificador.EnviarConviteAcessoAsync(usuario.Email, usuario.Nome, link, ValidadeConviteHoras, cancellationToken);
    }

    public async Task<ResultadoRedefinicaoSenha> RedefinirAsync(string token, string novaSenha, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return ResultadoRedefinicaoSenha.LinkInvalido;

        var agora = DateTimeOffset.UtcNow;
        var hash = HashDoToken(token.Trim());

        var redefinicao = await _dbContext.RedefinicoesSenha
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.Hash == hash, cancellationToken);

        if (redefinicao is null || !redefinicao.Valida(agora))
            return ResultadoRedefinicaoSenha.LinkInvalido;

        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == redefinicao.UsuarioId, cancellationToken);

        if (usuario is null || !usuario.Ativo || usuario.Excluido)
            return ResultadoRedefinicaoSenha.LinkInvalido;

        // A senha fraca não gasta o link: a pessoa corrige e tenta de novo.
        if (!ServicoCadastro.SenhaForte(novaSenha))
            return ResultadoRedefinicaoSenha.SenhaFraca;

        redefinicao.MarcarUsada(agora);
        usuario.AlterarSenha(_senhaHasher.Hash(novaSenha));

        // Quem pediu a troca pode estar tirando alguém da conta: derruba todas as sessões abertas.
        var sessoes = await _dbContext.TokensAtualizacao
            .IgnoreQueryFilters()
            .Where(t => t.UsuarioId == usuario.Id && t.RevogadoEm == null)
            .ToListAsync(cancellationToken);
        foreach (var sessao in sessoes)
            sessao.Revogar();

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outro envio do mesmo link chegou primeiro.
            return ResultadoRedefinicaoSenha.LinkInvalido;
        }

        _logger.LogInformation("Senha redefinida pelo link de e-mail para o usuário {UsuarioId}.", usuario.Id);
        return ResultadoRedefinicaoSenha.Sucesso;
    }

    private static string HashDoToken(string tokenBruto) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBruto)));
}
