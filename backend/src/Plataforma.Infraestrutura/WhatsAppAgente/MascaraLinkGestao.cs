using System.Text.RegularExpressions;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// G3: o link de gestão do agendamento (<c>/agendamentos/{token}</c>) e qualquer JWT solto (<c>eyJ...</c>) são CREDENCIAL — quem tem o
/// token cancela/remarca. Se o cliente colar um no WhatsApp, o texto vira <see cref="Marca"/> antes de ser gravado em
/// <c>mensagens_whatsapp</c> (e antes de ir à IA). Puro.
/// </summary>
public static class MascaraLinkGestao
{
    public const string Marca = "[link de gestão removido]";

    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);
    private const RegexOptions Opcoes = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Compiled;

    // URL inteira (com ou sem esquema/porta/host) cujo caminho passa por /agendamentos/ com algo depois.
    private static readonly Regex Link = new(
        @"(?:https?://)?(?:[A-Za-z0-9\-]+\.)*[A-Za-z0-9\-]+(?::\d+)?/agendamentos/[A-Za-z0-9_\-.~%]+", Opcoes, Limite);

    // Caminho sem host ("/agendamentos/eyJ...").
    private static readonly Regex Caminho = new(@"/agendamentos/[A-Za-z0-9_\-.~%]+", Opcoes, Limite);

    // JWT: três segmentos base64url, o primeiro começando por eyJ (cabeçalho JSON).
    private static readonly Regex Jwt = new(@"eyJ[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]{5,}\.[A-Za-z0-9_\-]*", Opcoes, Limite);

    public static string Aplicar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return texto ?? string.Empty;

        try
        {
            var resultado = Link.Replace(texto, Marca);
            resultado = Caminho.Replace(resultado, Marca);
            return Jwt.Replace(resultado, Marca);
        }
        catch (RegexMatchTimeoutException)
        {
            // Texto patológico: na dúvida, não guarda nada em vez de guardar uma credencial.
            return Marca;
        }
    }
}
