using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Seguranca;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Profissionais;

public sealed class GerenciadorProfissionais : IGerenciadorProfissionais
{
    /// <summary>Nome do índice único de <c>usuarios.profissional_id</c> — um profissional tem no máximo um usuário (seção 7).</summary>
    public const string IndiceUsuarioPorProfissional = "ix_usuarios_profissional_id";

    private const int TamanhoMinimoSenha = 8;

    private readonly PlataformaDbContext _dbContext;
    private readonly ICriptografiaCpf _criptografiaCpf;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IRegistroAuditoria _auditoria;
    private readonly ISenhaHasher _senhaHasher;
    private readonly IServicoRedefinicaoSenha _redefinicaoSenha;

    public GerenciadorProfissionais(
        PlataformaDbContext dbContext, ICriptografiaCpf criptografiaCpf, IContextoNegocio contextoNegocio,
        IRegistroAuditoria auditoria, ISenhaHasher senhaHasher, IServicoRedefinicaoSenha redefinicaoSenha)
    {
        _dbContext = dbContext;
        _criptografiaCpf = criptografiaCpf;
        _contextoNegocio = contextoNegocio;
        _auditoria = auditoria;
        _senhaHasher = senhaHasher;
        _redefinicaoSenha = redefinicaoSenha;
    }

    public async Task<IReadOnlyList<ProfissionalResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Profissionais
            .Where(p => !p.Excluido)
            .OrderBy(p => p.Nome)
            .Select(p => new ProfissionalResumo(p.Id, p.Nome, p.Ativo, p.FotoUrl, p.Funcao))
            .ToListAsync(cancellationToken);

    public async Task<ProfissionalDetalhe?> ObterAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return null;

