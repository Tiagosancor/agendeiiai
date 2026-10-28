using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>
/// Passa a conversa para uma pessoa (G13). O único parâmetro é <c>motivo</c>, um enum fechado: texto livre do modelo nunca chega ao
/// e-mail, ao log nem ao banco. O pior efeito de uma injeção é silenciar o agente NAQUELA conversa, que é do próprio cliente. A
/// conversa e o negócio vêm do contexto do servidor; o texto ao cliente e o e-mail ao dono são do servidor, depois da rodada.
/// </summary>
public sealed class FerramentaChamarAtendente : IFerramentaDeAtendimentoHumano
{
    public const string Status = "atendente_chamado";
    private const string AtendenteIndisponivel = ErrosFerramenta.AtendenteIndisponivel;

    private static readonly Dictionary<string, MotivoChamadaAtendente> Motivos = new(StringComparer.Ordinal)
    {
        ["pedido_do_cliente"] = MotivoChamadaAtendente.PedidoDoCliente,
        ["reclamacao"] = MotivoChamadaAtendente.Reclamacao,
        ["fora_do_escopo"] = MotivoChamadaAtendente.ForaDoEscopo,
        ["outro"] = MotivoChamadaAtendente.Outro,
    };

    private readonly IGerenciadorConversasWhatsApp _conversas;
    private readonly PoliticaAgenteIa _politica;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IOptions<OpcoesIa> _opcoes;
    private readonly ILogger<FerramentaChamarAtendente> _logger;

    public FerramentaChamarAtendente(
        IGerenciadorConversasWhatsApp conversas, PoliticaAgenteIa politica, IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes,
        ILogger<FerramentaChamarAtendente> logger)
    {
        _conversas = conversas;
        _politica = politica;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes;
        _logger = logger;
    }

    public string Nome => "chamar_atendente";

    public string Descricao =>
        "Chama uma pessoa da equipe para continuar esta conversa. Use quando o cliente pedir para falar com uma pessoa, fizer uma " +
        "reclamação ou o assunto fugir do que você consegue resolver. Depois de chamar, o sistema avisa o cliente: não escreva mais nada. " +
        "Chame no máximo uma vez por resposta.";

    public System.Text.Json.JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "motivo":{"type":"string","enum":["pedido_do_cliente","reclamacao","fora_do_escopo","outro"],
                    "description":"Por que está chamando: o cliente pediu uma pessoa, reclamação, assunto fora do escopo ou outro"}},
         "required":["motivo"],"additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
        (await ExecutarComSinalAsync(contexto, argumentos, cancellationToken)).Conteudo;

    public async Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(
        ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        static ResultadoFerramentaAgente Erro(string codigo) => new(RespostaFerramenta.Erro(codigo));

        // 1. Argumentos: só `motivo`, um dos quatro valores. Campo extra (telefone, negocio_id, texto livre...) invalida o pedido.
        var leitor = new LeitorArgumentos(argumentos, "motivo");
        var motivoTexto = leitor.Texto("motivo");
        if (!leitor.Valido || motivoTexto is null || !Motivos.TryGetValue(motivoTexto, out var motivo))
            return Erro(ErrosFerramenta.ArgumentosInvalidos);

        // 2. Flag e serviço (segunda barreira: o executor já não oferece a ferramenta fora disso).
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!_opcoes.Value.Humano.FerramentaAtiva || !(await _politica.ObterAsync(negocioId, cancellationToken)).IaLiberada)
            return Erro(AtendenteIndisponivel);

        // 3. A conversa é a do contexto (servidor). Já em Humano (o dono assumiu antes) = nada a fazer: sem texto ao cliente.
        var resultado = await _conversas.MarcarHumanoPeloAgenteAsync(contexto.ConversaId, motivo, cancellationToken);
        if (!resultado.Marcada)
            return Erro(AtendenteIndisponivel);

        _logger.LogInformation("Agente: ferramenta chamar_atendente executada (conversa {ConversaId}, negócio {NegocioId}).", contexto.ConversaId, negocioId);
        return new ResultadoFerramentaAgente(
            RespostaFerramenta.De(new { status = Status }),
            Acao: new AcaoServidor(TipoAcaoServidor.Transferencia, contexto.ConversaId, motivoTexto, resultado.AvisarAtendente));
    }
}
