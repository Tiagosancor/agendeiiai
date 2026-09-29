using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Usuarios;

public sealed class GerenciadorUsuarios : IGerenciadorUsuarios
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly ICriptografiaCpf _criptografiaCpf;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;

    public GerenciadorUsuarios(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, ICriptografiaCpf criptografiaCpf,
        IContextoNegocio contextoNegocio, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _criptografiaCpf = criptografiaCpf;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
    }

    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Usuarios
            .Where(u => !u.Excluido)
            .OrderBy(u => u.Nome)
            .Select(u => new UsuarioResumo(u.Id, u.Nome, u.Email, u.Perfil, u.Ativo, u.FotoUrl))
            .ToListAsync(cancellationToken);

    public async Task<UsuarioDetalhe?> ObterAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        return usuario is null ? null : Mapear(usuario);
    }

    public async Task<Guid> CriarAsync(CriarUsuario dados, CancellationToken cancellationToken = default)
    {
        var emailNormalizado = dados.Email.Trim().ToLowerInvariant();

        // Checagem "otimista": o índice único global em Email (só entre os não excluídos) é
        // quem garante a regra de verdade sob concorrência (ver o catch abaixo).
        if (await EmailEmUsoAsync(emailNormalizado, usuarioIgnorado: null, cancellationToken))
            throw new EmailJaCadastradoException(emailNormalizado);

        var cpfProtegido = ProtegerCpfSeInformado(dados.Cpf);
        var senhaHash = _senhaHasher.Hash(dados.Senha);

        var usuario = Usuario.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Nome, emailNormalizado, dados.Perfil, senhaHash,
            dados.Telefone, cpf: cpfProtegido);

        _dbContext.Usuarios.Add(usuario);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            throw new EmailJaCadastradoException(emailNormalizado);
        }

        return usuario.Id;
    }

    public async Task<bool> AtualizarDadosAsync(
        Guid usuarioId, AtualizarUsuario dados, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        var alterados = new List<string>();
        var endereco = dados.Endereco ?? usuario.Endereco;

        if (!string.Equals(usuario.Nome, dados.Nome.Trim(), StringComparison.Ordinal)) alterados.Add("nome");
        if (usuario.Telefone != dados.Telefone) alterados.Add("telefone");
        if (usuario.Endereco != endereco) alterados.Add("endereço");
        usuario.AtualizarDados(dados.Nome, dados.Telefone, endereco);

        if (!string.IsNullOrWhiteSpace(dados.Email))
        {
            var novoEmail = dados.Email.Trim().ToLowerInvariant();
            if (novoEmail != usuario.Email)
            {
                if (await EmailEmUsoAsync(novoEmail, usuarioId, cancellationToken))
                    throw new EmailJaCadastradoException(novoEmail);

                usuario.AlterarEmail(novoEmail);
                alterados.Add("e-mail");
            }
        }

        if (!string.IsNullOrWhiteSpace(dados.Cpf))
        {
            usuario.DefinirCpf(ProtegerCpfSeInformado(dados.Cpf)!);
            alterados.Add("CPF");
        }

        if (dados.FotoUrl is not null && dados.FotoUrl != (usuario.FotoUrl ?? string.Empty))
        {
            usuario.DefinirFoto(string.IsNullOrWhiteSpace(dados.FotoUrl) ? null : dados.FotoUrl.Trim());
            alterados.Add("foto");
        }

        if (alterados.Count > 0)
            _auditoria.Registrar(AcoesAuditoria.Editar, nameof(Usuario), usuario.Id, $"Campos: {string.Join(", ", alterados)}");

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            throw new EmailJaCadastradoException(usuario.Email);
        }

        return true;
    }

    public async Task<bool> AlterarSenhaAsync(
        Guid usuarioId, string novaSenha, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        usuario.AlterarSenha(_senhaHasher.Hash(novaSenha));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ConcederPermissaoAsync(
        Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        usuario.ConcederPermissao(permissao);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> RevogarPermissaoAsync(
        Guid usuarioId, Permissao permissao, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        // Sem ninguém que gerencie usuários, o negócio fica trancado para fora da própria
        // gestão de acessos (seção 7: nunca tirar a permissão do último Administrador).
        if (permissao == Permissao.GerenciarUsuarios && await EhUltimoAdministradorAsync(usuario, cancellationToken))
            throw new OperacaoCadastroBloqueadaException(
                "Este é o último Administrador do negócio: ele não pode perder a gestão de usuários.");

        usuario.RevogarPermissao(permissao);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        if (usuario.Id == _usuarioAtual.UsuarioId)
            throw new OperacaoCadastroBloqueadaException("Você não pode desativar o seu próprio usuário.");

        if (await EhUltimoAdministradorAsync(usuario, cancellationToken))
            throw new OperacaoCadastroBloqueadaException("Este é o último Administrador ativo do negócio e não pode ser desativado.");

        usuario.Desativar();
        await RevogarSessoesAsync(usuario.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AtivarAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return false;

        usuario.Ativar();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return null;

        return new PreviaExclusao(
            usuario.Nome, await TemHistoricoAsync(usuario.Id, cancellationToken), 0,
            await MotivoBloqueioExclusaoAsync(usuario, cancellationToken), []);
    }

    public async Task<ResultadoExclusao> ExcluirAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        if (usuario is null)
            return ResultadoExclusao.NaoEncontrado;

        var bloqueio = await MotivoBloqueioExclusaoAsync(usuario, cancellationToken);
        if (bloqueio is not null)
            return ResultadoExclusao.Bloqueado(bloqueio);

        var temHistorico = await TemHistoricoAsync(usuario.Id, cancellationToken);

        // Sessões encerradas na hora (seção 7): refresh tokens revogados aqui; o access token
        // em uso deixa de valer na próxima requisição (checagem do usuário no JwtBearer).
        await RevogarSessoesAsync(usuario.Id, cancellationToken);

        if (temHistorico)
        {
            usuario.Excluir(DateTimeOffset.UtcNow);
            _auditoria.Registrar(AcoesAuditoria.ExcluirLogicamente, nameof(Usuario), usuario.Id, $"Nome: {usuario.Nome}");
        }
        else
        {
            _dbContext.TokensAtualizacao.RemoveRange(
                await _dbContext.TokensAtualizacao.Where(t => t.UsuarioId == usuario.Id).ToListAsync(cancellationToken));
            _dbContext.Usuarios.Remove(usuario);
            _auditoria.Registrar(AcoesAuditoria.ApagarDefinitivo, nameof(Usuario), usuario.Id, $"Nome: {usuario.Nome}");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoExclusao.Concluido(temHistorico);
    }

    public async Task<string?> RevelarCpfAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await BuscarAsync(usuarioId, cancellationToken);
        return usuario?.Cpf is null ? null : _criptografiaCpf.Revelar(usuario.Cpf);
    }

    private async Task<string?> MotivoBloqueioExclusaoAsync(Usuario usuario, CancellationToken cancellationToken)
    {
        if (usuario.Id == _usuarioAtual.UsuarioId)
            return "Você não pode excluir o seu próprio usuário.";

        if (await EhUltimoAdministradorAsync(usuario, cancellationToken))
            return "Este é o último Administrador do negócio e não pode ser excluído. Promova outro usuário antes.";

        return null;
    }

    /// <summary>Administrador ativo sem nenhum outro Administrador ativo no negócio.</summary>
    private async Task<bool> EhUltimoAdministradorAsync(Usuario usuario, CancellationToken cancellationToken) =>
        usuario.Perfil == Perfil.Administrador && usuario.Ativo
        && !await _dbContext.Usuarios.AnyAsync(
            u => u.Id != usuario.Id && u.Perfil == Perfil.Administrador && u.Ativo && !u.Excluido, cancellationToken);

    /// <summary>
    /// Usuário "usado" = já fez alguma ação registrada na auditoria do negócio. Sem isso, não
    /// há nenhum registro que dependa dele e a linha pode sumir de fato (seção 7).
    /// </summary>
    /// <summary>Autor de alguma ação auditada, ou vendedor de alguma venda de produto (tem comissão no histórico).</summary>
    private async Task<bool> TemHistoricoAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await _dbContext.LogsAuditoriaNegocio.AnyAsync(l => l.AutorUsuarioId == usuarioId, cancellationToken)
        || await _dbContext.VendasProduto.AnyAsync(v => v.VendedorUsuarioId == usuarioId, cancellationToken);

    private async Task RevogarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken)
    {
        var tokens = await _dbContext.TokensAtualizacao
            .Where(t => t.UsuarioId == usuarioId && t.RevogadoEm == null)
            .ToListAsync(cancellationToken);

        foreach (var token in tokens)
            token.Revogar();
    }

    private Task<bool> EmailEmUsoAsync(string email, Guid? usuarioIgnorado, CancellationToken cancellationToken) =>
        _dbContext.Usuarios
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == email && !u.Excluido && u.Id != usuarioIgnorado, cancellationToken);

    /// <summary>Excluído não aparece mais em lugar nenhum do painel (seção 7) — 404 para qualquer ação.</summary>
    private Task<Usuario?> BuscarAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        _dbContext.Usuarios.Include(u => u.Permissoes)
            .FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Excluido, cancellationToken);

    private CpfProtegido? ProtegerCpfSeInformado(string? cpf) =>
        string.IsNullOrWhiteSpace(cpf) ? null : _criptografiaCpf.Proteger(Cpf.Criar(cpf));

    private static UsuarioDetalhe Mapear(Usuario usuario) => new(
        usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Perfil, usuario.Ativo, usuario.FotoUrl,
        usuario.Cpf?.Mascarado, usuario.Permissoes.Select(p => p.Permissao).ToList(), usuario.Endereco);
}
