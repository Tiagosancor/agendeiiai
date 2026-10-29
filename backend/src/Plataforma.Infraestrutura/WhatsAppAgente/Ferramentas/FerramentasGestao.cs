using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

// Ferramentas de GESTÃO do modo Link (Fase 4, Fatia 6). O modelo nunca informa negócio, telefone, cliente nem conversa: o número é o da
// conversa (servidor) e a lista de agendamentos vem de ConsultaAgendamentosDoClienteAgente (G1/G2). Os DTOs têm lista FECHADA de campos
// (G15: há teste de snapshot das chaves): sem preço, observação, nome, e-mail ou telefone do cliente.

public sealed record AgendamentoDoNumeroDaFerramenta(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("servicos")] IReadOnlyList<string> Servicos,
    [property: JsonPropertyName("profissional")] string Profissional,
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("dia_semana")] string DiaSemana,
    [property: JsonPropertyName("hora")] string Hora,
    [property: JsonPropertyName("pode_cancelar")] bool PodeCancelar,
    [property: JsonPropertyName("pode_remarcar")] bool PodeRemarcar,
    [property: JsonPropertyName("motivo")] string? Motivo);

public sealed record MeusAgendamentosDaFerramenta(
    [property: JsonPropertyName("agendamentos")] IReadOnlyList<AgendamentoDoNumeroDaFerramenta> Agendamentos,
    [property: JsonPropertyName("truncado")] bool Truncado);

/// <summary>Item do <c>escolha_necessaria</c>: o suficiente para o modelo perguntar qual (nada de serviço, preço ou profissional).</summary>
public sealed record OpcaoDeAgendamentoDaFerramenta(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("hora")] string Hora);

/// <summary>Barreiras e formatação comuns às duas ferramentas de gestão.</summary>
public static class ApoioGestaoAgente
{
    public const int MaximoAgendamentos = 5;

    /// <summary>Modo ligado para o negócio (segunda barreira: o executor já não oferece a ferramenta fora dele) e serviço contratado AGORA.</summary>
    public static async Task<bool> GestaoDisponivelAsync(
        PoliticaAgenteIa politica, Guid negocioId, CancellationToken cancellationToken, ModoGestaoAgente minimo = ModoGestaoAgente.Link) =>
        (await politica.ObterAsync(negocioId, cancellationToken)).Gestao >= minimo
        && await politica.ContratadoAgoraAsync(negocioId, cancellationToken);

    public static (string Data, string Hora, string DiaSemana) DataEHora(DateTimeOffset inicio, TimeZoneInfo fuso)
    {
        var (dia, hora) = ConversorFusoHorario.ParaLocal(inicio, fuso);
        return (dia.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), hora.ToString("HH':'mm", CultureInfo.InvariantCulture),
            ConstrutorPromptAgente.NomeDoDia(dia.DayOfWeek));
    }

    /// <summary>"Maria Silva" vira "Maria": só o primeiro nome do profissional vai ao modelo.</summary>
    public static string PrimeiroNome(string? nome)
    {
        var limpo = RespostaFerramenta.Limpar(nome);
        var indice = limpo.IndexOf(' ');
        return indice > 0 ? limpo[..indice] : limpo;
    }
}

/// <summary>Leitura: os próximos agendamentos deste número (até 5), com o que o cliente pode fazer com cada um (as mesmas regras do link).</summary>
public sealed class FerramentaListarMeusAgendamentos : IFerramentaDeGestaoAgente
{
    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly PoliticaAgenteIa _politica;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly ILogger<FerramentaListarMeusAgendamentos> _logger;

    public FerramentaListarMeusAgendamentos(
        ConsultaAgendamentosDoClienteAgente consulta, IGestaoPublicaAgendamento gestao, PoliticaAgenteIa politica,
        IContextoNegocio contextoNegocio, ILogger<FerramentaListarMeusAgendamentos> logger)
    {
        _consulta = consulta;
        _gestao = gestao;
        _politica = politica;
        _contextoNegocio = contextoNegocio;
        _logger = logger;
    }

