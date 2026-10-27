using System.Globalization;
using System.Text.Json;
using Plataforma.Dominio.WhatsAppAgente;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>Argumentos de <c>preparar_agendamento</c> depois da leitura estrita. Nada aqui diz respeito a negócio, telefone ou cliente.</summary>
public sealed record PedidoDeAgendamento(Guid ServicoId, Guid ProfissionalId, DateOnly Data, TimeOnly Hora, string? NomeCliente);

/// <summary>
/// Leitura ESTRITA dos argumentos de <c>preparar_agendamento</c> (à mão sobre <see cref="JsonElement"/>): só os cinco campos
/// conhecidos, tipos exatos; <c>forcar</c>, <c>telefone</c>, <c>cliente_id</c>, <c>negocio_id</c> etc. invalidam o pedido. Puro.
/// O nome só é validado depois, quando se sabe que o cliente é novo (<see cref="ValidarNome"/>).
/// </summary>
public static class ArgumentosPrepararAgendamento
{
    public static readonly string[] Campos = ["servico_id", "profissional_id", "data", "hora", "nome_cliente"];

    /// <summary>Devolve o pedido, ou o código de erro de <see cref="ErrosFerramenta"/>.</summary>
    public static (PedidoDeAgendamento? Pedido, string? Erro) Ler(JsonElement argumentos)
    {
        var leitor = new LeitorArgumentos(argumentos, Campos);
        var servicoId = leitor.Identificador("servico_id");
        var profissionalId = leitor.Identificador("profissional_id");
        var data = leitor.Data("data");
        var horaTexto = leitor.Texto("hora");
        var nome = leitor.Texto("nome_cliente");

        if (!leitor.Valido || servicoId is null || profissionalId is null || (data is null && !leitor.DataMalFormada) || horaTexto is null)
            return (null, ErrosFerramenta.ArgumentosInvalidos);
        if (leitor.DataMalFormada || data is null)
            return (null, ErrosFerramenta.DataInvalida);

        if (!TimeOnly.TryParseExact(horaTexto, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
            return (null, ErrosFerramenta.HoraInvalida);

        return (new PedidoDeAgendamento(servicoId.Value, profissionalId.Value, data.Value, hora,
            string.IsNullOrWhiteSpace(nome) ? null : nome), null);
    }

    /// <summary>Mesma regra de domínio que o serviço aplica na confirmação (<see cref="NomeClienteWhatsApp"/>).</summary>
    public static bool ValidarNome(string? bruto, out string nome) => NomeClienteWhatsApp.TentarNormalizar(bruto, out nome);
}