        var acesso = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => u.ProfissionalId == profissionalId && !u.Excluido)
            .Select(u => new AcessoVinculado(u.Id, u.Email, u.Ativo))
            .FirstOrDefaultAsync(cancellationToken);

        return Mapear(profissional) with { Acesso = acesso };
    }

    public async Task<Guid> CriarAsync(CriarProfissional dados, CancellationToken cancellationToken = default)
    {
        CpfProtegido? cpfProtegido = string.IsNullOrWhiteSpace(dados.Cpf)
            ? null
            : _criptografiaCpf.Proteger(Cpf.Criar(dados.Cpf));

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var profissional = Profissional.Criar(
            negocioId, dados.Nome, dados.Telefone, dados.Email, cpf: cpfProtegido, funcao: dados.Funcao);

        // Os dois caminhos da seção 7 levam ao mesmo par vinculado: login novo criado aqui, ou um usuário que
        // acabou de ser cadastrado em "Usuários" com perfil Profissional.
        var novoUsuario = dados.Acesso is null
            ? null
            : await NovoUsuarioDeAcessoAsync(negocioId, dados.Nome, dados.Telefone, dados.Acesso, cancellationToken);

        Usuario? usuarioExistente = null;
        if (dados.UsuarioId is { } usuarioId)
        {
            usuarioExistente = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Excluido, cancellationToken)
                ?? throw new OperacaoCadastroBloqueadaException("Usuário não encontrado.");
            if (usuarioExistente.ProfissionalId is not null)
                throw new OperacaoCadastroBloqueadaException("Este usuário já está vinculado a outro profissional.");
        }

        await DentroDoLimiteDoPlanoAsync(async () =>
        {
            _dbContext.Profissionais.Add(profissional);
            if (novoUsuario is not null)
            {
                novoUsuario.VincularProfissional(profissional.Id);
                _dbContext.Usuarios.Add(novoUsuario);
            }

            usuarioExistente?.VincularProfissional(profissional.Id);
            await SalvarComVinculoAsync(novoUsuario?.Email, cancellationToken);
        }, cancellationToken);

        if (novoUsuario is not null && dados.Acesso!.EnviarConvite)
            await _redefinicaoSenha.EnviarConviteAsync(novoUsuario.Id, cancellationToken);

        return profissional.Id;
    }

    public async Task<Guid?> DarAcessoAsync(Guid profissionalId, AcessoProfissional dados, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return null;

        if (await _dbContext.Usuarios.AnyAsync(u => u.ProfissionalId == profissionalId && !u.Excluido, cancellationToken))
            throw new OperacaoCadastroBloqueadaException("Este profissional já tem acesso ao sistema.");

        var usuario = await NovoUsuarioDeAcessoAsync(profissional.NegocioId, profissional.Nome, profissional.Telefone, dados, cancellationToken);
        usuario.VincularProfissional(profissional.Id);
        _dbContext.Usuarios.Add(usuario);
        _auditoria.Registrar(AcoesAuditoria.DarAcessoProfissional, nameof(Profissional), profissional.Id,
            dados.EnviarConvite ? "Acesso criado com convite por e-mail" : "Acesso criado com senha definida");

        await SalvarComVinculoAsync(usuario.Email, cancellationToken);

        if (dados.EnviarConvite)
            await _redefinicaoSenha.EnviarConviteAsync(usuario.Id, cancellationToken);

        return usuario.Id;
    }

    /// <summary>
    /// Login do profissional (seção 7): perfil Profissional, e-mail único na plataforma. Com convite, a senha
    /// é aleatória e ninguém a conhece — a pessoa cria a dela pelo link.
    /// </summary>
    private async Task<Usuario> NovoUsuarioDeAcessoAsync(
        Guid negocioId, string nome, string? telefone, AcessoProfissional acesso, CancellationToken cancellationToken)
    {
        var email = (acesso.Email ?? string.Empty).Trim().ToLowerInvariant();
        if (email.Length == 0 || !email.Contains('@'))
            throw new ArgumentException("Informe o e-mail de acesso do profissional.");

        string senha;
        if (acesso.EnviarConvite)
            senha = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        else if (string.IsNullOrEmpty(acesso.Senha) || acesso.Senha.Length < TamanhoMinimoSenha)
            throw new ArgumentException($"A senha precisa ter pelo menos {TamanhoMinimoSenha} caracteres.");
        else
            senha = acesso.Senha;

        // E-mail é único na plataforma inteira, só entre os não excluídos (mesma regra de "Usuários").
        if (await _dbContext.Usuarios.IgnoreQueryFilters().AnyAsync(u => u.Email == email && !u.Excluido, cancellationToken))
            throw new EmailJaCadastradoException(email);

        return Usuario.Criar(negocioId, nome, email, Perfil.Profissional, _senhaHasher.Hash(senha), telefone);
    }

    /// <summary>Os índices únicos decidem as corridas: e-mail repetido, ou o profissional ganhando dois usuários.</summary>
    private async Task SalvarComVinculoAsync(string? emailNovo, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            if ((excecao.InnerException as PostgresException)?.ConstraintName == IndiceUsuarioPorProfissional)
                throw new OperacaoCadastroBloqueadaException("Este profissional já tem acesso ao sistema.");
            throw new EmailJaCadastradoException(emailNovo ?? string.Empty);
        }
    }

    public async Task<bool> AtualizarDadosAsync(
        Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return false;

        var endereco = dados.Endereco ?? profissional.Endereco;
        var alterados = new List<string>();
        if (!string.Equals(profissional.Nome, dados.Nome.Trim(), StringComparison.Ordinal)) alterados.Add("nome");
        if (profissional.Telefone != dados.Telefone) alterados.Add("telefone");
        if (profissional.Email != dados.Email) alterados.Add("e-mail");
        if (profissional.Funcao != dados.Funcao) alterados.Add("função");
        if (profissional.Endereco != endereco) alterados.Add("endereço");

        profissional.AtualizarDados(dados.Nome, dados.Telefone, dados.Email, endereco, dados.Funcao);

        if (!string.IsNullOrWhiteSpace(dados.Cpf))
        {
            profissional.DefinirCpf(_criptografiaCpf.Proteger(Cpf.Criar(dados.Cpf)));
            alterados.Add("CPF");
        }

        if (dados.FotoUrl is not null && dados.FotoUrl != (profissional.FotoUrl ?? string.Empty))
        {
            profissional.DefinirFoto(string.IsNullOrWhiteSpace(dados.FotoUrl) ? null : dados.FotoUrl.Trim());
            alterados.Add("foto");
        }

        if (alterados.Count > 0)
            _auditoria.Registrar(AcoesAuditoria.Editar, nameof(Profissional), profissional.Id, $"Campos: {string.Join(", ", alterados)}");

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(profissionalId, ativo: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(profissionalId, ativo: true, cancellationToken);

    public async Task<string?> RevelarCpfAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        return profissional?.Cpf is null ? null : _criptografiaCpf.Revelar(profissional.Cpf);
    }

    private async Task<bool> AlterarAtivoAsync(Guid profissionalId, bool ativo, CancellationToken cancellationToken)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return false;

        if (!ativo)
        {
            profissional.Desativar();
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }

        if (profissional.Ativo)
            return true;

        await DentroDoLimiteDoPlanoAsync(async () =>
        {
            profissional.Ativar();
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        return true;
    }

    /// <summary>
    /// Limite de profissionais ativos do plano (seção 7). Trava a linha da assinatura
    /// (FOR UPDATE) antes de contar: dois cadastros simultâneos no último lugar livre não
    /// passam os dois. Sem assinatura (só em testes antigos), não há limite.
    /// </summary>
    private async Task DentroDoLimiteDoPlanoAsync(Func<Task> acao, CancellationToken cancellationToken)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM assinaturas WHERE negocio_id = {negocioId} FOR UPDATE", cancellationToken);

            var plano = await (
                from a in _dbContext.Assinaturas
                join p in _dbContext.Planos on a.PlanoId equals p.Id
                select new { p.Nome, p.MaximoProfissionais }).FirstOrDefaultAsync(cancellationToken);

            if (plano is not null)
            {
                var ativos = await _dbContext.Profissionais.CountAsync(p => p.Ativo, cancellationToken);
                if (ativos + 1 > plano.MaximoProfissionais)
                    throw new LimitePlanoAtingidoException(plano.Nome, plano.MaximoProfissionais);
            }

            await acao();
            await transacao.CommitAsync(cancellationToken);
        });
    }

    public async Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return null;

        var futuros = await BuscarAgendamentosFuturosAsync(profissionalId, cancellationToken);
        var clienteIds = futuros.Where(a => a.ClienteId is not null).Select(a => a.ClienteId!.Value).Distinct().ToList();
        var clientes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => clienteIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        // Quem pode receber cada agendamento: ativo, não excluído e executa TODOS os serviços
        // dele. Se o horário está livre para essa pessoa só a transferência confere (409).
        var outros = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id != profissionalId && p.Ativo && !p.Excluido)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome })
            .ToListAsync(cancellationToken);
        var outrosIds = outros.Select(o => o.Id).ToList();
        var vinculos = await _dbContext.ProfissionalServicos.AsNoTracking()
            .Where(ps => outrosIds.Contains(ps.ProfissionalId))
            .Select(ps => new { ps.ProfissionalId, ps.ServicoId })
            .ToListAsync(cancellationToken);

        var itens = futuros.Select(a =>
        {
            var servicoIds = a.Servicos.Select(s => s.ServicoId).ToHashSet();
            var possiveis = outros
                .Where(o => servicoIds.All(sid => vinculos.Any(v => v.ProfissionalId == o.Id && v.ServicoId == sid)))
                .Select(o => new OpcaoTransferencia(o.Id, o.Nome))
                .ToList();

            var cliente = a.ClienteId is not null && clientes.TryGetValue(a.ClienteId.Value, out var nome)
                ? nome
                : a.NomeInformado ?? "Cliente";

            return new AgendamentoFuturoParaExclusao(a.Id, a.Inicio, a.Fim, cliente, a.Servicos.Select(s => s.Nome).ToList(), possiveis);
        }).ToList();

        return new PreviaExclusao(
            profissional.Nome, await TemHistoricoAsync(profissionalId, cancellationToken), itens.Count,
            itens.Count > 0 ? MensagemFuturos(itens.Count) : null, itens);
    }

    public async Task<ResultadoExclusao> ExcluirAsync(Guid profissionalId, CancellationToken cancellationToken = default)
    {
        var profissional = await BuscarAsync(profissionalId, cancellationToken);
        if (profissional is null)
            return ResultadoExclusao.NaoEncontrado;

        var futuros = await BuscarAgendamentosFuturosAsync(profissionalId, cancellationToken);
        if (futuros.Count > 0)
            return ResultadoExclusao.Bloqueado(MensagemFuturos(futuros.Count));

        var temHistorico = await TemHistoricoAsync(profissionalId, cancellationToken);

        // Usuário do painel ligado a este profissional deixa de estar ligado a ele — e, se só entrava como ele
        // (perfil Profissional), perde o acesso (seção 7). Excluir o usuário junto é outra ação, explícita, em
        // "Usuários"; o Administrador dono que também atende continua entrando normalmente.
        var usuariosVinculados = await _dbContext.Usuarios.Where(u => u.ProfissionalId == profissionalId).ToListAsync(cancellationToken);
        foreach (var usuario in usuariosVinculados)
        {
            usuario.DesvincularProfissional();
            if (usuario.Perfil == Perfil.Profissional)
                usuario.Desativar();
        }

        // Configuração que só existe por causa dele: sai junto nos dois casos — tira o
        // profissional da oferta pública e das opções de transferência na hora.
        _dbContext.ProfissionalServicos.RemoveRange(
            await _dbContext.ProfissionalServicos.Where(ps => ps.ProfissionalId == profissionalId).ToListAsync(cancellationToken));

        if (temHistorico)
        {
            profissional.Excluir(DateTimeOffset.UtcNow);
            _auditoria.Registrar(AcoesAuditoria.ExcluirLogicamente, nameof(Profissional), profissional.Id, $"Nome: {profissional.Nome}");
        }
        else
        {
            _dbContext.HorariosTrabalho.RemoveRange(
                await _dbContext.HorariosTrabalho.Where(h => h.ProfissionalId == profissionalId).ToListAsync(cancellationToken));
            _dbContext.BloqueiosAgenda.RemoveRange(
                await _dbContext.BloqueiosAgenda.Where(b => b.ProfissionalId == profissionalId).ToListAsync(cancellationToken));
            _dbContext.Profissionais.Remove(profissional);
            _auditoria.Registrar(AcoesAuditoria.ApagarDefinitivo, nameof(Profissional), profissional.Id, $"Nome: {profissional.Nome}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoExclusao.Concluido(temHistorico);
    }

    private static string MensagemFuturos(int quantidade) => quantidade == 1
        ? "Este profissional tem 1 agendamento futuro. Transfira para outro profissional ou cancele antes de excluir."
        : $"Este profissional tem {quantidade} agendamentos futuros. Transfira cada um para outro profissional ou cancele antes de excluir.";

    /// <summary>Agendados e reservas ainda válidas que ainda não começaram, e atendimentos em andamento.</summary>
    private Task<List<Agendamento>> BuscarAgendamentosFuturosAsync(Guid profissionalId, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        return _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .Where(a => a.ProfissionalId == profissionalId
                && ((a.Inicio > agora && (a.Status == StatusAgendamento.Agendado
                        || (a.Status == StatusAgendamento.Reservado && a.ReservadoAte > agora)))
                    || a.Status == StatusAgendamento.EmAtendimento))
            .OrderBy(a => a.Inicio)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Qualquer agendamento (de qualquer status) já feito com ele conta como histórico (seção 7).</summary>
    private Task<bool> TemHistoricoAsync(Guid profissionalId, CancellationToken cancellationToken) =>
        _dbContext.Agendamentos.AnyAsync(a => a.ProfissionalId == profissionalId, cancellationToken);

    /// <summary>Excluído não aparece mais em lugar nenhum do painel (seção 7) — 404 para qualquer ação.</summary>
    private Task<Profissional?> BuscarAsync(Guid profissionalId, CancellationToken cancellationToken) =>
        _dbContext.Profissionais.FirstOrDefaultAsync(p => p.Id == profissionalId && !p.Excluido, cancellationToken);

    private static ProfissionalDetalhe Mapear(Profissional profissional) => new(
        profissional.Id, profissional.Nome, profissional.Telefone, profissional.Email, profissional.Ativo,
        profissional.FotoUrl, profissional.Cpf?.Mascarado, profissional.Funcao, profissional.Endereco);
}
