namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>Nomes de claim compartilhados entre quem emite o JWT (Infraestrutura) e quem o lê (Api).</summary>
public static class ClaimsPlataforma
{
    public const string NegocioId = "negocio_id";
    public const string Perfil = "perfil";
    public const string Permissao = "permissao";
}
