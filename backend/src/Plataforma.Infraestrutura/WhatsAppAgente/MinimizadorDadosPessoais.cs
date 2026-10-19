using System.Text.RegularExpressions;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Minimização de dados pessoais no texto que o CLIENTE escreveu, aplicada ao histórico e à mensagem atual ANTES de montar o
/// pedido à IA. É heurística por padrões (e-mail, CPF, CNPJ, telefone), não garantia: não pega número escrito por extenso,
/// com letras no meio, ofuscado ("arroba", "ponto"), nome, endereço, data de nascimento, número de cartão ou documento fora
/// do formato. O que o cliente digita vai para o provedor de IA como qualquer outro texto; a política de privacidade deve
/// dizer isso. Puro.
/// </summary>
public static class MinimizadorDadosPessoais
{
    private static readonly TimeSpan Limite = TimeSpan.FromSeconds(1);
    private const RegexOptions Opcoes = RegexOptions.CultureInvariant | RegexOptions.Compiled;

    private static readonly Regex Email = new(
        @"[A-Za-z0-9._%+\-]+@[A-Za-z0-9\-]+(?:\.[A-Za-z0-9\-]+)+", Opcoes, Limite);

    private static readonly Regex Cnpj = new(@"(?<!\d)\d{2}\.?\d{3}\.?\d{3}\s?/?\s?\d{4}\s?-?\s?\d{2}(?!\d)", Opcoes, Limite);

    private static readonly Regex Cpf = new(@"(?<!\d)\d{3}\.?\d{3}\.?\d{3}\s?-?\s?\d{2}(?!\d)", Opcoes, Limite);

    // 8 dígitos ou mais, com espaço, hífen, ponto ou parênteses no meio (inclui +55 e DDD).
    private static readonly Regex Telefone = new(@"(?<!\d)\+?\d(?:[\s\-().]*\d){7,}(?!\d)", Opcoes, Limite);

    // Datas não são telefone (2026-10-20, 20/10/2026): ficam guardadas à parte.
    private static readonly Regex Data = new(@"\b\d{4}-\d{2}-\d{2}\b|\b\d{1,2}/\d{1,2}/\d{2,4}\b", Opcoes, Limite);

    public const string MarcaEmail = "[e-mail removido]";
    public const string MarcaDocumento = "[documento removido]";
    public const string MarcaTelefone = "[telefone removido]";

    public static string Minimizar(string? texto)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        try
        {
            var resultado = Email.Replace(texto, MarcaEmail);
            resultado = Cnpj.Replace(resultado, MarcaDocumento);
            resultado = Cpf.Replace(resultado, MarcaDocumento);

            var datas = new List<string>();
            resultado = Data.Replace(resultado, correspondencia =>
            {
                datas.Add(correspondencia.Value);
                return "[[D" + (datas.Count - 1) + "]]";
            });

            resultado = Telefone.Replace(resultado, MarcaTelefone);

            for (var i = 0; i < datas.Count; i++)
                resultado = resultado.Replace("[[D" + i + "]]", datas[i], StringComparison.Ordinal);

            return resultado;
        }
        catch (RegexMatchTimeoutException)
        {
            // Texto patológico: na dúvida, não manda nada em vez de mandar sem minimizar.
            return string.Empty;
        }
    }
}
