using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
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

    public GerenciadorUsuarios(
        PlataformaDbContext dbContext, ISenhaHasher senhaHasher, ICriptografiaCpf criptografiaCpf,
        IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _criptografiaCpf = criptografiaCpf;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<UsuarioResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Usuarios
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

        // Checagem "otimista": o índice único global em Email é quem garante a regra de
        // verdade sob concorrência (ver CriarAsync's catch abaixo).
        var jaExiste = await _dbContext.Usuarios
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Email == emailNormalizado, cancellationToken);

        if (jaExiste)
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
        var usuario = await _dbContext.Usuarios.FindAsync([usuarioId], cancellationToken);
        if (usuario is null)
            return false;

        usuario.AtualizarDados(dados.Nome, dados.Telefone, usuario.Endereco);

        if (!string.IsNullOrWhiteSpace(dados.Cpf))
            usuario.DefinirCpf(ProtegerCpfSeInformado(dados.Cpf)!);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> AlterarSenhaAsync(
        Guid usuarioId, string novaSenha, CancellationToken cancellationToken = default)
    {
        var usuario = await _dbContext.Usuarios.FindAsync([usuarioId], cancellationToken);
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

        usuario.RevogarPermissao(permissao);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> DesativarAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(usuarioId, ativo: false, cancellationToken);

    public async Task<bool> AtivarAsync(Guid usuarioId, CancellationToken cancellationToken = default) =>
        await AlterarAtivoAsync(usuarioId, ativo: true, cancellationToken);

    public async Task<string?> RevelarCpfAsync(Guid usuarioId, CancellationToken cancellationToken = default)
    {
        var usuario = await _dbContext.Usuarios.FindAsync([usuarioId], cancellationToken);
        return usuario?.Cpf is null ? null : _criptografiaCpf.Revelar(usuario.Cpf);
    }

    private async Task<bool> AlterarAtivoAsync(Guid usuarioId, bool ativo, CancellationToken cancellationToken)
    {
        var usuario = await _dbContext.Usuarios.FindAsync([usuarioId], cancellationToken);
        if (usuario is null)
            return false;

        if (ativo)
            usuario.Ativar();
        else
            usuario.Desativar();

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private Task<Usuario?> BuscarAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        _dbContext.Usuarios.Include(u => u.Permissoes).FirstOrDefaultAsync(u => u.Id == usuarioId, cancellationToken);

    private CpfProtegido? ProtegerCpfSeInformado(string? cpf) =>
        string.IsNullOrWhiteSpace(cpf) ? null : _criptografiaCpf.Proteger(Cpf.Criar(cpf));

    private static UsuarioDetalhe Mapear(Usuario usuario) => new(
        usuario.Id, usuario.Nome, usuario.Email, usuario.Telefone, usuario.Perfil, usuario.Ativo, usuario.FotoUrl,
        usuario.Cpf?.Mascarado, usuario.Permissoes.Select(p => p.Permissao).ToList());
}
