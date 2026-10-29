using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

// Ferramentas de REMARCAÇÃO (Fase 4, Fatia 8, modo Completo). O modelo nunca informa negócio, telefone, cliente nem conversa; só o id
// do agendamento (opcional), a data e a hora. As regras vêm da mesma fonte do link público (IGestaoPublicaAgendamento) e a oferta de
// horários é a do link ("remarcar este agendamento"); quem move o agendamento é o servidor, depois do "SIM", em RemarcarPeloAgenteAsync.

public sealed record HorariosRemarcacaoDaFerramenta(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("dia_semana")] string DiaSemana,
    [property: JsonPropertyName("horarios")] IReadOnlyList<string> Horarios,
    [property: JsonPropertyName("truncado")] bool Truncado);

/// <summary>Pedaços comuns às duas ferramentas de remarcação.</summary>
internal static class ApoioRemarcacaoAgente
{
    public const int MaximoHorarios = 30;
    public const int MaximoAlternativas = 5;

    /// <summary>
    /// Identidade (G1) do agendamento a remarcar: só os elegíveis DESTE número. Id alheio, inexistente, de outro negócio, de origem
    /// Painel/Encaixe, de cliente excluído ou já passado dão a MESMA resposta (<c>agendamento_desconhecido</c>); mais de um sem id pede a escolha.
    /// </summary>
    public static async Task<(AgendamentoDoNumero? Alvo, ResultadoFerramentaAgente? Erro)> ResolverAlvoAsync(
        ConsultaAgendamentosDoClienteAgente consulta, PlataformaDbContext dbContext, Guid negocioId, Guid conversaId, Guid? pedido,
        CancellationToken cancellationToken)
    {
        var elegiveis = await consulta.ListarAsync(conversaId, ApoioGestaoAgente.MaximoAgendamentos, pedido, cancellationToken);
        if (elegiveis.Count == 0)
            return (null, new(RespostaFerramenta.Erro(ErrosFerramenta.AgendamentoDesconhecido)));

        if (pedido is null && elegiveis.Count > 1)
        {
            var nomeFuso = await dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.Fuso).FirstAsync(cancellationToken);
            var fuso = TimeZoneInfo.FindSystemTimeZoneById(nomeFuso);
            var opcoes = elegiveis.Select(e =>
            {
                var (data, hora, _) = ApoioGestaoAgente.DataEHora(e.Inicio, fuso);
                return new OpcaoDeAgendamentoDaFerramenta(e.Id, data, hora);
            }).ToList();
            return (null, new(RespostaFerramenta.ErroComAgendamentos(ErrosFerramenta.EscolhaNecessaria, opcoes)));
        }

        return (elegiveis[0], null);
    }

    /// <summary>A data (dia local do negócio) está entre hoje e o limite de consulta da Fase 2?</summary>
    public static async Task<bool> DataNoPeriodoAsync(
        PlataformaDbContext dbContext, OpcoesIa opcoes, Guid negocioId, DateOnly data, CancellationToken cancellationToken)
    {
        var nomeFuso = await dbContext.Negocios.AsNoTracking().Where(n => n.Id == negocioId).Select(n => n.Fuso).FirstAsync(cancellationToken);
        var hoje = ConversorFusoHorario.ParaLocal(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(nomeFuso)).Dia;
        return data >= hoje && data <= hoje.AddDays(opcoes.DiasMaximosConsulta);
    }

    public static string Hora(DateTimeOffset instante, TimeZoneInfo fuso) =>
        ConversorFusoHorario.ParaLocal(instante, fuso).Hora.ToString("HH':'mm", CultureInfo.InvariantCulture);
}

/// <summary>Leitura: horários livres de um dia para remarcar um agendamento do número (o mesmo motor do link; o horário atual não conta como ocupado).</summary>
public sealed class FerramentaConsultarHorariosRemarcacao : IFerramentaDeRemarcacaoAgente
{
    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly PlataformaDbContext _dbContext;
    private readonly PoliticaAgenteIa _politica;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IOptions<OpcoesIa> _opcoes;
    private readonly ILogger<FerramentaConsultarHorariosRemarcacao> _logger;

    public FerramentaConsultarHorariosRemarcacao(
        ConsultaAgendamentosDoClienteAgente consulta, IGestaoPublicaAgendamento gestao, PlataformaDbContext dbContext, PoliticaAgenteIa politica,
        IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes, ILogger<FerramentaConsultarHorariosRemarcacao> logger)
    {
        _consulta = consulta;
        _gestao = gestao;
        _dbContext = dbContext;
        _politica = politica;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes;
        _logger = logger;
    }

