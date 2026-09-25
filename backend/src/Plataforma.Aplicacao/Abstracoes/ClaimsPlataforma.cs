namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>Nomes de claim compartilhados entre quem emite o JWT (Infraestrutura) e quem o lê (Api).</summary>
public static class ClaimsPlataforma
{
    public const string NegocioId = "negocio_id";
    public const string Perfil = "perfil";
    public const string Permissao = "permissao";

    /// <summary>Valor da claim <see cref="Perfil"/> no token da administração da plataforma.</summary>
    public const string PerfilAdministradorPlataforma = "AdministradorPlataforma";

    /// <summary>Esquema de autenticação próprio da administração da plataforma (audiência <c>{Jwt:Audiencia}:plataforma</c>).</summary>
    public const string EsquemaPlataforma = "Plataforma";

    public const string PoliticaAdministradorPlataforma = "AdministradorPlataforma";

    public const string PoliticaSomenteAdministradorNegocio = "SomenteAdministradorNegocio";
}