    public string Nome => "listar_meus_agendamentos";

    public string Descricao =>
        "Lista os próximos agendamentos do cliente desta conversa (só os deste número, que ele marcou pelo link ou pelo WhatsApp): id, serviços, " +
        "primeiro nome do profissional, data, dia da semana, hora e se pode cancelar ou remarcar (com o motivo, se não puder). Não recebe parâmetros.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema("""{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        // Qualquer campo (telefone, cliente_id, negocio_id...) invalida o pedido.
        if (!new LeitorArgumentos(argumentos).Valido)
            return RespostaFerramenta.Erro(ErrosFerramenta.ArgumentosInvalidos);

        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ApoioGestaoAgente.GestaoDisponivelAsync(_politica, negocioId, cancellationToken))
            return RespostaFerramenta.Erro(ErrosFerramenta.GestaoIndisponivel);

        var elegiveis = await _consulta.ListarAsync(contexto.ConversaId, ApoioGestaoAgente.MaximoAgendamentos + 1, null, cancellationToken);
        var itens = new List<AgendamentoDoNumeroDaFerramenta>();
        foreach (var elegivel in elegiveis.Take(ApoioGestaoAgente.MaximoAgendamentos))
        {
            // O mesmo cálculo do GET /publico/meus-agendamentos/{token}: status, antecedência mínima e quinzena fechada.
            var detalhe = await _gestao.ObterAsync(elegivel.Id, cancellationToken);
            if (detalhe is null)
                continue;

            var fuso = TimeZoneInfo.FindSystemTimeZoneById(detalhe.Fuso);
            var (data, hora, diaSemana) = ApoioGestaoAgente.DataEHora(detalhe.Inicio, fuso);
            var bloqueio = !detalhe.Acoes.Cancelar.Permitido ? detalhe.Acoes.Cancelar : !detalhe.Acoes.Remarcar.Permitido ? detalhe.Acoes.Remarcar : null;
            itens.Add(new AgendamentoDoNumeroDaFerramenta(
                detalhe.Id,
                detalhe.Servicos.Select(s => RespostaFerramenta.Limpar(s)).ToList(),
                ApoioGestaoAgente.PrimeiroNome(detalhe.Profissional.Nome),
                data, diaSemana, hora,
                detalhe.Acoes.Cancelar.Permitido, detalhe.Acoes.Remarcar.Permitido, bloqueio?.CodigoMotivo));
        }

        _logger.LogInformation("Agente: listar_meus_agendamentos (conversa {ConversaId}, negócio {NegocioId}, {Quantidade} item(ns)).",
            contexto.ConversaId, negocioId, itens.Count);
        return RespostaFerramenta.De(new MeusAgendamentosDaFerramenta(itens, elegiveis.Count > ApoioGestaoAgente.MaximoAgendamentos));
    }
}

/// <summary>
/// Pede ao SERVIDOR que envie o link de gestão (cancelar/remarcar na página que já existe) de um agendamento do número. Encerra a rodada: o
/// texto do modelo é descartado e o token nunca passa pelo modelo, pelo banco nem pelos logs.
/// </summary>
public sealed class FerramentaEnviarLinkGestao : IFerramentaDeGestaoAgente, IFerramentaDeEscritaAgente
{
    public const string Status = "link_sera_enviado";

    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly PlataformaDbContext _dbContext;
    private readonly PoliticaAgenteIa _politica;
    private readonly LimitadorUsoAgente _limitador;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly ILogger<FerramentaEnviarLinkGestao> _logger;

    public FerramentaEnviarLinkGestao(
        ConsultaAgendamentosDoClienteAgente consulta, PlataformaDbContext dbContext, PoliticaAgenteIa politica, LimitadorUsoAgente limitador,
        IContextoNegocio contextoNegocio, ILogger<FerramentaEnviarLinkGestao> logger)
    {
        _consulta = consulta;
        _dbContext = dbContext;
        _politica = politica;
        _limitador = limitador;
        _contextoNegocio = contextoNegocio;
        _logger = logger;
    }

