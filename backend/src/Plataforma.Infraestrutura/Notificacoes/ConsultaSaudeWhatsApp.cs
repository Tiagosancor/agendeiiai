using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.Notificacoes;

/// <summary>Estado da conexão do WhatsApp para a administração da plataforma (seção 7). Consulta a instância na hora, sem cache.</summary>
public sealed class ConsultaSaudeWhatsApp : IConsultaSaudeWhatsApp
{
    private readonly OpcoesWhatsApp _opcoes;
    private readonly IServiceProvider _servicos;

    public ConsultaSaudeWhatsApp(IOptions<OpcoesWhatsApp> opcoes, IServiceProvider servicos)
    {
        _opcoes = opcoes.Value;
        _servicos = servicos;
    }

    public async Task<SaudeWhatsApp> ObterAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;

        return _opcoes.Provedor switch
        {
            ProvedorWhatsApp.EvolutionApi => await ConsultarEvolutionAsync(agora, cancellationToken),
            ProvedorWhatsApp.Oficial => new SaudeWhatsApp(
                "Oficial", EstadoConexaoWhatsApp.NaoSeAplica, "API oficial da Meta: não há sessão de aparelho para acompanhar.", agora),
            _ => new SaudeWhatsApp(
                "Fake", EstadoConexaoWhatsApp.NaoSeAplica, "Provedor de desenvolvimento: nenhuma mensagem sai de verdade.", agora),
        };
    }

    private async Task<SaudeWhatsApp> ConsultarEvolutionAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var evolution = _servicos.GetRequiredService<MensageriaWhatsAppEvolution>();
        var (estado, detalhe) = await evolution.ConsultarConexaoAsync(cancellationToken);
        return new SaudeWhatsApp("EvolutionApi", estado, detalhe, agora);
    }
}
