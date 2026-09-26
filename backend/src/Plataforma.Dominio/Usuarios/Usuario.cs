using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;

namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Conta de acesso ao painel. O e-mail é único **globalmente** (não só por negócio):
/// o login acontece em <c>app.{dominio}</c>, um único endereço compartilhado por todos os
/// negócios (seção 5), então é o e-mail sozinho que precisa identificar o usuário (e,
/// por tabela, o negócio) antes de existir qualquer token — ver docs/decisoes.md.
///
/// Autorização é sempre por <see cref="Permissao"/> (seção 4); o <see cref="Perfil"/> só
/// define o conjunto inicial de permissões ao criar o usuário (<see cref="PermissoesPadrao"/>) —
/// o administrador pode conceder/revogar permissões individualmente depois (seção 7).
/// </summary>
public class Usuario : EntidadeBase, IEntidadeDoNegocio
{
    private readonly List<UsuarioPermissao> _permissoes = [];

    public Guid NegocioId { get; private set; }

    public string Nome { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Telefone { get; private set; }

    public Endereco Endereco { get; private set; } = Endereco.Vazio;

    public CpfProtegido? Cpf { get; private set; }

    public string? FotoUrl { get; private set; }

    public string SenhaHash { get; private set; } = string.Empty;

    public Perfil Perfil { get; private set; }

    public bool Ativo { get; private set; } = true;

    /// <summary>
    /// Exclusão lógica (seção 7): some de todas as listas e não pode ser reativado. Só usada
    /// quando o usuário já tem histórico — sem histórico, a linha é apagada de fato.
    /// </summary>
    public bool Excluido { get; private set; }

    public DateTimeOffset? ExcluidoEm { get; private set; }

    /// <summary>Vínculo opcional com o registro de <c>Profissional</c> agendável — ver docs/decisoes.md.</summary>
    public Guid? ProfissionalId { get; private set; }

    public IReadOnlyCollection<UsuarioPermissao> Permissoes => _permissoes.AsReadOnly();

    protected Usuario()
    {
    }

    private Usuario(Guid negocioId, string nome, string email, Perfil perfil, string senhaHash)
    {
        NegocioId = negocioId;
        Nome = nome;
        Email = email;
        Perfil = perfil;
        SenhaHash = senhaHash;
    }

    public static Usuario Criar(
        Guid negocioId, string nome, string email, Perfil perfil, string senhaHash,
        string? telefone = null, Endereco? endereco = null, CpfProtegido? cpf = null)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        if (string.IsNullOrWhiteSpace(senhaHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(senhaHash));

        var usuario = new Usuario(negocioId, nome.Trim(), email.Trim().ToLowerInvariant(), perfil, senhaHash)
        {
            Telefone = telefone,
            Endereco = endereco ?? Endereco.Vazio,
            Cpf = cpf,
        };

        foreach (var permissao in PermissoesPadrao(perfil))
            usuario._permissoes.Add(new UsuarioPermissao(negocioId, usuario.Id, permissao));

        return usuario;
    }

    /// <summary>Conjunto inicial de permissões por perfil — só um ponto de partida (seção 7).</summary>
    public static IReadOnlyCollection<Permissao> PermissoesPadrao(Perfil perfil) => perfil switch
    {
        Perfil.Administrador => Enum.GetValues<Permissao>(),
        Perfil.Recepcionista =>
        [
            Permissao.GerenciarClientes,
            Permissao.GerenciarAgenda,
            Permissao.VerAgendaDeOutrosProfissionais,
            Permissao.GerenciarCupons,
        ],
        Perfil.Profissional => [],
        _ => throw new ArgumentOutOfRangeException(nameof(perfil), perfil, "Perfil desconhecido."),
    };

    public bool TemPermissao(Permissao permissao) => _permissoes.Any(p => p.Permissao == permissao);

    public void ConcederPermissao(Permissao permissao)
    {
        if (TemPermissao(permissao))
            return;

        _permissoes.Add(new UsuarioPermissao(NegocioId, Id, permissao));
    }

    public void RevogarPermissao(Permissao permissao) =>
        _permissoes.RemoveAll(p => p.Permissao == permissao);

    public void AtualizarDados(string nome, string? telefone, Endereco endereco)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        Nome = nome.Trim();
        Telefone = telefone;
        Endereco = endereco;
    }

    public void DefinirCpf(CpfProtegido cpf) => Cpf = cpf;

    public void DefinirFoto(string? fotoUrl) => FotoUrl = fotoUrl;

    public void VincularProfissional(Guid profissionalId) => ProfissionalId = profissionalId;

    public void DesvincularProfissional() => ProfissionalId = null;

    public void AlterarSenha(string novoHash)
    {
        if (string.IsNullOrWhiteSpace(novoHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(novoHash));

        SenhaHash = novoHash;
    }

    /// <summary>O índice único de e-mail considera só os não excluídos — quem chama checa a unicidade.</summary>
    public void AlterarEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        Email = email.Trim().ToLowerInvariant();
    }

    public void Desativar() => Ativo = false;

    public void Ativar()
    {
        if (Excluido)
            throw new InvalidOperationException("Um usuário excluído não pode ser reativado.");

        Ativo = true;
    }

    /// <summary>
    /// Exclusão lógica de quem já tem histórico (seção 7): fica só o nome, para os registros
    /// continuarem legíveis; e-mail, telefone, endereço, CPF e foto são apagados. Sem
    /// permissões e sem vínculo com profissional — a conta não entra mais em lugar nenhum.
    /// </summary>
    public void Excluir(DateTimeOffset agora)
    {
        Ativo = false;
        Excluido = true;
        ExcluidoEm = agora;
        Email = string.Empty;
        Telefone = null;
        Endereco = Endereco.Vazio;
        Cpf = null;
        FotoUrl = null;
        ProfissionalId = null;
        _permissoes.Clear();
    }
}
