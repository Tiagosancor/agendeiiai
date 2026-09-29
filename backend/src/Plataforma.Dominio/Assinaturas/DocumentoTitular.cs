using System.Text.RegularExpressions;
using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Assinaturas;

/// <summary>
/// CPF ou CNPJ de quem paga a assinatura, exigido pelo gateway de cobrança. Vai direto para o gateway: no nosso banco
/// fica só o <see cref="Mascarado"/> (decisão do dono), para exibir na tela de assinatura.
/// </summary>
public sealed partial class DocumentoTitular
{
    public string Digitos { get; }

    public bool EhCnpj => Digitos.Length == 14;

    private DocumentoTitular(string digitos) => Digitos = digitos;

    /// <summary>Lança <see cref="ArgumentException"/> para CPF ou CNPJ inválido.</summary>
    public static DocumentoTitular Criar(string? valor)
    {
        var digitos = SomenteDigitos().Replace(valor ?? string.Empty, string.Empty);

        if (digitos.Length == 11)
        {
            if (!Cpf.TentarCriar(digitos, out _))
                throw new ArgumentException("CPF inválido.", nameof(valor));
            return new DocumentoTitular(digitos);
        }

        if (digitos.Length == 14 && CnpjValido(digitos))
            return new DocumentoTitular(digitos);

        throw new ArgumentException(digitos.Length == 14 ? "CNPJ inválido." : "Informe um CPF (11 dígitos) ou CNPJ (14 dígitos).", nameof(valor));
    }

    /// <summary>CPF "***.456.789-**"; CNPJ "**.345.678/0001-**" — mesmo critério do CPF (seção 8.4).</summary>
    public string Mascarado() => EhCnpj
        ? $"**.{Digitos[2..5]}.{Digitos[5..8]}/{Digitos[8..12]}-**"
        : $"***.{Digitos[3..6]}.{Digitos[6..9]}-**";

    private static bool CnpjValido(string d)
    {
        if (d.Distinct().Count() == 1)
            return false;

        static int Digito(string baseCnpj, int[] pesos)
        {
            var soma = 0;
            for (var i = 0; i < pesos.Length; i++)
                soma += (baseCnpj[i] - '0') * pesos[i];
            var resto = soma % 11;
            return resto < 2 ? 0 : 11 - resto;
        }

        int[] pesos1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] pesos2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        return Digito(d, pesos1) == d[12] - '0' && Digito(d, pesos2) == d[13] - '0';
    }

    [GeneratedRegex(@"\D")]
    private static partial Regex SomenteDigitos();
}
