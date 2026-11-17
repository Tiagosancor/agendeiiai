namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>Nomes de claim compartilhados entre quem emite o JWT (Infraestrutura) e quem o lê (Api).</summary>
public static class ClaimsPlataforma
{
    public const string NegocioId = "negocio_id";
    public const string Perfil = "perfil";
    public const string Permissao = "permissao";

    /// <summary>Nome de usuário (login) — só quando a conta tem um.</summary>
    public const string NomeUsuario = "nome_usuario";

    /// <summary>Nome da pessoa (<c>Usuario.Nome</c>), último recurso do rótulo de autoria.</summary>
    public const string NomeExibicao = "name";

    /// <summary>Versão das sessões do usuário no momento da emissão (<c>Usuario.VersaoSessao</c>); ausente em tokens antigos = 0.</summary>
    public const string VersaoSessao = "sv";

    /// <summary>Valor da claim <see cref="Perfil"/> no token da administração da plataforma.</summary>
    public const string PerfilAdministradorPlataforma = "AdministradorPlataforma";

    /// <summary>Esquema de autenticação próprio da administração da plataforma (audiência <c>{Jwt:Audiencia}:plataforma</c>).</summary>
    public const string EsquemaPlataforma = "Plataforma";

    public const string PoliticaAdministradorPlataforma = "AdministradorPlataforma";

    public const string PoliticaSomenteAdministradorNegocio = "SomenteAdministradorNegocio";
}
