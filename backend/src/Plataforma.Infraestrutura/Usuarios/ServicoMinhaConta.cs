using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Autenticacao;
using Plataforma.Infraestrutura.Cadastro;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Verificacao;

namespace Plataforma.Infraestrutura.Usuarios;

/// <summary>
/// "Minha conta": o usuário logado completa e confirma o e-mail, troca o nome de usuário e a senha. Só opera sobre o usuário do
/// token (nunca recebe id) e dentro do negócio do JWT. Ações sensíveis exigem a senha atual, e as falhas dessa conferência
/// contam na chave <c>conta:{id}</c> (mesmo limite do login: estourou, 429). Nada de identificador, código ou senha em log.
/// </summary>
public sealed class ServicoMinhaConta : IServicoMinhaConta
{
    /// <summary>Prefixo de domínio do HMAC do código de e-mail: nunca colide com o código de cliente nem com o do cadastro.</summary>
    private const string PrefixoCodigo = "email-usuario:";

    private const int MaximoPedidosPorHora = 3;
    private const int MaximoPedidosPorDia = 10;
    private const int MaximoPedidosPorHoraPorDestino = 3;

    private readonly PlataformaDbContext _dbContext;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly ISenhaHasher _senhaHasher;
    private readonly LimitadorTentativasLogin _limitador;
    private readonly ServicoIdentificadoresAcesso _identificadores;
    private readonly INotificador _notificador;
    private readonly IRegistroAuditoria _auditoria;
    private readonly IServicoRedefinicaoSenha _redefinicaoSenha;
    private readonly OpcoesVerificacao _opcoesVerificacao;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly ILogger<ServicoMinhaConta> _logger;

    public ServicoMinhaConta(
        PlataformaDbContext dbContext, IUsuarioAtual usuarioAtual, ISenhaHasher senhaHasher, LimitadorTentativasLogin limitador,
        ServicoIdentificadoresAcesso identificadores, INotificador notificador, IRegistroAuditoria auditoria,
        IServicoRedefinicaoSenha redefinicaoSenha, IOptions<OpcoesVerificacao> opcoesVerificacao, IOptions<OpcoesMarca> opcoesMarca,
        ILogger<ServicoMinhaConta> logger)
    {
        _dbContext = dbContext;
        _usuarioAtual = usuarioAtual;
        _senhaHasher = senhaHasher;
        _limitador = limitador;
        _identificadores = identificadores;
        _notificador = notificador;
        _auditoria = auditoria;
        _redefinicaoSenha = redefinicaoSenha;
        _opcoesVerificacao = opcoesVerificacao.Value;
        _opcoesMarca = opcoesMarca.Value;
        _logger = logger;
    }

    public async Task<MinhaConta?> ObterAsync(CancellationToken cancellationToken = default)
    {
        var usuario = await CarregarAsync(cancellationToken);
        if (usuario is null)
            return null;

        var agora = DateTimeOffset.UtcNow;
        var pendente = await _dbContext.VerificacoesEmailUsuario.AsNoTracking()
            .Where(v => v.UsuarioId == usuario.Id && v.UsadoEm == null && v.InvalidadoEm == null && v.ExpiraEm > agora && v.Email != null)
            .OrderByDescending(v => v.CriadoEm)
            .Select(v => v.Email)
            .FirstOrDefaultAsync(cancellationToken);

        return new MinhaConta(
            usuario.Nome, usuario.Perfil, usuario.Email, usuario.EmailVerificado,
            pendente is null ? null : MascararEmail(pendente),
            usuario.NomeUsuario?.Valor, usuario.TelefoneLogin?.Valor, usuario.SenhaDefinidaPorTerceiro);
    }

