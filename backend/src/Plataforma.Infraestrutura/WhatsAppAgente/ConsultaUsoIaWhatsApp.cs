using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Uso de IA do negócio no mês UTC — o MESMO recorte da cota (<c>LimitadorUsoAgente</c>): mês civil UTC, soma de entrada + saída.
/// Só números, agregados de <c>usos_ia</c>, <c>rodadas_agente</c>, pendências confirmadas e da auditoria. Sempre no negócio do contexto.
/// </summary>
public sealed class ConsultaUsoIaWhatsApp : IConsultaUsoIaWhatsApp
{
    // Gravados pelas fatias de cancelar/remarcar (7 e 8). Contados pelo nome para a tela já funcionar quando existirem.
    private const string AcaoCancelamento = "CancelarPeloWhatsApp";
    private const string AcaoRemarcacao = "RemarcarPeloWhatsApp";

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesIa _opcoes;

    public ConsultaUsoIaWhatsApp(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes.Value;
    }

    public async Task<UsoIaMes> ObterAsync(int ano, int mes, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId ?? throw new InvalidOperationException("Negócio não resolvido.");
        var inicio = new DateTimeOffset(ano, mes, 1, 0, 0, 0, TimeSpan.Zero);
        var fim = inicio.AddMonths(1);

        var usos = await _dbContext.UsosIa.AsNoTracking()
            .Where(u => u.NegocioId == negocioId && u.CriadoEm >= inicio && u.CriadoEm < fim)
            .Select(u => new { u.CriadoEm, u.TokensEntrada, u.TokensSaida, u.Sucesso })
            .ToListAsync(cancellationToken);

        var porDia = usos
            .GroupBy(u => u.CriadoEm.UtcDateTime.Date)
            .OrderBy(g => g.Key)
            .Select(g => new UsoIaDia(
                g.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), g.Count(), g.Sum(u => (long)u.TokensEntrada + u.TokensSaida)))
            .ToList();

        var tokensEntrada = usos.Sum(u => (long)u.TokensEntrada);
        var tokensSaida = usos.Sum(u => (long)u.TokensSaida);
        var cota = _opcoes.CotaMensalTokensPorNegocio;
        var percentual = cota > 0 ? Math.Round((decimal)(tokensEntrada + tokensSaida) * 100m / cota, 1, MidpointRounding.AwayFromZero) : 0m;

        var hoje = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date, TimeSpan.Zero);
        var chamadasHoje = await _dbContext.UsosIa.AsNoTracking().CountAsync(u => u.NegocioId == negocioId && u.CriadoEm >= hoje, cancellationToken);

        var rodadas = _dbContext.RodadasAgente.AsNoTracking().Where(r => r.NegocioId == negocioId && r.IniciadaEm >= inicio && r.IniciadaEm < fim);
        var conversasAtendidas = await rodadas.Where(r => r.Desfecho != DesfechoRodadaAgente.Silencio).Select(r => r.ConversaId).Distinct().CountAsync(cancellationToken);
        var fallbacks = await rodadas.CountAsync(r => r.Desfecho == DesfechoRodadaAgente.Fallback, cancellationToken);

        var confirmados = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking().CountAsync(
            p => p.NegocioId == negocioId && p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= inicio && p.ConcluidaEm < fim,
            cancellationToken);

        var auditoria = _dbContext.LogsAuditoriaNegocio.AsNoTracking().Where(l => l.NegocioId == negocioId && l.CriadoEm >= inicio && l.CriadoEm < fim);
        var cancelamentos = await auditoria.CountAsync(l => l.Acao == AcaoCancelamento, cancellationToken);
        var remarcacoes = await auditoria.CountAsync(l => l.Acao == AcaoRemarcacao, cancellationToken);
        var transferencias = await auditoria.CountAsync(l => l.Acao == AcoesAuditoria.AtenderConversaPeloAgente, cancellationToken);

        return new UsoIaMes(
            $"{ano:D4}-{mes:D2}", usos.Count, usos.Count(u => !u.Sucesso), tokensEntrada, tokensSaida, cota, percentual,
            chamadasHoje, _opcoes.LimiteChamadasPorNegocioPorDia, conversasAtendidas, fallbacks, confirmados,
            cancelamentos, remarcacoes, transferencias, porDia);
    }
}