    public string Nome => "enviar_link_gestao";

    public string Descricao =>
        "Faz o sistema enviar ao cliente o link para ver, cancelar ou remarcar um agendamento dele. Use quando o cliente quiser cancelar ou remarcar. " +
        "agendamento_id é opcional se o cliente tiver só um agendamento; com mais de um, informe qual (id de listar_meus_agendamentos). " +
        "Depois de chamar, o sistema envia o link: não escreva mais nada. Chame no máximo uma vez por resposta.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "agendamento_id":{"type":"string","description":"UUID do agendamento, obtido em listar_meus_agendamentos (opcional se houver só um)"}},
         "additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
        (await ExecutarComSinalAsync(contexto, argumentos, cancellationToken)).Conteudo;

    public async Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(
        ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        static ResultadoFerramentaAgente Erro(string codigo) => new(RespostaFerramenta.Erro(codigo));

        // 1. Argumentos: só `agendamento_id` (uuid). Telefone, cliente_id, negocio_id, token... invalidam o pedido.
        var leitor = new LeitorArgumentos(argumentos, "agendamento_id");
        var pedido = leitor.Identificador("agendamento_id");
        if (!leitor.Valido)
            return Erro(ErrosFerramenta.ArgumentosInvalidos);

        // 2. Modo ligado e serviço contratado agora (a situação da rodada pode ter mudado).
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ApoioGestaoAgente.GestaoDisponivelAsync(_politica, negocioId, cancellationToken))
            return Erro(ErrosFerramenta.GestaoIndisponivel);

        // 3. Identidade (G1): só agendamento elegível DESTE número. Id alheio, inexistente, de outro negócio, de origem Painel/Encaixe,
        // de cliente excluído ou já passado dão a MESMA resposta (sem oráculo).
        var elegiveis = await _consulta.ListarAsync(contexto.ConversaId, ApoioGestaoAgente.MaximoAgendamentos, pedido, cancellationToken);
        if (elegiveis.Count == 0)
            return Erro(ErrosFerramenta.AgendamentoDesconhecido);

        if (pedido is null && elegiveis.Count > 1)
        {
            var nomeFuso = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.Fuso).FirstAsync(cancellationToken);
            var fuso = TimeZoneInfo.FindSystemTimeZoneById(nomeFuso);
            var opcoes = elegiveis.Select(e =>
            {
                var (data, hora, _) = ApoioGestaoAgente.DataEHora(e.Inicio, fuso);
                return new OpcaoDeAgendamentoDaFerramenta(e.Id, data, hora);
            }).ToList();
            return new(RespostaFerramenta.ErroComAgendamentos(ErrosFerramenta.EscolhaNecessaria, opcoes));
        }

        // 4. Limite de links por telefone no dia.
        if (await _limitador.LimiteDeLinksDeGestaoAtingidoAsync(contexto.ConversaId, DateTimeOffset.UtcNow, cancellationToken))
        {
            _logger.LogWarning("Agente: limite de links de gestão atingido (conversa {ConversaId}, negócio {NegocioId}).", contexto.ConversaId, negocioId);
            return Erro(ErrosFerramenta.LimiteGestao);
        }

        // 5. Efeito: só o sinal. O servidor monta o texto, gera o token e envia depois da rodada.
        _logger.LogInformation("Agente: ferramenta enviar_link_gestao aceita (conversa {ConversaId}, negócio {NegocioId}).", contexto.ConversaId, negocioId);
        return new ResultadoFerramentaAgente(
            RespostaFerramenta.De(new { status = Status }),
            Acao: new AcaoServidor(TipoAcaoServidor.LinkGestao, elegiveis[0].Id));
    }
}
