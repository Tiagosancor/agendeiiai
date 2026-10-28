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

    public IReadOnlyList<IFerramentaAgente> FerramentasPara(Guid negocioId) =>
        EscritaLigada(negocioId) ? Ferramentas : Ferramentas.Where(f => f is not IFerramentaDeEscritaAgente).ToList();

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
        if (!_porNome.TryGetValue(nome, out var ferramenta)
            || (ferramenta is IFerramentaDeEscritaAgente && !EscritaLigada(contexto.NegocioId)))
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