    public string Nome => "consultar_horarios_remarcacao";

    public string Descricao =>
        "Lista os horários livres de UM dia para REMARCAR um agendamento do cliente desta conversa (mesmo profissional e serviço dele). " +
        "agendamento_id é opcional se o cliente tiver só um agendamento; com mais de um, informe qual (id de listar_meus_agendamentos). " +
        "Use um horário devolvido aqui em preparar_remarcacao.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "agendamento_id":{"type":"string","description":"UUID do agendamento, obtido em listar_meus_agendamentos (opcional se houver só um)"},
          "data":{"type":"string","description":"Dia no formato AAAA-MM-DD"}},
         "required":["data"],"additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        // 1. Argumentos: só `agendamento_id` (uuid) e `data`. Telefone, cliente_id, negocio_id, token... invalidam o pedido.
        var leitor = new LeitorArgumentos(argumentos, "agendamento_id", "data");
        var pedido = leitor.Identificador("agendamento_id");
        var data = leitor.Data("data");
        if (!leitor.Valido || (data is null && !leitor.DataMalFormada))
            return RespostaFerramenta.Erro(ErrosFerramenta.ArgumentosInvalidos);
        if (leitor.DataMalFormada || data is null)
            return RespostaFerramenta.Erro(ErrosFerramenta.DataInvalida);

        // 2. Modo Completo e serviço contratado agora.
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ApoioGestaoAgente.GestaoDisponivelAsync(_politica, negocioId, cancellationToken, ModoGestaoAgente.Completo))
            return RespostaFerramenta.Erro(ErrosFerramenta.GestaoIndisponivel);

        // 3. Identidade (G1).
        var (alvo, erro) = await ApoioRemarcacaoAgente.ResolverAlvoAsync(_consulta, _dbContext, negocioId, contexto.ConversaId, pedido, cancellationToken);
        if (alvo is null)
            return erro!.Conteudo;

        if (!await ApoioRemarcacaoAgente.DataNoPeriodoAsync(_dbContext, _opcoes.Value, negocioId, data.Value, cancellationToken))
            return RespostaFerramenta.Erro(ErrosFerramenta.DataForaDoPeriodo);

        // 4. A mesma decisão e a mesma oferta do link ("remarcar este agendamento").
        var resultado = await _gestao.ListarHorariosParaRemarcacaoAsync(alvo.Id, data.Value, cancellationToken);
        if (resultado is null)
            return RespostaFerramenta.Erro(ErrosFerramenta.AgendamentoDesconhecido);
        if (resultado.Bloqueio is { } bloqueio)
            return RespostaFerramenta.ErroComMotivo(ErrosFerramenta.RemarcacaoNaoPermitida, bloqueio.CodigoMotivo ?? MotivosGestaoPublica.NaoPermitido);

        var oferta = resultado.Horarios!;
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(oferta.Fuso);
        var horarios = oferta.Horarios
            .Where(h => h != alvo.Inicio && ConversorFusoHorario.ParaLocal(h, fuso).Dia == data.Value)
            .OrderBy(h => h)
            .Select(h => ApoioRemarcacaoAgente.Hora(h, fuso))
            .ToList();

        _logger.LogInformation("Agente: consultar_horarios_remarcacao (conversa {ConversaId}, negócio {NegocioId}, {Quantidade} horário(s)).",
            contexto.ConversaId, negocioId, horarios.Count);
        return RespostaFerramenta.De(new HorariosRemarcacaoDaFerramenta(
            data.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), ConstrutorPromptAgente.NomeDoDia(data.Value.DayOfWeek),
            horarios.Take(ApoioRemarcacaoAgente.MaximoHorarios).ToList(), horarios.Count > ApoioRemarcacaoAgente.MaximoHorarios));
    }
}

/// <summary>
/// PREPARA a remarcação de um agendamento do número (modo <c>Completo</c>) e nunca a efetiva: valida no servidor, cria a
/// <see cref="PendenciaGestaoWhatsApp"/> (sem segurar o novo horário) e encerra a rodada; o servidor envia o resumo e só um "SIM" do cliente,
/// avaliado por código determinístico antes de qualquer chamada à IA, move o agendamento.
/// </summary>
public sealed class FerramentaPrepararRemarcacao : IFerramentaDeRemarcacaoAgente, IFerramentaDeEscritaAgente
{
    public const string Status = "resumo_sera_enviado";

