using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>
/// Login e renovação. Usa <c>IgnoreQueryFilters()</c> propositalmente: antes de existir um
/// token não há negócio resolvido (o painel inteiro fica em <c>app.{dominio}</c> — seção 5),
/// então é o e-mail (ou o refresh token) que precisa ser buscado através de todos os
/// negócios. Esta é exatamente a "rotina administrativa explícita e revisada" citada em
/// docs/decisoes.md como a única desculpa aceitável para ignorar o filtro multi-tenant.
/// </summary>
public sealed class ServicoAutenticacao : IServicoAutenticacao
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly IGeradorTokenAcesso _geradorToken;
    private readonly OpcoesJwt _opcoesJwt;
    private readonly LimitadorTentativasLogin _limitador;
    private readonly ILogger<ServicoAutenticacao> _logger;

    /// <summary>
    /// bcrypt de um texto aleatório, gerado uma vez por processo: é contra ele que se confere a senha quando não há usuário, para
    /// o tempo de resposta não revelar se o identificador existe (todo login passa por exatamente uma verificação de bcrypt).
    /// </summary>
    private static string? _hashFicticio;

    public ServicoAutenticacao(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, IGeradorTokenAcesso geradorToken,
        IOptions<OpcoesJwt> opcoesJwt, LimitadorTentativasLogin limitador, ILogger<ServicoAutenticacao> logger)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _geradorToken = geradorToken;
        _opcoesJwt = opcoesJwt.Value;
        _limitador = limitador;
        _logger = logger;
    }

    public async Task<ResultadoLogin> LoginAsync(string identificador, string senha, CancellationToken cancellationToken = default)
    {
        var interpretado = IdentificadorLogin.Interpretar(identificador);

        // Identificador bloqueado (existente ou não, a resposta é a mesma): nem confere a senha.
        var chave = _limitador.ChaveDoIdentificador(NormalizadoParaChave(interpretado));
        if (await _limitador.BloqueadaAsync(chave, cancellationToken))
        {
            _logger.LogInformation("Login bloqueado por excesso de tentativas.");
            return ResultadoLogin.TentativasEsgotadas;
        }

        var usuario = await BuscarUsuarioAsync(interpretado, cancellationToken);

        // SEMPRE uma verificação de bcrypt, haja ou não usuário (e esteja ele ativo ou não): sem isso, quem não existe responde
        // muito mais rápido e dá para enumerar contas pelo tempo. Resultado idêntico para identificador inexistente ou
        // inválido, usuário inativo e senha errada (mesmo espírito anti-enumeração da seção 8.1.3).
        var senhaConfere = _senhaHasher.Verificar(senha, usuario?.SenhaHash ?? HashFicticio());

        if (usuario is null || !usuario.Ativo || !senhaConfere)
        {
            await _limitador.RegistrarFalhaAsync(chave, cancellationToken);
            return ResultadoLogin.Falha;
        }

        await _limitador.ZerarAsync(chave, cancellationToken);
        return await EmitirParDeTokensAsync(usuario, cancellationToken);
    }

    private string HashFicticio() => _hashFicticio ??= _senhaHasher.Hash(Guid.NewGuid().ToString("N"));

    /// <summary>
    /// Texto que identifica a conta na chave do bloqueio: a forma normalizada (telefone na canônica, para as variações do mesmo
    /// número valerem uma conta só). Tudo que não é identificador válido divide uma única chave (não enche a tabela de lixo).
    /// </summary>
    private static string NormalizadoParaChave(IdentificadorLogin id) => id.Tipo switch
    {
        TipoIdentificadorLogin.Email => "email:" + id.Email,
        TipoIdentificadorLogin.Telefone => "telefone:" + id.Telefone!.Valor,
        TipoIdentificadorLogin.NomeUsuario => "usuario:" + id.NomeUsuario!.Valor,
        _ => "invalido",
    };

    private async Task<Usuario?> BuscarUsuarioAsync(IdentificadorLogin id, CancellationToken cancellationToken)
    {
        var consulta = _dbContext.Usuarios.IgnoreQueryFilters().Include(u => u.Permissoes).Where(u => !u.Excluido);

        switch (id.Tipo)
        {
            case TipoIdentificadorLogin.Email:
                var email = id.Email;
                return await consulta.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            case TipoIdentificadorLogin.Telefone:
                var telefone = id.Telefone;
                return await consulta.FirstOrDefaultAsync(u => u.TelefoneLogin == telefone, cancellationToken);
            case TipoIdentificadorLogin.NomeUsuario:
                var nome = id.NomeUsuario;
                return await consulta.FirstOrDefaultAsync(u => u.NomeUsuario == nome, cancellationToken);
            default:
                return null;
        }
    }

    public async Task<ResultadoLogin> RenovarAsync(string refreshTokenBruto, CancellationToken cancellationToken = default)
    {
        var hash = HashDoToken(refreshTokenBruto);

        var tokenAtual = await _dbContext.TokensAtualizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);

        if (tokenAtual is null || !tokenAtual.Valido)
            return ResultadoLogin.Falha;

        var usuario = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .Include(u => u.Permissoes)
            .FirstOrDefaultAsync(u => u.Id == tokenAtual.UsuarioId, cancellationToken);

        if (usuario is null || !usuario.Ativo || usuario.Excluido)
            return ResultadoLogin.Falha;

        tokenAtual.Revogar();

        return await EmitirParDeTokensAsync(usuario, cancellationToken);
    }

    public async Task LogoutAsync(string refreshTokenBruto, CancellationToken cancellationToken = default)
    {
        var hash = HashDoToken(refreshTokenBruto);

        var tokenAtual = await _dbContext.TokensAtualizacao
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Hash == hash, cancellationToken);

        if (tokenAtual is null)
            return;

        tokenAtual.Revogar();
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<ResultadoLogin> EmitirParDeTokensAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        var tokenAcesso = _geradorToken.Gerar(usuario);

        var refreshTokenBruto = GerarTokenOpaco();
        var expiraEm = DateTimeOffset.UtcNow.AddDays(_opcoesJwt.RefreshTokenDias);

        var tokenAtualizacao = TokenAtualizacao.Criar(
            usuario.NegocioId, usuario.Id, HashDoToken(refreshTokenBruto), expiraEm);

        _dbContext.Add(tokenAtualizacao);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return new ResultadoLogin(true, tokenAcesso.Token, tokenAcesso.ExpiraEm, refreshTokenBruto, expiraEm);
    }

    private static string GerarTokenOpaco() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static string HashDoToken(string tokenBruto) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(tokenBruto)));
}
