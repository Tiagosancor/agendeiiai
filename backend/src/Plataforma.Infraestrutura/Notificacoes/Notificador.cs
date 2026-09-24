using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>
/// Orquestra os canais por evento (seção 9). Falha de notificação nunca derruba o fluxo
/// que a chamou (agendar continua valendo mesmo que o e-mail falhe) — cada envio é melhor
/// esforço, com a falha só registrada em log, nunca propagada.
/// </summary>
public sealed class Notificador : INotificador
{
    private readonly IEmailSender _email;
    private readonly IMensageriaWhatsApp _whatsApp;
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesVerificacao _opcoesVerificacao;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly ILogger<Notificador> _logger;

    public Notificador(
        IEmailSender email, IMensageriaWhatsApp whatsApp, PlataformaDbContext dbContext,
        IContextoNegocio contextoNegocio, IOptions<OpcoesVerificacao> opcoesVerificacao,
        IOptions<OpcoesMarca> opcoesMarca, ILogger<Notificador> logger)
    {
        _email = email;
        _whatsApp = whatsApp;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoesVerificacao = opcoesVerificacao.Value;
        _opcoesMarca = opcoesMarca.Value;
        _logger = logger;
    }

    public async Task EnviarCodigoVerificacaoAsync(
        TelefoneE164 telefone, string? email, string codigo, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);
        var mensagem = $"Seu código de confirmação em {negocio.NomeExibido}: {codigo}. Válido por 5 minutos.";

        var tarefas = new List<Task>();

        // Teto diário de WhatsApp por negócio (seção 8.1.5) — cada código solicitado tenta 1
        // envio por WhatsApp, então a contagem de códigos criados nas últimas 24h aproxima
        // bem quantos WhatsApp já foram tentados hoje; ao estourar, só e-mail.
        var codigosHoje = await _dbContext.CodigosVerificacao.CountAsync(
            c => c.NegocioId == negocio.Id && c.CriadoEm >= DateTimeOffset.UtcNow.AddDays(-1), cancellationToken);

        if (codigosHoje <= _opcoesVerificacao.MaximoWhatsAppPorNegocioPorDia)
            tarefas.Add(ExecutarSemFalharAsync(_whatsApp.EnviarAsync(telefone, mensagem, cancellationToken), "WhatsApp/código"));
        else
            _logger.LogInformation("Teto diário de WhatsApp do negócio {NegocioId} atingido — código enviado só por e-mail.", negocio.Id);

        if (!string.IsNullOrWhiteSpace(email))
        {
            var corpo = Envelope(negocio.NomeExibido,
                $"<p>Seu código de confirmação em <strong>{negocio.NomeExibido}</strong>:</p><h2>{codigo}</h2><p>Válido por 5 minutos.</p>");
            tarefas.Add(ExecutarSemFalharAsync(
                _email.EnviarAsync(email, $"Seu código de confirmação — {negocio.NomeExibido}", corpo, cancellationToken), "E-mail/código"));
        }

