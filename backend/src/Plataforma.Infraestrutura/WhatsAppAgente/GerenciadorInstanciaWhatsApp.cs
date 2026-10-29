using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Instância de WhatsApp do negócio do token (o filtro multi-tenant do <see cref="PlataformaDbContext"/> restringe tudo ao
/// negócio atual). Uma instância por negócio (índice único); o nome na Evolution é <c>neg-{id do negócio}</c>.
/// </summary>
public sealed class GerenciadorInstanciaWhatsApp : IGerenciadorInstanciaWhatsApp
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IClienteEvolution _evolution;
    private readonly OpcoesAgenteWhatsApp _opcoes;
    private readonly ILogger<GerenciadorInstanciaWhatsApp> _logger;
    private readonly PoliticaAgenteIa _politica;
    private readonly OpcoesIa _opcoesIa;
    private readonly IRegistroAuditoria _auditoria;

    public GerenciadorInstanciaWhatsApp(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IClienteEvolution evolution,
        IOptions<OpcoesAgenteWhatsApp> opcoes, ILogger<GerenciadorInstanciaWhatsApp> logger,
        PoliticaAgenteIa politica, IOptions<OpcoesIa> opcoesIa, IRegistroAuditoria auditoria)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _evolution = evolution;
        _opcoes = opcoes.Value;
        _logger = logger;
        _politica = politica;
        _opcoesIa = opcoesIa.Value;
        _auditoria = auditoria;
    }

    public async Task<ConexaoWhatsApp> ConectarAsync(CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId ?? throw new InvalidOperationException("Negócio não resolvido.");
        var instancia = await _dbContext.InstanciasWhatsApp.FirstOrDefaultAsync(cancellationToken);

        if (instancia is not null)
        {
            var estado = await _evolution.ConsultarStatusAsync(instancia.NomeInstancia, cancellationToken);

            if (estado == EstadoInstanciaEvolution.Aberta)
            {
                instancia.AtualizarStatus(StatusInstanciaWhatsApp.Conectada);
                await _dbContext.SaveChangesAsync(cancellationToken);
                return new ConexaoWhatsApp(nameof(StatusInstanciaWhatsApp.Conectada), null);
            }

            // Sumiu da Evolution (ex.: instância recriada do zero): recria com o mesmo nome.
            var qr = estado == EstadoInstanciaEvolution.NaoExiste
                ? await _evolution.CriarInstanciaAsync(instancia.NomeInstancia, UrlWebhook(), cancellationToken)
                : await _evolution.ObterQrCodeAsync(instancia.NomeInstancia, cancellationToken);

            instancia.AtualizarStatus(StatusInstanciaWhatsApp.AguardandoQrCode);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ConexaoWhatsApp(nameof(StatusInstanciaWhatsApp.AguardandoQrCode), qr);
        }

        var nome = $"neg-{negocioId:N}";
        var qrNovo = await _evolution.CriarInstanciaAsync(nome, UrlWebhook(), cancellationToken);

        instancia = new InstanciaWhatsApp(negocioId, nome);
        instancia.AtualizarStatus(StatusInstanciaWhatsApp.AguardandoQrCode);
        _dbContext.InstanciasWhatsApp.Add(instancia);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Instância de WhatsApp {InstanciaId} criada.", instancia.Id);
        return new ConexaoWhatsApp(nameof(StatusInstanciaWhatsApp.AguardandoQrCode), qrNovo);
    }

    public async Task<StatusWhatsApp?> ObterStatusAsync(CancellationToken cancellationToken = default)
    {
        var instancia = await _dbContext.InstanciasWhatsApp.FirstOrDefaultAsync(cancellationToken);
        if (instancia is null)
            return null;

        try
        {
            var estado = await _evolution.ConsultarStatusAsync(instancia.NomeInstancia, cancellationToken);
            var novo = estado switch
            {
                EstadoInstanciaEvolution.Aberta => StatusInstanciaWhatsApp.Conectada,
                EstadoInstanciaEvolution.Conectando => StatusInstanciaWhatsApp.AguardandoQrCode,
                _ => StatusInstanciaWhatsApp.Desconectada,
            };

            if (novo != instancia.Status)
            {
                instancia.AtualizarStatus(novo);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        catch (FalhaEvolutionException)
        {
            // Evolution fora do ar: devolve o último estado gravado em vez de derrubar a tela.
        }

        // Mesma regra que decide em tempo de execução (global E contratado): a tela só mostra o que o agente de fato faz.
        _politica.Invalidar();
        var situacao = await _politica.ObterAsync(instancia.NegocioId, cancellationToken);
        return new StatusWhatsApp(
            instancia.Status.ToString(), instancia.AgenteAtivo, instancia.Telefone, situacao.Contratado, instancia.MensagemTransferencia,
            situacao.IaLiberada, situacao.AgendamentoLiberado,
            // Gestão (cancelar/remarcar) só chega nas Fatias 6–8; até lá o efetivo é sempre "Desligado".
            GestaoModo: "Desligado",
            AtendimentoHumanoAtivo: situacao.IaLiberada && _opcoesIa.Humano.FerramentaAtiva);
    }

    public async Task<ResultadoMensagemTransferencia> DefinirMensagemTransferenciaAsync(string? mensagem, CancellationToken cancellationToken = default)
    {
        var instancia = await _dbContext.InstanciasWhatsApp.FirstOrDefaultAsync(cancellationToken);
        if (instancia is null)
            return ResultadoMensagemTransferencia.SemInstancia;

        var antes = instancia.MensagemTransferencia?.Length ?? 0;
        try
        {
            // O navegador pode mandar quebra de linha do Windows; o domínio só aceita "\n".
            instancia.DefinirMensagemTransferencia(mensagem?.Replace("\r\n", "\n").Replace('\r', '\n'));
        }
        catch (ArgumentException)
        {
            return ResultadoMensagemTransferencia.Invalida;
        }

        // Auditoria sem o conteúdo: só o tamanho do texto antes e depois.
        var depois = instancia.MensagemTransferencia?.Length ?? 0;
        _auditoria.Registrar(
            AcoesAuditoria.AlterarMensagemTransferenciaWhatsApp, nameof(InstanciaWhatsApp), instancia.Id, $"{antes} → {depois} caracteres");
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoMensagemTransferencia.Ok;
    }

    public async Task<ResultadoDefinirAgente> DefinirAgenteAsync(bool ativo, CancellationToken cancellationToken = default)
    {
        var instancia = await _dbContext.InstanciasWhatsApp.FirstOrDefaultAsync(cancellationToken);
        if (instancia is null)
            return ResultadoDefinirAgente.SemInstancia;

        // Sem o serviço contratado, "agente ativo" na tela seria mentira (nada responderia): ligar é recusado; desligar sempre vale.
        if (ativo && !await PoliticaAgenteIa.LerContratadoAsync(_dbContext, instancia.NegocioId, cancellationToken))
            return ResultadoDefinirAgente.NaoContratado;

        instancia.DefinirAgente(ativo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ResultadoDefinirAgente.Ok;
    }

    public async Task<ServicoAgenteIaPainel> ObterServicoIaAsync(CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId ?? throw new InvalidOperationException("Negócio não resolvido.");
        var negocio = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId)
            .Select(n => new { n.AgenteIaContratado, n.AgenteIaOrigem }).FirstOrDefaultAsync(cancellationToken);
        return new ServicoAgenteIaPainel(negocio?.AgenteIaContratado == true, negocio?.AgenteIaContratado == true ? negocio.AgenteIaOrigem?.ToString() : null);
    }

    /// <summary>Webhook por instância. Sem URL pública ou segredo, a instância nasce sem webhook (o que não adianta para o agente — o painel avisa).</summary>
    private string? UrlWebhook() =>
        _opcoes.WebhookConfigurado && Uri.TryCreate(_opcoes.UrlPublicaApi, UriKind.Absolute, out var baseUri)
            ? $"{baseUri.ToString().TrimEnd('/')}/webhooks/whatsapp/{Uri.EscapeDataString(_opcoes.WebhookSecret!)}"
            : null;
}
