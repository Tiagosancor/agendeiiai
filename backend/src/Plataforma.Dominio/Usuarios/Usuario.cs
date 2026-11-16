using System.Net.Mail;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Seguranca;

namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Conta de acesso ao painel. E-mail, nome de usuário e telefone de login são cada um único **globalmente** (não só
/// por negócio) entre os não excluídos, e pelo menos um deles é obrigatório: o login acontece em <c>app.{dominio}</c>,
/// um único endereço compartilhado por todos os negócios (seção 5), então o identificador sozinho precisa apontar
/// para o usuário (e, por tabela, o negócio) antes de existir qualquer token — ver docs/decisoes.md.
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

    /// <summary>
    /// E-mail normalizado (minúsculas) ou nulo: o usuário pode entrar só por nome de usuário ou telefone. Só vale para
    /// recuperar senha e receber alertas quando <see cref="EmailVerificadoEm"/> está preenchido.
    /// </summary>
    public string? Email { get; private set; }

    /// <summary>Quando o e-mail foi confirmado (código, convite usado ou cadastro). Qualquer troca do e-mail zera.</summary>
    public DateTimeOffset? EmailVerificadoEm { get; private set; }

    public bool EmailVerificado => Email is not null && EmailVerificadoEm is not null;

    /// <summary>Nome de usuário para entrar (único entre não excluídos, no sistema todo).</summary>
    public NomeUsuario? NomeUsuario { get; private set; }

    /// <summary>
    /// Telefone para entrar, sempre na forma canônica (<see cref="TelefoneE164.FormaCanonica"/>). **Não** é o
    /// <see cref="Telefone"/> de contato (texto livre, pode repetir): só vira login por pedido explícito.
    /// </summary>
    public TelefoneE164? TelefoneLogin { get; private set; }

    /// <summary>Verdadeiro quando o administrador definiu a senha; o próprio usuário trocar (ou usar link/convite) zera.</summary>
    public bool SenhaDefinidaPorTerceiro { get; private set; }

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

    /// <summary>
    /// Comissão (%) sobre as vendas de produto de quem vende sem cadastro de profissional (ex.: Recepcionista,
    /// seção 7). Com vínculo, vale o percentual do <c>Profissional</c>.
    /// </summary>
    public decimal PercentualComissaoProdutoVenda { get; private set; }

    /// <summary>
    /// Entra no fechamento por quinzena (comissão de produto − vale/consumo) quando vende sem cadastro de profissional.
    /// Com vínculo, vale o do <c>Profissional</c>.
    /// </summary>
    public bool AcertoPorQuinzena { get; private set; }

    public IReadOnlyCollection<UsuarioPermissao> Permissoes => _permissoes.AsReadOnly();

    protected Usuario()
    {
    }

    private Usuario(Guid negocioId, string nome, string? email, Perfil perfil, string senhaHash)
    {
        NegocioId = negocioId;
        Nome = nome;
        Email = email;
        Perfil = perfil;
        SenhaHash = senhaHash;
    }

    /// <summary>
    /// Criação só com e-mail (obrigatório). O e-mail nasce **não verificado**; quem já o confirmou
    /// (cadastro, semeador) chama <see cref="ConfirmarEmail"/> em seguida.
    /// </summary>
    public static Usuario Criar(
        Guid negocioId, string nome, string email, Perfil perfil, string senhaHash,
        string? telefone = null, Endereco? endereco = null, CpfProtegido? cpf = null)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        return Criar(negocioId, nome, email, null, null, perfil, senhaHash, telefone, endereco, cpf);
    }

    /// <summary>
    /// Criação com os três identificadores opcionais, mas pelo menos um é obrigatório
    /// (<see cref="IdentificadorObrigatorioException"/>). O e-mail nasce **não verificado**.
    /// </summary>
    public static Usuario Criar(
        Guid negocioId, string nome, string? email, NomeUsuario? nomeUsuario, TelefoneE164? telefoneLogin,
        Perfil perfil, string senhaHash,
        string? telefone = null, Endereco? endereco = null, CpfProtegido? cpf = null,
        bool senhaDefinidaPorTerceiro = false)
    {
        if (string.IsNullOrWhiteSpace(nome))
            throw new ArgumentException("O nome é obrigatório.", nameof(nome));

        if (string.IsNullOrWhiteSpace(senhaHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(senhaHash));

        var emailNormalizado = NormalizarEmail(email);

        if (emailNormalizado is null && nomeUsuario is null && telefoneLogin is null)
            throw new IdentificadorObrigatorioException();

        var usuario = new Usuario(negocioId, nome.Trim(), emailNormalizado, perfil, senhaHash)
        {
            NomeUsuario = nomeUsuario,
            TelefoneLogin = telefoneLogin?.FormaCanonica(),
            SenhaDefinidaPorTerceiro = senhaDefinidaPorTerceiro,
            Telefone = telefone,
            Endereco = endereco ?? Endereco.Vazio,
            Cpf = cpf,
        };

        foreach (var permissao in PermissoesPadrao(perfil))
            usuario._permissoes.Add(new UsuarioPermissao(negocioId, usuario.Id, permissao));

        return usuario;
    }

    /// <summary>
    /// Normaliza (<c>Trim().ToLowerInvariant()</c>); vazio vira nulo; sem <c>@</c> ou inválido lança
    /// <see cref="ArgumentException"/>.
    /// </summary>
    public static string? NormalizarEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        var normalizado = email.Trim().ToLowerInvariant();

        if (!normalizado.Contains('@') || !MailAddress.TryCreate(normalizado, out _))
            throw new ArgumentException("O e-mail é inválido.", nameof(email));

        return normalizado;
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
            Permissao.LancarAtendimentoSemAgendamento,
            Permissao.VenderProdutos,
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

    public void DefinirAcertoPorQuinzena(bool acertoPorQuinzena) => AcertoPorQuinzena = acertoPorQuinzena;

    public void DefinirPercentualComissaoProdutoVenda(decimal percentual) =>
        PercentualComissaoProdutoVenda = ValidacaoPercentual.Comissao(percentual, nameof(percentual));

    /// <param name="definidaPorTerceiro">Verdadeiro quando o administrador define; o próprio usuário (ou link/convite) passa falso.</param>
    public void AlterarSenha(string novoHash, bool definidaPorTerceiro = false)
    {
        if (string.IsNullOrWhiteSpace(novoHash))
            throw new ArgumentException("A senha é obrigatória.", nameof(novoHash));

        SenhaHash = novoHash;
        SenhaDefinidaPorTerceiro = definidaPorTerceiro;
    }

    /// <summary>Equivale a <see cref="DefinirEmailPeloAdministrador"/> com e-mail obrigatório (compatibilidade).</summary>
    public void AlterarEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("O e-mail é obrigatório.", nameof(email));

        DefinirEmailPeloAdministrador(email);
    }

    /// <summary>
    /// E-mail digitado por quem não é o dono do endereço: se mudar, deixa de ser verificado. Nulo remove (só se sobrar
    /// outro identificador). O índice único considera só os não excluídos — quem chama checa a unicidade.
    /// </summary>
    public void DefinirEmailPeloAdministrador(string? email)
    {
        var novo = NormalizarEmail(email);
        if (novo == Email)
            return;

        GarantirIdentificadorApos(novo, NomeUsuario, TelefoneLogin);
        Email = novo;
        EmailVerificadoEm = null;
    }

    /// <summary>
    /// Marca o e-mail como confirmado (código digitado, convite usado ou cadastro). Se for outro endereço, troca-o.
    /// </summary>
    public void ConfirmarEmail(string email, DateTimeOffset agora)
    {
        Email = NormalizarEmail(email) ?? throw new ArgumentException("O e-mail é obrigatório.", nameof(email));
        EmailVerificadoEm = agora;
    }

    public void DefinirNomeUsuario(NomeUsuario? nomeUsuario)
    {
        if (Equals(nomeUsuario, NomeUsuario))
            return;

        GarantirIdentificadorApos(Email, nomeUsuario, TelefoneLogin);
        NomeUsuario = nomeUsuario;
    }

    /// <summary>Guarda sempre a forma canônica (o mesmo celular com e sem o 9º dígito é uma conta só).</summary>
    public void DefinirTelefoneLogin(TelefoneE164? telefoneLogin)
    {
        var canonico = telefoneLogin?.FormaCanonica();
        if (Equals(canonico, TelefoneLogin))
            return;

        GarantirIdentificadorApos(Email, NomeUsuario, canonico);
        TelefoneLogin = canonico;
    }

    private static void GarantirIdentificadorApos(string? email, NomeUsuario? nomeUsuario, TelefoneE164? telefoneLogin)
    {
        if (email is null && nomeUsuario is null && telefoneLogin is null)
            throw new IdentificadorObrigatorioException();
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
    /// continuarem legíveis; e-mail, nome de usuário e telefone de login (liberados para outra conta), telefone,
    /// endereço, CPF e foto são apagados. Sem permissões e sem vínculo com profissional — a conta não entra mais em
    /// lugar nenhum.
    /// </summary>
    public void Excluir(DateTimeOffset agora)
    {
        Ativo = false;
        Excluido = true;
        ExcluidoEm = agora;
        Email = null;
        EmailVerificadoEm = null;
        NomeUsuario = null;
        TelefoneLogin = null;
        Telefone = null;
        Endereco = Endereco.Vazio;
        Cpf = null;
        FotoUrl = null;
        ProfissionalId = null;
        _permissoes.Clear();
    }
}
