using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Cadastro;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Verificacao;

namespace Plataforma.Infraestrutura.Cadastro;

/// <summary>
/// Cadastro de negócio novo (seção 6.5/8.6). Negócio, administrador com todas as permissões,
/// assinatura em teste, registro do teste grátis e a chave de idempotência nascem numa
/// transação só: nunca fica negócio sem administrador ou sem assinatura, e o mesmo clique
/// repetido devolve o mesmo negócio.
/// </summary>
public sealed class ServicoCadastro : IServicoCadastro
{
    public const string EscopoIdempotencia = "cadastro";
    public const int TamanhoMinimoSenha = 8;

    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly INotificador _notificador;
    private readonly OpcoesVerificacao _opcoesVerificacao;
    private readonly OpcoesMarca _opcoesMarca;

    public ServicoCadastro(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, IServicoTokenPublico servicoToken, INotificador notificador,
        IOptions<OpcoesVerificacao> opcoesVerificacao, IOptions<OpcoesMarca> opcoesMarca)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _servicoToken = servicoToken;
        _notificador = notificador;
        _opcoesVerificacao = opcoesVerificacao.Value;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task<IReadOnlyList<PlanoPublico>> ListarPlanosAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Planos.AsNoTracking()
            .Where(p => p.Ativo)
            .OrderBy(p => p.Ordem)
            .Select(p => new PlanoPublico(p.Id, p.Nome, p.MinimoProfissionais, p.MaximoProfissionais, p.PrecoMensal, p.PrecoAnualPorMes, p.Destaque))
            .ToListAsync(cancellationToken);

    public async Task<DisponibilidadeSlug> VerificarSlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var normalizado = (slug ?? string.Empty).Trim().ToLowerInvariant();

        if (Slug.Reservados.Contains(normalizado))
            return new DisponibilidadeSlug(false, "Este endereço é reservado. Escolha outro.");

        if (!Slug.TentarCriar(normalizado, out var slugValido))
            return new DisponibilidadeSlug(false,
                $"Use de {Slug.TamanhoMinimo} a {Slug.TamanhoMaximo} letras minúsculas, números ou hífen, sem hífen no começo ou no fim.");

