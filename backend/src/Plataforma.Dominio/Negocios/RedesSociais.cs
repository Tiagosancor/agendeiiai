namespace Plataforma.Dominio.Negocios;

public sealed record RedesSociais(string? Instagram, string? Facebook, string? WhatsApp)
{
    public static RedesSociais Vazio { get; } = new(null, null, null);
}
