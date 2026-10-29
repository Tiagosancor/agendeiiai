using System.Text;
using System.Text.RegularExpressions;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Última barreira antes do cliente (A6): remove caracteres de controle, links que não sejam o do próprio negócio, números de
/// telefone que não sejam o da instância e limita o tamanho. Puro. Texto vazio no fim = o chamador usa o fallback fixo.
/// Não impede o modelo de errar um horário em texto livre — isso é mitigado por prompt/temperatura e, na Fase 3, pela
/// revalidação do agendamento no servidor.
/// </summary>
public static class SanitizadorRespostaAgente
{
    private static readonly Regex Url = new(
        // Qualquer "rótulo.rótulo" cujo último rótulo comece por letra (domínio sem esquema, de qualquer extensão, incl. punycode),
        // com caminho opcional — sem lista de extensões, que sempre fica para trás (.ai, .zip, .xyz...).
        @"(?:[a-z][a-z0-9+.\-]*://|www\.)[^\s]+|\bwa\.me/[^\s]*|\b[a-z0-9](?:[a-z0-9-]*[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]*[a-z0-9])?)*\.[a-z][a-z0-9-]{1,62}\b(?:[:/][^\s]*)?",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    private static readonly Regex Telefone = new(
        @"\+?\d(?:[\s\-().]*\d){7,}", RegexOptions.CultureInvariant | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    // Datas (2026-10-20, 20/10/2026) não são telefone: ficam guardadas à parte, senão "2026 (2026-10-20)" viraria uma sequência de 8 dígitos.
    private static readonly Regex Data = new(@"\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}/\d{2,4}\b", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex LinhasEmBranco = new(@"\n{3,}", RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <param name="hostPermitido">Host do link público (<c>{slug}.{dominio}</c>); links para ele ficam.</param>
    /// <param name="telefonePermitido">Telefone conectado da instância (qualquer formato); só ele pode aparecer.</param>
    public static string? Sanitizar(string? texto, string hostPermitido, string? telefonePermitido, int maximoCaracteres)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return null;

        var sb = new StringBuilder(texto.Length);
        foreach (var c in texto)
            sb.Append(c is '\n' or '\t' ? c : char.IsControl(c) ? ' ' : c);

        var resultado = sb.ToString().Replace("\r", string.Empty);

        // Links permitidos ficam guardados à parte enquanto os telefones são tratados (o slug pode ter uma sequência de dígitos).
        var protegidos = new List<string>();
        resultado = Url.Replace(resultado, correspondencia =>
        {
            if (EhFimDeFraseSemEspaco(correspondencia.Value))
                return correspondencia.Value;

            if (!PermitirLink(correspondencia.Value, hostPermitido) || EhLinkDeGestao(correspondencia.Value))
                return string.Empty;

            protegidos.Add(correspondencia.Value);
            return "[[P" + (protegidos.Count - 1) + "]]";
        });

        resultado = Data.Replace(resultado, correspondencia =>
        {
            protegidos.Add(correspondencia.Value);
            return "[[P" + (protegidos.Count - 1) + "]]";
        });

        var digitosPermitidos = SoDigitos(telefonePermitido);
        resultado = Telefone.Replace(resultado, correspondencia =>
        {
            var digitos = SoDigitos(correspondencia.Value);
            return digitosPermitidos.Length >= 8 && (digitosPermitidos.EndsWith(digitos, StringComparison.Ordinal) || digitos.EndsWith(digitosPermitidos, StringComparison.Ordinal))
                ? correspondencia.Value
                : "(número omitido)";
        });

        for (var i = 0; i < protegidos.Count; i++)
            resultado = resultado.Replace("[[P" + i + "]]", protegidos[i], StringComparison.Ordinal);

        resultado = LinhasEmBranco.Replace(resultado, "\n\n").Trim();
        if (resultado.Length == 0)
            return null;

        if (resultado.Length > maximoCaracteres)
        {
            var corte = resultado[..maximoCaracteres];
            var ultimoEspaco = corte.LastIndexOfAny([' ', '\n']);
            resultado = (ultimoEspaco > maximoCaracteres / 2 ? corte[..ultimoEspaco] : corte).TrimEnd() + "…";
        }

        return resultado;
    }

    /// <summary>
    /// "agendar.Vamos" (frase seguinte colada ao ponto, sem espaço): um único ponto, só letras, segunda parte iniciada em
    /// maiúscula e sem caminho. Domínio real em minúsculas ("golpe.ai") continua bloqueado; "Golpe.Ai" tem a mesma forma e escapa
    /// — limite aceito, o risco de apagar texto legítimo do cliente pesa mais do que esse caso.
    /// </summary>
    private static bool EhFimDeFraseSemEspaco(string valor)
    {
        var partes = valor.Split('.');
        return partes.Length == 2
            && partes[0].Length > 0 && partes[1].Length > 0
            && partes[0].All(char.IsLetter) && partes[1].All(char.IsLetter)
            && char.IsLower(partes[0][^1]) && char.IsUpper(partes[1][0]);
    }

    /// <summary>Texto fixo nosso que interpola cadastro livre (nome do negócio): mesma barreira, sem limite de tamanho prático.</summary>
    public static string SanitizarTextoFixo(string texto, string linkPermitido, string? telefonePermitido)
    {
        var host = Uri.TryCreate(linkPermitido, UriKind.Absolute, out var uri) ? uri.Authority : linkPermitido;
        return Sanitizar(texto, host, telefonePermitido, 4000) ?? linkPermitido;
    }

    /// <summary>
    /// Fragmento de cadastro livre (nome de serviço, de profissional, do negócio) que vai DENTRO de um texto montado pelo servidor:
    /// passa pela mesma barreira sozinho (URL e telefone saem), para que o texto final possa ser montado depois sem que o filtro
    /// de telefone toque o token do link de gestão. Vazio = nada sobrou.
    /// </summary>
    public static string SanitizarFragmento(string? fragmento, string hostPermitido, string? telefonePermitido, int maximo = 100) =>
        Sanitizar(fragmento, hostPermitido, telefonePermitido, maximo)?.Replace('\n', ' ').Trim() ?? string.Empty;

    private static readonly Regex PromessaDeAgendamento = new(
        @"\b(?:agendad[oa]s?|agendei|marcad[oa]s?|marquei|confirmad[oa]s?|confirmei|reservad[oa]s?|reservei)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>Aviso fixo anexado quando o modelo diz que agendou/confirmou sem que isso tenha acontecido (guarda de promessa falsa).</summary>
    public const string AvisoPromessaFalsa =
        "Atenção: ainda não há agendamento feito por aqui. Ele só vale depois do resumo e da sua resposta SIM.";

    /// <summary>
    /// O texto afirma que algo está agendado/marcado/confirmado/reservado? Heurística de palavras (sem acento também). Quem decide se
    /// a afirmação é falsa é o chamador (houve confirmação recente? preparo nesta rodada?).
    /// </summary>
    public static bool AfirmaAgendamento(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        try
        {
            return PromessaDeAgendamento.IsMatch(SemAcento(texto));
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static string SemAcento(string texto)
    {
        var decomposto = texto.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposto.Length);
        foreach (var c in decomposto)
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        return sb.ToString();
    }

    /// <summary>
    /// G3: o link de gestão (<c>/agendamentos/{token}</c>) é credencial e só os textos fixos do servidor o enviam. Qualquer URL do modelo
    /// com esse caminho sai, mesmo no host do próprio negócio (o modelo pode ter visto o link no que o cliente colou).
    /// </summary>
    private static bool EhLinkDeGestao(string valor) => valor.Contains("/agendamentos/", StringComparison.OrdinalIgnoreCase);

    private static readonly Regex PromessaDeGestao = new(
        @"\b(?:cancelad[oa]s?|cancelei|desmarquei|desmarcad[oa]s?|remarquei|remarcad[oa]s?)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>Aviso fixo anexado quando o modelo diz que cancelou ou remarcou (no modo Link ele nunca faz nenhum dos dois).</summary>
    public const string AvisoGestaoNaoEfetivada =
        "Atenção: não cancelei nem remarquei nada por aqui. Para cancelar ou remarcar, use o link de gestão do seu agendamento.";

    /// <summary>Aviso fixo do modo Cancelar: o cancelamento só vale depois do SIM ao resumo do sistema.</summary>
    public const string AvisoCancelamentoNaoEfetivado =
        "Atenção: ainda não cancelei nada por aqui. O cancelamento só vale depois que você responde SIM ao resumo que o sistema envia.";

    /// <summary>Aviso fixo quando o modelo diz que remarcou: remarcar pelo agente não existe (só o link de gestão).</summary>
    public const string AvisoRemarcacaoNaoEfetivada =
        "Atenção: não remarquei nada por aqui. Para remarcar, use o link de gestão do seu agendamento.";

    /// <summary>Aviso fixo do modo Completo: a remarcação só vale depois do SIM ao resumo do sistema.</summary>
    public const string AvisoRemarcacaoNaoEfetivadaCompleto =
        "Atenção: ainda não remarquei nada por aqui. A remarcação só vale depois que você responde SIM ao resumo que o sistema envia.";

    private static readonly Regex PromessaDeRemarcacao = new(
        @"\b(?:remarquei|remarcad[oa]s?|remarcamos)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    private static readonly Regex PromessaDeCancelamento = new(
        @"\b(?:cancelad[oa]s?|cancelei|cancelamos|desmarquei|desmarcad[oa]s?)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));

    /// <summary>O texto afirma que algo foi REMARCADO? Sempre vale a proteção enquanto remarcar pelo agente não existe.</summary>
    public static bool AfirmaRemarcacao(string? texto) => Casa(PromessaDeRemarcacao, texto);

    /// <summary>O texto afirma que algo foi cancelado/desmarcado?</summary>
    public static bool AfirmaCancelamento(string? texto) => Casa(PromessaDeCancelamento, texto);

    private static bool Casa(Regex regex, string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        try
        {
            return regex.IsMatch(SemAcento(texto));
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    /// <summary>O texto afirma que algo foi cancelado/remarcado/desmarcado? Heurística de palavras (sem acento também).</summary>
    public static bool AfirmaGestaoEfetivada(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
            return false;

        try
        {
            return PromessaDeGestao.IsMatch(SemAcento(texto));
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    private static bool PermitirLink(string valor, string hostPermitido)
    {
        // Pontuação colada ao fim da frase ("...agendar: https://x.com/.") não faz parte do endereço.
        var limpo = valor.TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '"', '\'');
        var semEsquema = limpo;
        var indice = semEsquema.IndexOf("://", StringComparison.Ordinal);
        if (indice >= 0)
            semEsquema = semEsquema[(indice + 3)..];

        var fim = semEsquema.IndexOfAny(['/', '?', '#']);
        var host = fim >= 0 ? semEsquema[..fim] : semEsquema;

        // "usuario@host" nunca vale; a porta é ignorada (em dev o link público leva :3000 e o modelo pode omitir).
        if (host.Contains('@'))
            return false;

        return string.Equals(SemPorta(host), SemPorta(hostPermitido), StringComparison.OrdinalIgnoreCase);
    }

    private static string SemPorta(string host)
    {
        var indice = host.LastIndexOf(':');
        return indice > 0 ? host[..indice] : host;
    }

    private static string SoDigitos(string? texto) => string.IsNullOrEmpty(texto) ? string.Empty : new string(texto.Where(char.IsAsciiDigit).ToArray());
}
