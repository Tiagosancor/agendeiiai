namespace Plataforma.Dominio.Usuarios;

/// <summary>
/// Catálogo fixo de permissões (seção 4: "autorização por permissões (policies), não só
/// por perfil"). O administrador concede/revoga por usuário (seção 7) — o <see cref="Perfil"/>
/// só define um conjunto inicial razoável ao criar o usuário (ver <c>PermissoesPadrao</c>
/// em <c>Usuario</c>).
/// </summary>
public enum Permissao
{
    GerenciarUsuarios = 1,
    GerenciarProfissionais = 2,
    GerenciarServicos = 3,
    GerenciarClientes = 4,
    GerenciarConfiguracoesDoNegocio = 5,
    VerAgendaDeOutrosProfissionais = 6,
    GerenciarAgenda = 7,
    VerFinanceiro = 8,
    GerenciarCupons = 9,
    GerenciarFidelidade = 10,

    /// <summary>Corrigir usuários, profissionais e serviços já cadastrados (seção 7). Só o Administrador, por padrão.</summary>
    EditarCadastros = 11,

    /// <summary>Excluir usuários, profissionais e serviços (seção 7). Só o Administrador, por padrão.</summary>
    ExcluirCadastros = 12,
}
