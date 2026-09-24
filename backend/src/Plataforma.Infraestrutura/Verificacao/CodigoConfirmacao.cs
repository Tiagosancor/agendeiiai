using System.Security.Cryptography;
using System.Text;

namespace Plataforma.Infraestrutura.Verificacao;

/// <summary>Geração e hash dos códigos de 6 dígitos (seção 8.1.2) — o mesmo mecanismo para o cliente final e para o cadastro.</summary>
public static class CodigoConfirmacao
{
    public static string Gerar() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    /// <summary>HMAC-SHA256 com pimenta de configuração: nunca o código em texto puro no banco.</summary>
    public static string Hash(string chaveHmac, string codigo)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(chaveHmac));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(codigo)));
    }
}
