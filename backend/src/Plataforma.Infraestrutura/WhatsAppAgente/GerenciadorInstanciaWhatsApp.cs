using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
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

    public GerenciadorInstanciaWhatsApp(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IClienteEvolution evolution,
        IOptions<OpcoesAgenteWhatsApp> opcoes, ILogger<GerenciadorInstanciaWhatsApp> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _evolution = evolution;
        _opcoes = opcoes.Value;
        _logger = logger;
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

        return new StatusWhatsApp(instancia.Status.ToString(), instancia.AgenteAtivo, instancia.Telefone);
    }

    public async Task<bool> DefinirAgenteAsync(bool ativo, CancellationToken cancellationToken = default)
    {
        var instancia = await _dbContext.InstanciasWhatsApp.FirstOrDefaultAsync(cancellationToken);
        if (instancia is null)
            return false;

        instancia.DefinirAgente(ativo);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Webhook por instância. Sem URL pública ou segredo, a instância nasce sem webhook (o que não adianta para o agente — o painel avisa).</summary>
    private string? UrlWebhook() =>
        _opcoes.WebhookConfigurado && Uri.TryCreate(_opcoes.UrlPublicaApi, UriKind.Absolute, out var baseUri)
            ? $"{baseUri.ToString().TrimEnd('/')}/webhooks/whatsapp/{Uri.EscapeDataString(_opcoes.WebhookSecret!)}"
            : null;
}
