using System.Text.Json;
using System.Text.Json.Serialization;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// `HttpClient` de teste usa `JsonSerializerOptions.Default` (sem o `JsonStringEnumConverter`
/// que a API registra via `AddJsonOptions`), então desserializar uma resposta com enum
/// (ex.: <c>UsuarioDetalhe.Perfil</c>) direto com `GetFromJsonAsync&lt;T&gt;()` falha. Use
/// esta instância nas chamadas que envolvem DTOs com enum bruto no corpo.
/// </summary>
public static class OpcoesJsonTeste
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