        return await _dbContext.Negocios.AnyAsync(n => n.Slug == slugValido!, cancellationToken)
            ? new DisponibilidadeSlug(false, "Este endereço já está em uso. Escolha outro.")
            : new DisponibilidadeSlug(true, null);
    }

    public async Task<ResultadoSolicitarCodigoCadastro> SolicitarCodigoAsync(string email, CancellationToken cancellationToken = default)
    {
        var normalizado = CodigoCadastro.NormalizarEmail(email);
        var agora = DateTimeOffset.UtcNow;

        var ultimaHora = await _dbContext.CodigosCadastro.CountAsync(c => c.Email == normalizado && c.CriadoEm >= agora.AddHours(-1), cancellationToken);
        var ultimoDia = await _dbContext.CodigosCadastro.CountAsync(c => c.Email == normalizado && c.CriadoEm >= agora.AddDays(-1), cancellationToken);

        if (ultimaHora >= _opcoesVerificacao.MaximoCodigosPorTelefonePorHora || ultimoDia >= _opcoesVerificacao.MaximoCodigosPorTelefonePorDia)
            return new ResultadoSolicitarCodigoCadastro(LimiteExcedido: true);

        await _dbContext.CodigosCadastro
            .Where(c => c.Email == normalizado && !c.Usado && !c.Invalidado)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Invalidado, true), cancellationToken);

        // O registro é gravado mesmo quando o e-mail já tem conta (com um código que ninguém
        // recebe): o rate limit acima se comporta igual nos dois casos, e não vira um jeito
        // de descobrir quem é cliente (seção 8.6.2).
        var codigo = CodigoConfirmacao.Gerar();
        _dbContext.CodigosCadastro.Add(CodigoCadastro.Criar(normalizado, CodigoConfirmacao.Hash(_opcoesVerificacao.ChaveHmac, codigo), agora));
        await _dbContext.SaveChangesAsync(cancellationToken);

        var jaTemConta = await _dbContext.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == normalizado, cancellationToken);
        if (jaTemConta)
            await _notificador.EnviarAvisoContaExistenteAsync(normalizado, ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, "/painel/login"), cancellationToken);
        else
            await _notificador.EnviarCodigoCadastroAsync(normalizado, codigo, cancellationToken);

        return new ResultadoSolicitarCodigoCadastro(LimiteExcedido: false);
    }

    public async Task<string?> ValidarCodigoAsync(string email, string codigo, CancellationToken cancellationToken = default)
    {
        var normalizado = CodigoCadastro.NormalizarEmail(email);

        var registro = await _dbContext.CodigosCadastro
            .Where(c => c.Email == normalizado)
            .OrderByDescending(c => c.CriadoEm)
            .FirstOrDefaultAsync(cancellationToken);

        if (registro is null)
            return null;

        var confere = registro.ConferirEMarcar(CodigoConfirmacao.Hash(_opcoesVerificacao.ChaveHmac, codigo ?? string.Empty), DateTimeOffset.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return confere ? _servicoToken.GerarTokenCadastro(normalizado) : null;
    }

    public async Task<ResultadoCadastro> CadastrarAsync(DadosCadastro dados, string chaveIdempotencia, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chaveIdempotencia) || chaveIdempotencia.Length > ChaveIdempotencia.TamanhoMaximo)
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Cabeçalho Idempotency-Key ausente ou inválido.");

        var email = CodigoCadastro.NormalizarEmail(dados.Email ?? string.Empty);
        var emailDoToken = await _servicoToken.ValidarTokenCadastroAsync(dados.TokenCadastro ?? string.Empty);
        if (emailDoToken is null || emailDoToken != email)
            return ResultadoCadastro.Falha(ErroCadastro.TokenInvalido, "Confirme o e-mail com o código de novo.");

        var validacao = Validar(dados, out var slug, out var telefone, out var tipo, out var periodicidade);
        if (validacao is not null)
            return validacao;

        var plano = await _dbContext.Planos.FirstOrDefaultAsync(p => p.Id == dados.PlanoId && p.Ativo, cancellationToken);
        if (plano is null)
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Este plano não está disponível.");

        var senhaHash = _senhaHasher.Hash(dados.Senha);
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        var (resultado, criado) = await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            var chave = new ChaveIdempotencia(EscopoIdempotencia, chaveIdempotencia, email);
            _dbContext.ChavesIdempotencia.Add(chave);

            try
            {
                // Grava a chave primeiro: um segundo clique com a mesma chave fica esperando
                // aqui até esta transação terminar e então cai no índice único.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                return ((ResultadoCadastro?)null, (Negocio?)null);
            }

            var conflito = await ProcurarConflitoAsync(slug!, email, telefone!, cancellationToken);
            if (conflito is not null)
                return (conflito, null);

            var agora = DateTimeOffset.UtcNow;
            var negocio = Negocio.Criar(slug!, dados.NomeNegocio, tipo);
            negocio.AtualizarPerfil(
                dados.NomeNegocio, null, null, null, null, null, null, Endereco.Vazio,
                dados.Telefone.Trim(), email, RedesSociais.Vazio, whatsAppAtivoParaConfirmacoes: false);

            var administrador = Usuario.Criar(negocio.Id, dados.Nome, email, Perfil.Administrador, senhaHash, telefone: telefone!.Valor);
            var assinatura = ServicoAssinatura.IniciarTeste(negocio.Id, plano, periodicidade, $"cadastro:{email}", agora);

            _dbContext.Negocios.Add(negocio);
            _dbContext.Usuarios.Add(administrador);
            _dbContext.Assinaturas.Add(assinatura);
            _dbContext.RegistrosTesteGratis.Add(new RegistroTesteGratis(email, telefone, negocio.Id));
            chave.Concluir(negocio.Id);

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                // Corrida com outro cadastro entre a checagem acima e o INSERT: o banco decide.
                return (TraduzirViolacao(excecao), null);
            }

            await transacao.CommitAsync(cancellationToken);
            return (ResultadoCadastro.Ok(negocio.Id, negocio.Slug.Valor), negocio);
        });

        if (resultado is null)
            return await ResultadoDaChaveExistenteAsync(chaveIdempotencia, email, cancellationToken);

        if (criado is not null)
        {
            var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters().AsNoTracking().FirstAsync(a => a.NegocioId == criado.Id, cancellationToken);
            await _notificador.EnviarBoasVindasAsync(new DadosBoasVindas(
                email, dados.Nome.Trim(), criado.NomeExibido,
                ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, "/painel"),
                ConstrutorUrlPublica.Construir(_opcoesMarca, criado.Slug.Valor, "/"),
                assinatura.FimTeste!.Value), cancellationToken);
        }

        return resultado;
    }

    private static ResultadoCadastro? Validar(
        DadosCadastro dados, out Slug? slug, out TelefoneE164? telefone, out TipoNegocio tipo, out Periodicidade periodicidade)
    {
        slug = null;
        telefone = null;
        tipo = default;
        periodicidade = default;

        if (!dados.AceiteTermos)
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "É preciso aceitar os Termos de Uso e a Política de Privacidade.");

        if (string.IsNullOrWhiteSpace(dados.NomeNegocio) || dados.NomeNegocio.Trim().Length > 120)
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Informe o nome do negócio (até 120 caracteres).");

        if (string.IsNullOrWhiteSpace(dados.Nome) || dados.Nome.Trim().Length > 200)
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Informe seu nome.");

        if (!MailAddress.TryCreate(dados.Email, out _))
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "E-mail inválido.");

        if (!TelefoneE164.TentarCriar(dados.Telefone, out telefone))
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Telefone inválido.");

        if (!SenhaForte(dados.Senha))
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos,
                $"A senha precisa ter pelo menos {TamanhoMinimoSenha} caracteres, com letras e números.");

        if (!Enum.TryParse(dados.TipoNegocio, ignoreCase: true, out tipo) || !Enum.IsDefined(tipo))
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Tipo de negócio inválido.");

        if (!Enum.TryParse(dados.Periodicidade, ignoreCase: true, out periodicidade) || !Enum.IsDefined(periodicidade))
            return ResultadoCadastro.Falha(ErroCadastro.DadosInvalidos, "Periodicidade inválida.");

        if (!Slug.TentarCriar(dados.Slug, out slug))
            return ResultadoCadastro.Falha(ErroCadastro.SlugInvalido, "Endereço da página inválido ou reservado.");

        return null;
    }

    public static bool SenhaForte(string? senha) =>
        senha is not null && senha.Length >= TamanhoMinimoSenha && senha.Any(char.IsLetter) && senha.Any(char.IsDigit);

    private async Task<ResultadoCadastro?> ProcurarConflitoAsync(Slug slug, string email, TelefoneE164 telefone, CancellationToken cancellationToken)
    {
        if (await _dbContext.Negocios.AnyAsync(n => n.Slug == slug, cancellationToken))
            return ResultadoCadastro.Falha(ErroCadastro.SlugEmUso, "Este endereço já está em uso. Escolha outro.");

        if (await _dbContext.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email, cancellationToken))
            return ResultadoCadastro.Falha(ErroCadastro.EmailJaCadastrado, "Este e-mail já tem uma conta. Entre pelo painel.");

        if (await _dbContext.RegistrosTesteGratis.AnyAsync(r => r.Email == email || r.Telefone == telefone.Valor, cancellationToken))
            return ResultadoCadastro.Falha(ErroCadastro.TesteJaUtilizado, "Já existe um teste grátis para este e-mail ou telefone.");

        return null;
    }

    private static ResultadoCadastro TraduzirViolacao(DbUpdateException excecao)
    {
        var restricao = (excecao.InnerException as PostgresException)?.ConstraintName ?? string.Empty;

        if (restricao.Contains("slug", StringComparison.Ordinal))
            return ResultadoCadastro.Falha(ErroCadastro.SlugEmUso, "Este endereço já está em uso. Escolha outro.");

        if (restricao.Contains("usuarios", StringComparison.Ordinal))
            return ResultadoCadastro.Falha(ErroCadastro.EmailJaCadastrado, "Este e-mail já tem uma conta. Entre pelo painel.");

        return ResultadoCadastro.Falha(ErroCadastro.TesteJaUtilizado, "Já existe um teste grátis para este e-mail ou telefone.");
    }

    private async Task<ResultadoCadastro> ResultadoDaChaveExistenteAsync(string chave, string email, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var existente = await _dbContext.ChavesIdempotencia.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Escopo == EscopoIdempotencia && c.Chave == chave.Trim(), cancellationToken);

        if (existente?.NegocioId is not Guid negocioId || existente.Email != email)
            return ResultadoCadastro.Falha(ErroCadastro.ChaveIdempotenciaDeOutroCadastro, "Esta Idempotency-Key já foi usada em outro cadastro.");

        var slug = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.Slug).FirstAsync(cancellationToken);
        return ResultadoCadastro.Ok(negocioId, slug.Valor);
    }
}