    public async Task AlterarNomeUsuarioAsync(string? nomeUsuario, string senhaAtual, CancellationToken cancellationToken = default)
    {
        var usuario = await CarregarOuFalharAsync(cancellationToken);
        await ConferirSenhaAtualAsync(usuario, senhaAtual, cancellationToken);

        var novo = _identificadores.ParseNomeUsuario(nomeUsuario);
        if (Equals(novo, usuario.NomeUsuario))
            return;

        if (novo is not null)
            await _identificadores.VerificarDisponibilidadeAsync(null, novo, null, usuario.Id, cancellationToken);

        try
        {
            usuario.DefinirNomeUsuario(novo);
        }
        catch (IdentificadorObrigatorioException)
        {
            throw new ErroDeAcessoException("identificador_obrigatorio", "Cadastre um e-mail ou um telefone de login antes de remover o nome de usuário.");
        }

        _auditoria.Registrar(AcoesAuditoria.AlterarNomeUsuario, nameof(Usuario), usuario.Id, "Campos: nome de usuário");

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            throw await _identificadores.TraduzirViolacaoAsync(excecao, novo, cancellationToken) ?? (Exception)excecao;
        }
    }

    public async Task SolicitarEmailAsync(string email, string senhaAtual, CancellationToken cancellationToken = default)
    {
        var usuario = await CarregarOuFalharAsync(cancellationToken);
        await ConferirSenhaAtualAsync(usuario, senhaAtual, cancellationToken);

        var destino = ServicoIdentificadoresAcesso.ParseEmail(email)
            ?? throw new ErroDeAcessoException("email_invalido", "Informe um e-mail válido.");

        // Limites por usuário e por e-mail de destino (ninguém bombardeia a caixa de um terceiro). Acima deles a resposta é a
        // mesma — 202 — e nada é enviado.
        var porHora = await _limitador.ConsumirAsync(_limitador.Chave("email-usuario-hora", usuario.Id.ToString("N")), 60, cancellationToken);
        var porDia = await _limitador.ConsumirAsync(_limitador.Chave("email-usuario-dia", usuario.Id.ToString("N")), 1440, cancellationToken);
        var porDestino = await _limitador.ConsumirAsync(_limitador.Chave("email-destino-hora", destino), 60, cancellationToken);
        if (porHora > MaximoPedidosPorHora || porDia > MaximoPedidosPorDia || porDestino > MaximoPedidosPorHoraPorDestino)
        {
            _logger.LogInformation("Limite de pedidos de confirmação de e-mail atingido para o usuário {UsuarioId}.", usuario.Id);
            return;
        }

        // Já é o e-mail verificado da conta: nada a confirmar.
        if (usuario.EmailVerificado && usuario.Email == destino)
            return;

        var agora = DateTimeOffset.UtcNow;

        // Um pedido novo invalida os anteriores (e apaga o e-mail pendente deles).
        var anteriores = await _dbContext.VerificacoesEmailUsuario
            .Where(v => v.UsuarioId == usuario.Id && v.UsadoEm == null && v.InvalidadoEm == null)
            .ToListAsync(cancellationToken);
        foreach (var anterior in anteriores)
            anterior.Invalidar(agora);

        var expiraEm = agora.AddMinutes(_opcoesVerificacao.ValidadeCodigoMinutos);
        var donoDoEmail = await _dbContext.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(u => !u.Excluido && u.Email == destino && u.Id != usuario.Id, cancellationToken);

        if (donoDoEmail)
        {
            // Oráculo "esse e-mail já tem conta": o registro nasce com hash aleatório (ninguém conhece o código; o mesmo trabalho
            // no banco), nenhum código é enviado, e o endereço recebe o aviso de que já tem acesso. A confirmação vai falhar com o
            // mesmo "codigo_invalido" de um código errado.
            var aleatorio = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            _dbContext.VerificacoesEmailUsuario.Add(VerificacaoEmailUsuario.Criar(
                usuario.NegocioId, usuario.Id, destino, aleatorio, expiraEm, _opcoesVerificacao.MaximoTentativasValidacao));
            await _dbContext.SaveChangesAsync(cancellationToken);

            await _notificador.EnviarAvisoEmailJaTemAcessoAsync(
                destino, ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, "/painel/login"), cancellationToken);
            return;
        }

        var codigo = CodigoConfirmacao.Gerar();
        _dbContext.VerificacoesEmailUsuario.Add(VerificacaoEmailUsuario.Criar(
            usuario.NegocioId, usuario.Id, destino, HashDoCodigo(codigo), expiraEm, _opcoesVerificacao.MaximoTentativasValidacao));
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notificador.EnviarCodigoEmailUsuarioAsync(destino, usuario.Nome, codigo, _opcoesVerificacao.ValidadeCodigoMinutos, cancellationToken);
    }

    public async Task ConfirmarEmailAsync(string codigo, CancellationToken cancellationToken = default)
    {
        var usuario = await CarregarOuFalharAsync(cancellationToken);
        var agora = DateTimeOffset.UtcNow;

        var verificacao = await _dbContext.VerificacoesEmailUsuario
            .Where(v => v.UsuarioId == usuario.Id && v.UsadoEm == null && v.InvalidadoEm == null)
            .OrderByDescending(v => v.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (verificacao is null || !verificacao.Pendente(agora) || verificacao.Email is null)
            throw CodigoInvalido();

        // A tentativa é gravada ANTES de comparar (xmin protege contra duas confirmações simultâneas): errar três vezes esgota.
        verificacao.RegistrarTentativa();
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw CodigoInvalido();
        }

        var informado = (codigo ?? string.Empty).Trim();
        var confere = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashDoCodigo(informado)), Encoding.UTF8.GetBytes(verificacao.CodigoHash));
        if (!confere)
            throw CodigoInvalido();

        var novoEmail = verificacao.Email;
        var emailAnteriorVerificado = usuario.EmailVerificado && usuario.Email != novoEmail ? usuario.Email : null;

        usuario.ConfirmarEmail(novoEmail, agora);
        verificacao.MarcarUsada(agora);

        // Os links (redefinição/convite) enviados ao endereço antigo deixam de valer.
        await _redefinicaoSenha.InvalidarPendentesAsync(usuario.Id, cancellationToken);
        _auditoria.Registrar(AcoesAuditoria.ConfirmarEmailUsuario, nameof(Usuario), usuario.Id, "Campos: e-mail");

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao is DbUpdateConcurrencyException || excecao.EhViolacaoDeUnicidade())
        {
            // Corrida: outra conta confirmou o mesmo e-mail primeiro (o índice único decide) ou o código foi usado em paralelo.
            throw CodigoInvalido();
        }

        if (emailAnteriorVerificado is not null)
            await _notificador.EnviarAvisoEmailAcessoAlteradoAsync(emailAnteriorVerificado, usuario.Nome, MascararEmail(novoEmail), cancellationToken);
    }

    public async Task AlterarSenhaAsync(string senhaAtual, string novaSenha, CancellationToken cancellationToken = default)
    {
        var usuario = await CarregarOuFalharAsync(cancellationToken);
        await ConferirSenhaAtualAsync(usuario, senhaAtual, cancellationToken);

        if (!ServicoCadastro.SenhaForte(novaSenha))
            throw new ErroDeAcessoException("senha_fraca",
                $"A senha precisa ter pelo menos {ServicoCadastro.TamanhoMinimoSenha} caracteres, com letras e números.");

        usuario.AlterarSenha(_senhaHasher.Hash(novaSenha), definidaPorTerceiro: false);

        // Todas as sessões caem (inclusive a atual): o front entra de novo na hora com o identificador e a senha nova.
        var sessoes = await _dbContext.TokensAtualizacao.Where(t => t.UsuarioId == usuario.Id && t.RevogadoEm == null).ToListAsync(cancellationToken);
        foreach (var sessao in sessoes)
            sessao.Revogar();

        _auditoria.Registrar(AcoesAuditoria.AlterarSenhaPropria, nameof(Usuario), usuario.Id);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Confere a senha atual contando as falhas na chave da conta: 5 em 15 min e a conta responde 429 até passar a janela.</summary>
    private async Task ConferirSenhaAtualAsync(Usuario usuario, string senhaAtual, CancellationToken cancellationToken)
    {
        var chave = _limitador.ChaveDaConta(usuario.Id);
        if (await _limitador.BloqueadaAsync(chave, cancellationToken))
            throw new MuitasTentativasException();

        if (string.IsNullOrEmpty(senhaAtual) || !_senhaHasher.Verificar(senhaAtual, usuario.SenhaHash))
        {
            await _limitador.RegistrarFalhaAsync(chave, cancellationToken);
            throw new ErroDeAcessoException("senha_incorreta", "A senha atual não confere.");
        }

        await _limitador.ZerarAsync(chave, cancellationToken);
    }

    private Task<Usuario?> CarregarAsync(CancellationToken cancellationToken)
    {
        var id = _usuarioAtual.UsuarioId;
        return id is null
            ? Task.FromResult<Usuario?>(null)
            : _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == id && !u.Excluido && u.Ativo, cancellationToken);
    }

    private async Task<Usuario> CarregarOuFalharAsync(CancellationToken cancellationToken) =>
        await CarregarAsync(cancellationToken) ?? throw new ErroDeAcessoException("usuario_nao_encontrado", "Usuário não encontrado.");

    private static ErroDeAcessoException CodigoInvalido() =>
        new("codigo_invalido", "Código inválido ou vencido. Peça um novo código.");

    private string HashDoCodigo(string codigo) => CodigoConfirmacao.Hash(_opcoesVerificacao.ChaveHmac, PrefixoCodigo + codigo);

    /// <summary>Primeira letra, três asteriscos e o domínio: <c>a***@exemplo.com</c>.</summary>
    public static string MascararEmail(string email)
    {
        var arroba = email.IndexOf('@');
        return arroba <= 0 ? "***" : $"{email[0]}***{email[arroba..]}";
    }
}