    /// <summary>Quanto o pedido espera o "SIM". Igual ao do cancelamento.</summary>
    public static readonly TimeSpan Validade = FerramentaPrepararCancelamento.Validade;

    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly PlataformaDbContext _dbContext;
    private readonly PoliticaAgenteIa _politica;
    private readonly LimitadorUsoAgente _limitador;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IOptions<OpcoesIa> _opcoes;
    private readonly ILogger<FerramentaPrepararRemarcacao> _logger;

    public FerramentaPrepararRemarcacao(
        ConsultaAgendamentosDoClienteAgente consulta, IGestaoPublicaAgendamento gestao, PlataformaDbContext dbContext, PoliticaAgenteIa politica,
        LimitadorUsoAgente limitador, GerenciadorPendenciasAgente pendencias, IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes,
        ILogger<FerramentaPrepararRemarcacao> logger)
    {
        _consulta = consulta;
        _gestao = gestao;
        _dbContext = dbContext;
        _politica = politica;
        _limitador = limitador;
        _pendencias = pendencias;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes;
        _logger = logger;
    }

    public string Nome => "preparar_remarcacao";

    public string Descricao =>
        "Prepara a remarcação de um agendamento do cliente desta conversa para outro dia/horário (mesmo profissional): o sistema envia um resumo " +
        "e o cliente confirma com SIM. Não remarca nada por si. Use um horário devolvido por consultar_horarios_remarcacao. agendamento_id é opcional " +
        "se o cliente tiver só um agendamento; com mais de um, informe qual (id de listar_meus_agendamentos). Depois de chamar, o sistema envia o " +
        "resumo: não escreva mais nada. Chame no máximo uma vez por resposta.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "agendamento_id":{"type":"string","description":"UUID do agendamento, obtido em listar_meus_agendamentos (opcional se houver só um)"},
          "data":{"type":"string","description":"Novo dia no formato AAAA-MM-DD"},
          "hora":{"type":"string","description":"Nova hora no formato HH:mm (24 horas), igual a um horário devolvido por consultar_horarios_remarcacao"}},
         "required":["data","hora"],"additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
        (await ExecutarComSinalAsync(contexto, argumentos, cancellationToken)).Conteudo;

    public async Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(
        ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        static ResultadoFerramentaAgente Erro(string codigo) => new(RespostaFerramenta.Erro(codigo));

        // 1. Argumentos: só `agendamento_id` (uuid), `data` e `hora`. Telefone, cliente_id, negocio_id, token, forçar... invalidam o pedido.
        var leitor = new LeitorArgumentos(argumentos, "agendamento_id", "data", "hora");
        var pedido = leitor.Identificador("agendamento_id");
        var data = leitor.Data("data");
        var horaTexto = leitor.Texto("hora");
        if (!leitor.Valido || (data is null && !leitor.DataMalFormada) || horaTexto is null)
            return Erro(ErrosFerramenta.ArgumentosInvalidos);
        if (leitor.DataMalFormada || data is null)
            return Erro(ErrosFerramenta.DataInvalida);
        if (!TimeOnly.TryParseExact(horaTexto, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var hora))
            return Erro(ErrosFerramenta.HoraInvalida);

        // 2. Modo Completo e serviço contratado agora (a situação da rodada pode ter mudado).
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ApoioGestaoAgente.GestaoDisponivelAsync(_politica, negocioId, cancellationToken, ModoGestaoAgente.Completo))
            return Erro(ErrosFerramenta.GestaoIndisponivel);

        // 3. Identidade (G1).
        var (alvo, erroIdentidade) = await ApoioRemarcacaoAgente.ResolverAlvoAsync(_consulta, _dbContext, negocioId, contexto.ConversaId, pedido, cancellationToken);
        if (alvo is null)
            return erroIdentidade!;

        // 4. As MESMAS regras do link público (status e antecedência mínima) pela mesma fonte.
        var detalhe = await _gestao.ObterAsync(alvo.Id, cancellationToken);
        if (detalhe is null)
            return Erro(ErrosFerramenta.AgendamentoDesconhecido);
        if (!detalhe.Acoes.Remarcar.Permitido)
            return new(RespostaFerramenta.ErroComMotivo(ErrosFerramenta.RemarcacaoNaoPermitida, detalhe.Acoes.Remarcar.CodigoMotivo ?? MotivosGestaoPublica.NaoPermitido));

        if (!await ApoioRemarcacaoAgente.DataNoPeriodoAsync(_dbContext, _opcoes.Value, negocioId, data.Value, cancellationToken))
            return Erro(ErrosFerramenta.DataForaDoPeriodo);

        var fuso = TimeZoneInfo.FindSystemTimeZoneById(detalhe.Fuso);
        var novoInicio = ConversorFusoHorario.ParaUtc(data.Value, hora, fuso);
        if (novoInicio == detalhe.Inicio)
            return Erro(ErrosFerramenta.MesmoHorario);

        // 5. O mesmo pedido já aguardando: reaproveita (a rodada refeita não cria duas pendências nem reenvia o resumo).
        var agora = DateTimeOffset.UtcNow;
        var existente = await _pendencias.ObterGestaoAguardandoAsync(contexto.ConversaId, cancellationToken);
        if (existente is { Tipo: TipoGestaoWhatsApp.Remarcar } && existente.AgendamentoId == alvo.Id && existente.NovoInicio == novoInicio && existente.ExpiraEm > agora)
        {
            return existente.ResumoEnviadoEm is not null
                ? Erro(ErrosFerramenta.ResumoJaEnviado)
                : Sucesso(existente.Id);
        }

        // 6. Limites de gestão (macios), antes de criar qualquer pendência.
        var limite = await _limitador.VerificarGestaoAsync(contexto.ConversaId, agora, cancellationToken, TipoGestaoWhatsApp.Remarcar, alvo.Id);
        if (limite != MotivoLimiteGestao.Nenhum)
        {
            _logger.LogWarning("Agente: limite de gestão {Motivo} atingido (conversa {ConversaId}, negócio {NegocioId}).", limite, contexto.ConversaId, negocioId);
            return Erro(ErrosFerramenta.LimiteGestao);
        }

        // 7. O novo início precisa estar EXATAMENTE na oferta do link para este agendamento (a confirmação confere de novo, dentro da transação).
        var oferta = await _gestao.ListarHorariosParaRemarcacaoAsync(alvo.Id, data.Value, cancellationToken);
        if (oferta?.Horarios is not { } horarios)
            return Erro(ErrosFerramenta.AgendamentoDesconhecido);
        if (!horarios.Horarios.Contains(novoInicio))
            return new(RespostaFerramenta.ErroComAlternativas(ErrosFerramenta.HorarioIndisponivel, Alternativas(horarios.Horarios, detalhe.Inicio, hora, data.Value, fuso)));

        // 8. Efeito: a pendência (substitui a de agendamento aberta e libera a reserva, na mesma transação). O servidor envia o resumo.
        var pendencia = await _pendencias.CriarGestaoAsync(
            contexto.ConversaId, alvo.Id, TipoGestaoWhatsApp.Remarcar, agora, agora + Validade, novoInicio, cancellationToken,
            detalhe.Profissional.Id, detalhe.Inicio, detalhe.DuracaoMinutos);
        if (pendencia is null)
            return Erro(ErrosFerramenta.GestaoIndisponivel);

        _logger.LogInformation("Agente: remarcação preparada (conversa {ConversaId}, negócio {NegocioId}, pendência {PendenciaId}).",
            contexto.ConversaId, negocioId, pendencia.Id);
        return Sucesso(pendencia.Id);
    }

    private static ResultadoFerramentaAgente Sucesso(Guid pendenciaId) =>
        new(RespostaFerramenta.De(new { status = Status, expira_em_minutos = (int)Validade.TotalMinutes }),
            Acao: new AcaoServidor(TipoAcaoServidor.ResumoGestao, pendenciaId));

    /// <summary>Até 5 horários livres do mesmo dia, os mais próximos do pedido, em ordem de hora (o horário atual do agendamento fica de fora).</summary>
    private static List<string> Alternativas(IReadOnlyList<DateTimeOffset> livres, DateTimeOffset atual, TimeOnly pedida, DateOnly dia, TimeZoneInfo fuso) =>
        livres.Where(h => h != atual)
            .Select(h => ConversorFusoHorario.ParaLocal(h, fuso))
            .Where(h => h.Dia == dia)
            .Select(h => h.Hora)
            .OrderBy(h => Math.Abs((h.ToTimeSpan() - pedida.ToTimeSpan()).TotalMinutes))
            .Take(ApoioRemarcacaoAgente.MaximoAlternativas)
            .OrderBy(h => h)
            .Select(h => h.ToString("HH':'mm", CultureInfo.InvariantCulture))
            .ToList();
}
