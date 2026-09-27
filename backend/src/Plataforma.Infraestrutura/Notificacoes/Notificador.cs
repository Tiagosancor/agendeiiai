using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Comum;
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

    public async Task<ResultadoEnvioCanais> EnviarCodigoVerificacaoAsync(
        TelefoneE164 telefone, string? email, string codigo, int validadeMinutos, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);
        var mensagem = $"Seu código de confirmação em {negocio.NomeExibido}: {codigo}. Válido por {validadeMinutos} minutos.";

        // Teto diário de WhatsApp por negócio (seção 8.1.5) — cada código (e cada reenvio dele)
        // tenta 1 envio por WhatsApp, então a soma das últimas 24h aproxima bem quantos WhatsApp
        // já foram tentados hoje; ao estourar, só e-mail.
        var whatsAppHoje = await _dbContext.CodigosVerificacao
            .Where(c => c.NegocioId == negocio.Id && c.CriadoEm >= DateTimeOffset.UtcNow.AddDays(-1))
            .SumAsync(c => 1 + c.TentativasReenvio, cancellationToken);

        // Os dois canais saem em paralelo e cada um contém a própria falha (seção 8.1): o
        // e-mail não espera o WhatsApp (nem o retry dele), e um não cancela o outro.
        Task<ResultadoEnvioWhatsApp>? envioWhatsApp = null;
        if (whatsAppHoje <= _opcoesVerificacao.MaximoWhatsAppPorNegocioPorDia)
            envioWhatsApp = EnviarWhatsAppSemFalharAsync(telefone, mensagem, "WhatsApp/código", cancellationToken);
        else
            _logger.LogInformation("Teto diário de WhatsApp do negócio {NegocioId} atingido — código enviado só por e-mail.", negocio.Id);

        Task<bool>? envioEmail = null;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var corpo = Envelope(negocio.NomeExibido,
                $"<p>Seu código de confirmação em <strong>{negocio.NomeExibido}</strong>:</p><h2>{codigo}</h2><p>Válido por {validadeMinutos} minutos.</p>");
            envioEmail = ExecutarSemFalharAsync(() =>
                _email.EnviarAsync(email, $"Seu código de confirmação — {negocio.NomeExibido}", corpo, cancellationToken), "E-mail/código");
        }

        var whatsApp = envioWhatsApp is null ? null : await envioWhatsApp;
        var emailEnviado = envioEmail is null ? (bool?)null : await envioEmail;

        return new ResultadoEnvioCanais(whatsApp?.Status, whatsApp?.IdMensagem, StatusDoEmail(emailEnviado));
    }

    public async Task EnviarConfirmacaoAgendamentoAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Seu agendamento em <strong>{negocio.NomeExibido}</strong> está confirmado.</p>
            <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><strong>Total:</strong> {FormatacaoBrasil.Reais(dados.Total)}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """);

        var tarefas = new List<Task>();

        if (!string.IsNullOrWhiteSpace(dados.EmailCliente))
        {
            tarefas.Add(ExecutarSemFalharAsync(() =>
                _email.EnviarAsync(dados.EmailCliente, $"Agendamento confirmado — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/confirmação"));
        }

        if (negocio.WhatsAppAtivoParaConfirmacoes)
        {
            var mensagem = $"Agendamento confirmado em {negocio.NomeExibido} para {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}. Total {FormatacaoBrasil.Reais(dados.Total)}.";
            tarefas.Add(EnviarWhatsAppSemFalharAsync(dados.TelefoneCliente, mensagem, "WhatsApp/confirmação", cancellationToken));
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

        await ExecutarSemFalharAsync(() =>
            _email.EnviarAsync(negocio.EmailContato, $"Nova mensagem pelo site — {negocio.NomeExibido}", corpo, cancellationToken),
            "E-mail/fale-conosco");
    }

    public async Task<ResultadoEnvioCanais> EnviarNotificacaoProfissionalAsync(DadosNotificacaoProfissional dados, CancellationToken cancellationToken = default)
    {
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
            <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            {(string.IsNullOrWhiteSpace(dados.Observacoes) ? "" : $"<p><strong>Observações:</strong> {dados.Observacoes}</p>")}
            """);

        // E-mail sempre (é a rede de segurança se a instância de WhatsApp cair); WhatsApp só com
        // o aviso ligado no negócio e telefone válido no cadastro do profissional (seção 9).
        Task<bool>? envioEmail = string.IsNullOrWhiteSpace(dados.EmailProfissional)
            ? null
            : ExecutarSemFalharAsync(() =>
                _email.EnviarAsync(dados.EmailProfissional!, $"{assunto} — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/profissional");

        Task<ResultadoEnvioWhatsApp>? envioWhatsApp = null;
        if (negocio.WhatsAppAvisoProfissional && TelefoneE164.TentarCriar(dados.TelefoneProfissional ?? "", out var telefone))
        {
            var mensagem = $"{titulo} em {negocio.NomeExibido}: {dados.NomeCliente}, {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)} — {string.Join(", ", dados.Servicos)}.";
            envioWhatsApp = EnviarWhatsAppSemFalharAsync(telefone!, mensagem, "WhatsApp/profissional", cancellationToken);
        }

        var whatsApp = envioWhatsApp is null ? null : await envioWhatsApp;
        var emailEnviado = envioEmail is null ? (bool?)null : await envioEmail;

        return new ResultadoEnvioCanais(whatsApp?.Status, whatsApp?.IdMensagem, StatusDoEmail(emailEnviado));
    }

    public async Task EnviarLembreteAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p>Olá, {dados.NomeCliente}! Lembrete do seu agendamento em <strong>{negocio.NomeExibido}</strong>.</p>
            <p><strong>Quando:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {string.Join(", ", dados.Servicos)}</p>
            <p><a href="{dados.LinkRemarcar}">Remarcar</a> · <a href="{dados.LinkCancelar}">Cancelar</a></p>
            """);

        var tarefas = new List<Task>();

        if (!string.IsNullOrWhiteSpace(dados.EmailCliente))
        {
            tarefas.Add(ExecutarSemFalharAsync(() =>
                _email.EnviarAsync(dados.EmailCliente, $"Lembrete do seu agendamento — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/lembrete"));
        }

        if (negocio.WhatsAppAtivoParaConfirmacoes)
        {
            var mensagem = $"Lembrete: você tem um agendamento em {negocio.NomeExibido} em {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}.";
            tarefas.Add(EnviarWhatsAppSemFalharAsync(dados.TelefoneCliente, mensagem, "WhatsApp/lembrete", cancellationToken));
        }

        await Task.WhenAll(tarefas);
    }

    public async Task EnviarCancelamentoClienteAsync(DadosNotificacaoAgendamento dados, CancellationToken cancellationToken = default)
    {
        var negocio = await ObterNegocioAsync(cancellationToken);
        var nomeCliente = System.Net.WebUtility.HtmlEncode(dados.NomeCliente);
        var servicos = System.Net.WebUtility.HtmlEncode(string.Join(", ", dados.Servicos));

        var corpo = Envelope(negocio.NomeExibido, $"""
            <p>Olá, {nomeCliente}. Infelizmente, <strong>{negocio.NomeExibido}</strong> precisou cancelar o seu agendamento.</p>
            <p><strong>Quando seria:</strong> {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}</p>
            <p><strong>Serviços:</strong> {servicos}</p>
            <p>Se quiser, é só agendar um novo horário pela página do negócio.</p>
            """);

        var tarefas = new List<Task>();

        if (!string.IsNullOrWhiteSpace(dados.EmailCliente))
        {
            tarefas.Add(ExecutarSemFalharAsync(() =>
                _email.EnviarAsync(dados.EmailCliente, $"Agendamento cancelado — {negocio.NomeExibido}", corpo, cancellationToken),
                "E-mail/cancelamento"));
        }

        if (negocio.WhatsAppAtivoParaConfirmacoes)
        {
            var mensagem = $"{negocio.NomeExibido} precisou cancelar o seu agendamento de {FormatacaoBrasil.DataHora(dados.Inicio, negocio.Fuso)}. Se quiser, agende um novo horário pela página do negócio.";
            tarefas.Add(EnviarWhatsAppSemFalharAsync(dados.TelefoneCliente, mensagem, "WhatsApp/cancelamento", cancellationToken));
        }

        await Task.WhenAll(tarefas);
    }

    public async Task EnviarAvisoAssinaturaAsync(DadosAvisoAssinatura dados, CancellationToken cancellationToken = default)
    {
        var nomeNegocio = System.Net.WebUtility.HtmlEncode(dados.NomeNegocio);
        var quando = dados.DiasRestantes == 1 ? "amanhã" : $"em {dados.DiasRestantes} dias";
        var (assunto, abertura) = dados.EmTeste
            ? ($"Seu teste grátis termina {quando}", $"O teste grátis de <strong>{nomeNegocio}</strong> termina {quando} ({FormatacaoBrasil.Data(dados.Prazo, dados.Fuso)}).")
            : ($"Sua assinatura vence {quando}", $"A assinatura de <strong>{nomeNegocio}</strong> vence {quando} ({FormatacaoBrasil.Data(dados.Prazo, dados.Fuso)}).");

        var corpo = Envelope(null, $"""
            <p>{abertura}</p>
            <p>Plano {dados.NomePlano}: {FormatacaoBrasil.Reais(dados.ValorDoPeriodo)}. Para continuar recebendo agendamentos sem interrupção, é só assinar.</p>
            <p><a href="{dados.LinkAssinatura}">Assinar agora</a></p>
            """);

        await Task.WhenAll(dados.EmailsAdministradores.Select(email =>
            ExecutarSemFalharAsync(() => _email.EnviarAsync(email, $"{assunto} — {_opcoesMarca.NomeProduto}", corpo, cancellationToken), "E-mail/aviso-assinatura")));
    }

    public Task EnviarCodigoCadastroAsync(string email, string codigo, CancellationToken cancellationToken = default)
    {
        var corpo = Envelope(null, $"<p>Seu código para criar a conta:</p><h2>{codigo}</h2><p>Válido por 5 minutos. Se não foi você, ignore este e-mail.</p>");
        return ExecutarSemFalharAsync(() =>
            _email.EnviarAsync(email, $"Seu código de cadastro — {_opcoesMarca.NomeProduto}", corpo, cancellationToken), "E-mail/código-cadastro");
    }

    public Task EnviarAvisoContaExistenteAsync(string email, string linkLogin, CancellationToken cancellationToken = default)
    {
        var corpo = Envelope(null, $"""
            <p>Alguém tentou criar uma conta nova com este e-mail, mas ele já tem uma conta.</p>
            <p><a href="{linkLogin}">Entrar no painel</a></p>
            <p>Se não lembra a senha, fale com o suporte do {_opcoesMarca.NomeProduto}. Se não foi você, ignore esta mensagem.</p>
            """);
        return ExecutarSemFalharAsync(() =>
            _email.EnviarAsync(email, $"Você já tem uma conta — {_opcoesMarca.NomeProduto}", corpo, cancellationToken), "E-mail/conta-existente");
    }

    public Task EnviarBoasVindasAsync(DadosBoasVindas dados, CancellationToken cancellationToken = default)
    {
        var nomeUsuario = System.Net.WebUtility.HtmlEncode(dados.NomeUsuario);
        var nomeNegocio = System.Net.WebUtility.HtmlEncode(dados.NomeNegocio);
        var corpo = Envelope(null, $"""
            <p>Olá, {nomeUsuario}! A conta de <strong>{nomeNegocio}</strong> está pronta.</p>
            <p>Seu teste grátis vai até <strong>{FormatacaoBrasil.Data(dados.FimTeste, dados.Fuso)}</strong>, com todos os recursos do plano.</p>
            <p><strong>Painel:</strong> <a href="{dados.LinkPainel}">{dados.LinkPainel}</a></p>
            <p><strong>Seu link de agendamento:</strong> <a href="{dados.LinkPublico}">{dados.LinkPublico}</a></p>
            <p>Comece cadastrando serviços e equipe, e configure os horários de trabalho.</p>
            """);
        return ExecutarSemFalharAsync(() =>
            _email.EnviarAsync(dados.Email, $"Bem-vindo ao {_opcoesMarca.NomeProduto}", corpo, cancellationToken), "E-mail/boas-vindas");
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

    /// <summary>Recebe a função, não a Task já criada: um provedor que lance antes do primeiro await também fica contido aqui.</summary>
    private async Task<bool> ExecutarSemFalharAsync(Func<Task> envio, string rotulo)
    {
        try
        {
            await envio();
            return true;
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Falha ao enviar notificação ({Rotulo}).", rotulo);
            return false;
        }
    }

    /// <summary>O provedor já não deveria lançar (contrato de <see cref="IMensageriaWhatsApp"/>); isto é só a segunda barreira.</summary>
    private async Task<ResultadoEnvioWhatsApp> EnviarWhatsAppSemFalharAsync(
        TelefoneE164 telefone, string mensagem, string rotulo, CancellationToken cancellationToken)
    {
        try
        {
            return await _whatsApp.EnviarAsync(telefone, mensagem, cancellationToken);
        }
        catch (Exception excecao)
        {
            _logger.LogError(excecao, "Falha ao enviar notificação ({Rotulo}).", rotulo);
            return ResultadoEnvioWhatsApp.Indisponivel();
        }
    }

    private static StatusCanal? StatusDoEmail(bool? enviado) => enviado switch
    {
        null => null,
        true => StatusCanal.Enviado,
        false => StatusCanal.Falhou,
    };
}
