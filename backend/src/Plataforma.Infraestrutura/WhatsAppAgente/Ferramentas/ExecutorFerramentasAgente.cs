using System.Text.Json;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>
/// Único caminho do modelo até os dados. Confere que o negócio do contexto é o da requisição antes de cada ferramenta
/// (defesa em profundidade: o filtro global do EF já isola) e converte qualquer falha em resultado de erro.
/// </summary>
public sealed class ExecutorFerramentasAgente : IExecutorFerramentasAgente
{
    private readonly Dictionary<string, IFerramentaAgente> _porNome;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly ILogger<ExecutorFerramentasAgente> _logger;
    private readonly OpcoesIa? _opcoes;
    private readonly PoliticaAgenteIa? _politica;

    public ExecutorFerramentasAgente(
        IEnumerable<IFerramentaAgente> ferramentas, IContextoNegocio contextoNegocio, ILogger<ExecutorFerramentasAgente> logger,
        IOptions<OpcoesIa>? opcoes = null, PoliticaAgenteIa? politica = null)
    {
        _politica = politica;
        _opcoes = opcoes?.Value;
        Ferramentas = ferramentas.ToList();
        _porNome = Ferramentas.ToDictionary(f => f.Nome, StringComparer.Ordinal);
        _contextoNegocio = contextoNegocio;
        _logger = logger;
    }

    public IReadOnlyList<IFerramentaAgente> Ferramentas { get; }

    /// <summary>
    /// Com a política (produção): vale a situação já lida pelo orquestrador nesta rodada (global E contratado); sem leitura, fica
    /// fechado. Sem a política (testes antigos) vale só a parte global; sem as opções, a escrita fica desligada.
    /// </summary>
    private bool EscritaLigada(Guid negocioId) =>
        _politica is not null
            ? _politica.Memorizada(negocioId)?.AgendamentoLiberado == true
            : _opcoes?.AgendamentoAtivoPara(negocioId) == true;

    /// <summary>
    /// Atendimento humano (Fase 4): a ferramenta <c>chamar_atendente</c> não depende do agendamento — só da flag e do serviço de IA
    /// contratado (mesma leitura memorizada da política; sem leitura, fechado).
    /// </summary>
    private bool HumanoLigado(Guid negocioId) =>
        _opcoes?.Humano.FerramentaAtiva == true
        && (_politica is not null
            ? _politica.Memorizada(negocioId)?.IaLiberada == true
            : _opcoes.Provider == ProvedorIa.Gemini && _opcoes.NegocioHabilitado(negocioId));

    /// <summary>Gestão dos agendamentos do cliente (Fase 4): precisa do agendamento liberado e de <c>Ai__Gestao__Modo</c> ligado; sem leitura, fechado.</summary>
    private bool GestaoLigada(Guid negocioId) =>
        _politica is not null
            ? _politica.Memorizada(negocioId)?.Gestao >= ModoGestaoAgente.Link
            : _opcoes?.AgendamentoAtivoPara(negocioId) == true && _opcoes.Gestao.ModoEfetivo >= ModoGestaoAgente.Link;

    /// <summary>Cancelar pelo agente (Fatia 7): além da gestão ligada, o modo precisa ser <c>Cancelar</c> ou acima; sem leitura, fechado.</summary>
    private bool CancelarLigado(Guid negocioId) =>
        _politica is not null
            ? _politica.Memorizada(negocioId)?.Gestao >= ModoGestaoAgente.Cancelar
            : _opcoes?.AgendamentoAtivoPara(negocioId) == true && _opcoes.Gestao.ModoEfetivo >= ModoGestaoAgente.Cancelar;

    private bool Disponivel(IFerramentaAgente ferramenta, Guid negocioId) => ferramenta switch
    {
        IFerramentaDeAtendimentoHumano => HumanoLigado(negocioId),
        IFerramentaDeCancelamentoAgente => CancelarLigado(negocioId),
        IFerramentaDeGestaoAgente => GestaoLigada(negocioId),
        IFerramentaDeEscritaAgente => EscritaLigada(negocioId),
        _ => true,
    };

    public IReadOnlyList<IFerramentaAgente> FerramentasPara(Guid negocioId) =>
        Ferramentas.Where(f => Disponivel(f, negocioId)).ToList();

    public async Task<JsonElement> ExecutarAsync(
        ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default) =>
        (await ExecutarComSinalAsync(contexto, nome, argumentos, cancellationToken)).Conteudo;

    public async Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(
        ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        if (_contextoNegocio.NegocioId != contexto.NegocioId)
        {
            // Nunca deveria acontecer. Aborta sem executar; loga só IDs.
            _logger.LogError("Agente: contexto de negócio divergente na ferramenta (conversa {ConversaId}, esperado {Esperado}, atual {Atual}).",
                contexto.ConversaId, contexto.NegocioId, _contextoNegocio.NegocioId);
            return new(RespostaFerramenta.Erro(ErrosFerramenta.ErroInterno));
        }

        // Ferramenta de escrita com o agendamento desligado (ou fora do piloto) não existe: mesmo erro de nome inventado.
        // Idem chamar_atendente com a flag desligada.
        if (!_porNome.TryGetValue(nome, out var ferramenta) || !Disponivel(ferramenta, contexto.NegocioId))
            return new(RespostaFerramenta.Erro(ErrosFerramenta.FerramentaDesconhecida));

        try
        {
            return ferramenta is IFerramentaDeEscritaAgente escrita
                ? await escrita.ExecutarComSinalAsync(contexto, argumentos, cancellationToken)
                : new(await ferramenta.ExecutarAsync(contexto, argumentos, cancellationToken));
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _logger.LogWarning("Agente: falha na ferramenta {Ferramenta} (conversa {ConversaId}): {Tipo}.",
                ferramenta.Nome, contexto.ConversaId, excecao.GetType().Name);
            return new(RespostaFerramenta.Erro(ErrosFerramenta.ErroInterno));
        }
    }
}