        await Task.WhenAll(tarefas);
    }

    public async Task EnviarConfirmacaoAgendamentoAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Seu agendamento em <strong>{negocio.NomeExibido}</strong> está confirmado.</p>
            <p><strong>Quando:</strong> {dados.Inicio:dd/MM/yyyy HH:mm}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><strong>Total:</strong> R$ {dados.Total:F2}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """);

        var tarefas = new List<Task>();

        if (!string.IsNullOrWhiteSpace(dados.EmailCliente))
        {
            tarefas.Add(ExecutarSemFalharAsync(
                _email.EnviarAsync(dados.EmailCliente, $"Agendamento confirmado — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/confirmação"));
        }

        if (negocio.WhatsAppAtivoParaConfirmacoes)
        {
            var mensagem = $"Agendamento confirmado em {negocio.NomeExibido} para {dados.Inicio:dd/MM/yyyy HH:mm}. Total R$ {dados.Total:F2}.";
            tarefas.Add(ExecutarSemFalharAsync(_whatsApp.EnviarAsync(dados.TelefoneCliente, mensagem, cancellationToken), "WhatsApp/confirmação"));
        }

        await Task.WhenAll(tarefas);
    }

    public async Task EnviarMensagemContatoAsync(DadosNotificacaoContato dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(negocio.EmailContato))
        {
            _logger.LogWarning("Negócio {NegocioId} recebeu uma mensagem de contato sem e-mail de contato configurado.", negocio.Id);
            return;
        }

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p><strong>Nome:</strong> {dados.NomeRemetente}</p>
            <p><strong>Telefone:</strong> {dados.TelefoneRemetente ?? "—"}</p>
            <p><strong>E-mail:</strong> {dados.EmailRemetente ?? "—"}</p>
            <p><strong>Mensagem:</strong></p>
            <p>{dados.Mensagem}</p>
            """);

        await ExecutarSemFalharAsync(
            _email.EnviarAsync(negocio.EmailContato, $"Nova mensagem pelo site — {negocio.NomeExibido}", corpo, cancellationToken),
            "E-mail/fale-conosco");
    }

    public async Task EnviarNotificacaoProfissionalAsync(DadosNotificacaoProfissional dados, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(dados.EmailProfissional))
            return;

        var negocio = await ObterNegocioAsync(cancellationToken);

        var (assunto, titulo) = dados.Evento switch
        {
            EventoAgendamentoProfissional.Novo => ("Novo agendamento", "Novo agendamento"),
            EventoAgendamentoProfissional.Remarcado => ("Agendamento remarcado", "Agendamento remarcado"),
            EventoAgendamentoProfissional.Cancelado => ("Agendamento cancelado", "Agendamento cancelado"),
            _ => ("Agendamento", "Agendamento"),
        };

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p><strong>{titulo}</strong> em {negocio.NomeExibido}.</p>
            <p><strong>Cliente:</strong> {dados.NomeCliente}</p>
            <p><strong>Quando:</strong> {dados.Inicio:dd/MM/yyyy HH:mm}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            {(string.IsNullOrWhiteSpace(dados.Observacoes) ? "" : $"<p><strong>Observações:</strong> {dados.Observacoes}</p>")}
            """);

        await ExecutarSemFalharAsync(
            _email.EnviarAsync(dados.EmailProfissional, $"{assunto} — {negocio.NomeExibido}", corpo, cancellationToken),
            "E-mail/profissional");
    }

    public async Task EnviarLembreteAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Lembrete do seu agendamento em <strong>{negocio.NomeExibido}</strong>.</p>
            <p><strong>Quando:</strong> {dados.Inicio:dd/MM/yyyy HH:mm}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """);

        var tarefas = new List<Task>();

        if (!string.IsNullOrWhiteSpace(dados.EmailCliente))
        {
            tarefas.Add(ExecutarSemFalharAsync(
                _email.EnviarAsync(dados.EmailCliente, $"Lembrete do seu agendamento — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/lembrete"));
        }

        if (negocio.WhatsAppAtivoParaConfirmacoes)
        {
            var mensagem = $"Lembrete: você tem um agendamento em {negocio.NomeExibido} em {dados.Inicio:dd/MM/yyyy HH:mm}.";
            tarefas.Add(ExecutarSemFalharAsync(_whatsApp.EnviarAsync(dados.TelefoneCliente, mensagem, cancellationToken), "WhatsApp/lembrete"));
        }

        await Task.WhenAll(tarefas);
    }

    public async Task EnviarAvisoAssinaturaAsync(DadosAvisoAssinatura dados, CancellationToken cancellationToken = default)
    {
        var nomeNegocio = System.Net.WebUtility.HtmlEncode(dados.NomeNegocio);
        var quando = dados.DiasRestantes == 1 ? "amanhã" : $"em {dados.DiasRestantes} dias";
        var (assunto, abertura) = dados.EmTeste
            ? ($"Seu teste grátis termina {quando}", $"O teste grátis de <strong>{nomeNegocio}</strong> termina {quando} ({dados.Prazo:dd/MM/yyyy}).")
            : ($"Sua assinatura vence {quando}", $"A assinatura de <strong>{nomeNegocio}</strong> vence {quando} ({dados.Prazo:dd/MM/yyyy}).");

        var corpo = Envelope(null, $"""
            <p>{abertura}</p>
            <p>Plano {dados.NomePlano}: R$ {dados.ValorDoPeriodo:F2}. Para continuar recebendo agendamentos sem interrupção, é só assinar.</p>
            <p><a href="{dados.LinkAssinatura}">Assinar agora</a></p>
            """);

        await Task.WhenAll(dados.EmailsAdministradores.Select(email =>
            ExecutarSemFalharAsync(_email.EnviarAsync(email, $"{assunto} — {_opcoesMarca.NomeProduto}", corpo, cancellationToken), "E-mail/aviso-assinatura")));
    }

    /// <summary>
    /// Envolve o conteúdo (que fala sempre do NEGÓCIO — seção 5) com o cabeçalho e o rodapé
    /// da marca do PRODUTO (seção 5.1) — o único lugar em que o nome do produto aparece
    /// nesses e-mails. Estilo inline (obrigatório em e-mail: a maioria dos clientes ignora
    /// `<style>`) e sem web font nem imagem externa — clientes de e-mail bloqueiam imagem
    /// por padrão e não têm suporte confiável a SVG (Outlook não suporta de jeito nenhum),
    /// então o "carimbado" da marca vem só da pilha de fontes serifadas do sistema.
    /// Sem <paramref name="nomeNegocio"/>, é um e-mail do próprio produto para o negócio.
    /// </summary>
    private string Envelope(string? nomeNegocio, string conteudoHtml) => $"""
        <div style="font-family: Arial, Helvetica, sans-serif; max-width: 480px; margin: 0 auto; color: #1c1c1a;">
          <div style="background-color: #1e2a38; padding: 18px 24px; border-radius: 8px 8px 0 0;">
            <span style="font-family: Georgia, 'Times New Roman', serif; font-weight: 700; font-size: 20px; color: #faf9f6;">agendeiiai</span>
          </div>
          <div style="background-color: #ffffff; padding: 24px; border: 1px solid #e5e5e5; border-top: none; border-radius: 0 0 8px 8px;">
            {conteudoHtml}
          </div>
          <p style="font-size: 11px; color: #9a9a9a; text-align: center; margin-top: 16px;">
            {(nomeNegocio is null ? $"Enviado por {_opcoesMarca.NomeProduto}." : $"Enviado por {_opcoesMarca.NomeProduto} em nome de {nomeNegocio}.")}
          </p>
        </div>
        """;

    private Task<Negocio> ObterNegocioAsync(CancellationToken cancellationToken) =>
        _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);

    private async Task ExecutarSemFalharAsync(Task tarefa, string rotulo)
    {
        try
        {
            await tarefa;
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Falha ao enviar notificação ({Rotulo}).", rotulo);
        }
    }
}
