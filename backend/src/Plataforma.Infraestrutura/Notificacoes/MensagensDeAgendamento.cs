using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Comum;

namespace Plataforma.Infraestrutura.Notificacoes;

public sealed record EmailMontado(string Assunto, string CorpoHtml);

/// <summary>
/// Texto de cada mensagem de agendamento, num lugar só: o envio direto (<see cref="Notificador"/>) e o despachante da outbox montam a
/// mesma mensagem a partir daqui, então a escolha entre os dois caminhos nunca muda o que o cliente lê.
/// </summary>
public static class MensagensDeAgendamento
{
    /// <summary>
    /// Envolve o conteúdo (que fala sempre do NEGÓCIO — seção 5) com o cabeçalho e o rodapé da marca do PRODUTO (seção 5.1) — o único
    /// lugar em que o nome do produto aparece nesses e-mails. Estilo inline (obrigatório em e-mail: a maioria dos clientes ignora
    /// `<style>`) e sem web font nem imagem externa. Sem <paramref name="nomeNegocio"/>, é um e-mail do próprio produto para o negócio.
    /// </summary>
    public static string Envelope(string nomeProduto, string? nomeNegocio, string conteudoHtml) => $"""
        <div style="font-family: Arial, Helvetica, sans-serif; max-width: 480px; margin: 0 auto; color: #1c1c1a;">
          <div style="background-color: #1e2a38; padding: 18px 24px; border-radius: 8px 8px 0 0;">
            <span style="font-family: Georgia, 'Times New Roman', serif; font-weight: 700; font-size: 20px; color: #faf9f6;">agendeiiai</span>
          </div>
          <div style="background-color: #ffffff; padding: 24px; border: 1px solid #e5e5e5; border-top: none; border-radius: 0 0 8px 8px;">
            {conteudoHtml}
          </div>
          <p style="font-size: 11px; color: #9a9a9a; text-align: center; margin-top: 16px;">
            {(nomeNegocio is null ? $"Enviado por {nomeProduto}." : $"Enviado por {nomeProduto} em nome de {nomeNegocio}.")}
          </p>
        </div>
        """;

    public static EmailMontado EmailConfirmacao(string nomeProduto, Negocio negocio, DadosNotificacaoAgendamento dados) => new(
        $"Agendamento confirmado — {negocio.NomeExibido}",
        Envelope(nomeProduto, negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Seu agendamento em <strong>{negocio.NomeExibido}</strong> está confirmado.</p>
            <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><strong>Total:</strong> {FormatacaoBrasil.Reais(dados.Total)}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """));

    public static string WhatsAppConfirmacao(Negocio negocio, DadosNotificacaoAgendamento dados) =>
        $"Agendamento confirmado em {negocio.NomeExibido} para {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}. Total {FormatacaoBrasil.Reais(dados.Total)}.";

    public static EmailMontado EmailLembrete(string nomeProduto, Negocio negocio, DadosNotificacaoAgendamento dados) => new(
        $"Lembrete do seu agendamento — {negocio.NomeExibido}",
        Envelope(nomeProduto, negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Lembrete do seu agendamento em <strong>{negocio.NomeExibido}</strong>.</p>
            <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """));

    public static string WhatsAppLembrete(Negocio negocio, DadosNotificacaoAgendamento dados) =>
        $"Lembrete: você tem um agendamento em {negocio.NomeExibido} em {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}.";

    public static EmailMontado EmailCancelamento(string nomeProduto, Negocio negocio, DadosNotificacaoAgendamento dados)
    {
        var nomeCliente = System.Net.WebUtility.HtmlEncode(dados.NomeCliente);
        var servicos = System.Net.WebUtility.HtmlEncode(string.Join(", ", dados.Servicos));

        return new EmailMontado(
            $"Agendamento cancelado — {negocio.NomeExibido}",
            Envelope(nomeProduto, negocio.NomeExibido, $"""
                <p>Olá, {nomeCliente}. Infelizmente, <strong>{negocio.NomeExibido}</strong> precisou cancelar o seu agendamento.</p>
                <p><strong>Quando seria:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
                <p><strong>Serviços:</strong> {servicos}</p>
                <p>Se quiser, é só agendar um novo horário pela página do negócio.</p>
                """));
    }

    public static string WhatsAppCancelamento(Negocio negocio, DadosNotificacaoAgendamento dados) =>
        $"{negocio.NomeExibido} precisou cancelar o seu agendamento de {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}. Se quiser, agende um novo horário pela página do negócio.";

    public static (string Assunto, string Titulo) TituloDoAvisoAoProfissional(EventoAgendamentoProfissional evento) => evento switch
    {
        EventoAgendamentoProfissional.Novo => ("Novo agendamento", "Novo agendamento"),
        EventoAgendamentoProfissional.Remarcado => ("Agendamento remarcado", "Agendamento remarcado"),
        EventoAgendamentoProfissional.Cancelado => ("Agendamento cancelado", "Agendamento cancelado"),
        _ => ("Agendamento", "Agendamento"),
    };

    public static EmailMontado EmailAvisoProfissional(string nomeProduto, Negocio negocio, DadosNotificacaoProfissional dados)
    {
        var (assunto, titulo) = TituloDoAvisoAoProfissional(dados.Evento);

        return new EmailMontado(
            $"{assunto} — {negocio.NomeExibido}",
            Envelope(nomeProduto, negocio.NomeExibido, $"""
                <p><strong>{titulo}</strong> em {negocio.NomeExibido}.</p>
                <p><strong>Cliente:</strong> {dados.NomeCliente}</p>
                <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
                <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
                {(string.IsNullOrWhiteSpace(dados.Observacoes) ? "" : $"<p><strong>Observações:</strong> {dados.Observacoes}</p>")}
                {(dados.Forcado ? "<p><em>Agendamento forçado pelo negócio, fora das regras de horário.</em></p>" : "")}
                """));
    }

    public static string WhatsAppAvisoProfissional(Negocio negocio, DadosNotificacaoProfissional dados)
    {
        var (_, titulo) = TituloDoAvisoAoProfissional(dados.Evento);
        return $"{titulo} em {negocio.NomeExibido}: {dados.NomeCliente}, {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)} — {string.Join(", ", dados.Servicos)}{(dados.Forcado ? " (agendamento forçado, fora das regras de horário)" : "")}.";
    }
}
