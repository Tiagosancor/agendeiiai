using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plataforma.Infraestrutura.Idempotencia;

/// <summary>
/// Hash canônico da <i>intenção</i> da requisição (DTO + valores de rota), nunca dos bytes crus do JSON: ordem das propriedades,
/// espaços e a escala de decimais (<c>10</c> × <c>10.00</c>) ou o fuso de um instante (<c>-03:00</c> × <c>Z</c>) não mudam o hash;
/// um valor realmente diferente muda. O corpo em si nunca é guardado — só este SHA-256.
/// </summary>
public static class HashDeRequisicao
{
    private static readonly JsonSerializerOptions Opcoes = Criar();

    public static string Calcular(object? dto, IReadOnlyDictionary<string, object?>? rota = null)
    {
        var rotaOrdenada = (rota ?? new Dictionary<string, object?>())
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ToDictionary(p => p.Key, p => p.Value);
        var json = JsonSerializer.Serialize(new { rota = rotaOrdenada, dto }, Opcoes);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
    }

    /// <summary>Id estável (16 bytes do SHA-256) derivado da chave e da finalidade, para entidades criadas antes da transação.</summary>
    public static Guid IdDerivado(Guid negocioId, string principal, string escopo, string chave, string finalidade)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{negocioId:N}|{principal}|{escopo}|{chave}|{finalidade}"));
        return new Guid(bytes.AsSpan(0, 16));
    }

    private static JsonSerializerOptions Criar()
    {
        var opcoes = new JsonSerializerOptions { PropertyNamingPolicy = null, WriteIndented = false };
        opcoes.Converters.Add(new DecimalCanonico());
        opcoes.Converters.Add(new InstanteCanonico());
        return opcoes;
    }

    private sealed class DecimalCanonico : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opcoes) => reader.GetDecimal();

        // Dividir por 1,000…0 remove os zeros à direita da escala (10.00 → 10); "G29" escreve o número sem notação científica.
        public override void Write(Utf8JsonWriter writer, decimal valor, JsonSerializerOptions opcoes) =>
            writer.WriteRawValue((valor / 1.0000000000000000000000000000m).ToString("G29", CultureInfo.InvariantCulture));
    }

    private sealed class InstanteCanonico : JsonConverter<DateTimeOffset>
    {
        public override DateTimeOffset Read(ref Utf8JsonReader reader, Type tipo, JsonSerializerOptions opcoes) => reader.GetDateTimeOffset();

        public override void Write(Utf8JsonWriter writer, DateTimeOffset valor, JsonSerializerOptions opcoes) =>
            writer.WriteStringValue(valor.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFFZ", CultureInfo.InvariantCulture));
    }
}
