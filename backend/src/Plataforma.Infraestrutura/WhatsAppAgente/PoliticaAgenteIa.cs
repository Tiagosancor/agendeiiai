using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// O que o agente de IA pode fazer para um negócio. <c>Contratado</c> é o serviço premium (<c>Negocio.AgenteIaContratado</c>);
/// <c>IaLiberada</c> = Gemini + lista global (<c>Ai__NegociosHabilitados</c>) E contratado; <c>AgendamentoLiberado</c> = IaLiberada E
/// modo/lista de agendamento (<c>Ai__Agendamento__*</c>). As variáveis <c>Ai__*</c> são só o disjuntor global: restringem, nunca ligam
/// nada sem a contratação. <c>Gestao</c> = modo EFETIVO de gestão dos agendamentos do cliente (Fase 4): só sai de Desligado onde o
/// agendamento está liberado (mesmo piloto da Fase 3, sem lista nova) e <c>Ai__Gestao__Modo</c> não é Desligado.
/// </summary>
public sealed record SituacaoAgenteIa(bool Contratado, bool IaLiberada, bool AgendamentoLiberado, ModoGestaoAgente Gestao = ModoGestaoAgente.Desligado);

/// <summary>
/// Gate único do serviço premium. Escopo de DI = uma rodada: a situação é memorizada por negócio (uma consulta por rodada), mas
/// os pontos de escrita e de envio releem a contratação direto no banco (<see cref="ContratadoAgoraAsync"/>), para o desligamento
/// valer "na hora" mesmo no meio de uma rodada. Lê <c>negocios</c> pelo filtro normal — nenhum <c>IgnoreQueryFilters</c>.
/// </summary>
public sealed class PoliticaAgenteIa
{
    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesIa _opcoes;
    private readonly Dictionary<Guid, SituacaoAgenteIa> _memoria = [];

    public PoliticaAgenteIa(PlataformaDbContext dbContext, IOptions<OpcoesIa> opcoes)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
    }

    /// <summary>Situação (memorizada neste escopo). Quem começa uma rodada chama <see cref="Invalidar"/> antes.</summary>
    public async Task<SituacaoAgenteIa> ObterAsync(Guid negocioId, CancellationToken cancellationToken = default)
    {
        if (_memoria.TryGetValue(negocioId, out var memorizada))
            return memorizada;

        var situacao = Calcular(_opcoes, negocioId, await LerContratadoAsync(_dbContext, negocioId, cancellationToken));
        _memoria[negocioId] = situacao;
        return situacao;
    }

    /// <summary>Situação já lida neste escopo, ou nulo (uso síncrono do executor de ferramentas: sem leitura, fica fechado).</summary>
    public SituacaoAgenteIa? Memorizada(Guid negocioId) => _memoria.GetValueOrDefault(negocioId);

    /// <summary>Esquece o que foi lido (o escopo do processador atravessa até duas rodadas).</summary>
    public void Invalidar() => _memoria.Clear();

    /// <summary>Releitura direta do banco, sem memória: usada antes de gravar na agenda e antes de enviar texto do modelo.</summary>
    public Task<bool> ContratadoAgoraAsync(Guid negocioId, CancellationToken cancellationToken = default) =>
        LerContratadoAsync(_dbContext, negocioId, cancellationToken);

    /// <summary>Leitura sem rastreamento do campo único; negócio inexistente = não contratado.</summary>
    public static Task<bool> LerContratadoAsync(PlataformaDbContext dbContext, Guid negocioId, CancellationToken cancellationToken = default) =>
        dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.AgenteIaContratado)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>Regra pura (testável sem banco): global E contratado.</summary>
    public static SituacaoAgenteIa Calcular(OpcoesIa opcoes, Guid negocioId, bool contratado)
    {
        var iaLiberada = contratado && opcoes.Provider == ProvedorIa.Gemini && opcoes.NegocioHabilitado(negocioId);
        var agendamento = iaLiberada && opcoes.Agendamento.Habilitado(negocioId);
        return new SituacaoAgenteIa(contratado, iaLiberada, agendamento, agendamento ? opcoes.Gestao.ModoEfetivo : ModoGestaoAgente.Desligado);
    }
}
