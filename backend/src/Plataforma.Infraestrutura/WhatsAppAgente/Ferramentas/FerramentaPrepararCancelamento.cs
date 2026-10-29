using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>
/// PREPARA o cancelamento de um agendamento do número (Fase 4, Fatia 7, modo <c>Cancelar</c>) e nunca o efetiva: valida no servidor,
/// cria a <see cref="PendenciaGestaoWhatsApp"/> e encerra a rodada; o servidor envia o resumo e só um "SIM" do cliente, avaliado por
/// código determinístico antes de qualquer chamada à IA, cancela de fato. O esquema só tem <c>agendamento_id</c> (opcional): negócio,
/// telefone, cliente, motivo, token e "forçar" não existem e invalidam o pedido. O mesmo pedido de gestão por rodada vale para as
/// demais ferramentas de escrita (o orquestrador limita a UMA execução de ferramenta de escrita por rodada).
/// </summary>
public sealed class FerramentaPrepararCancelamento : IFerramentaDeCancelamentoAgente, IFerramentaDeEscritaAgente
{
    public const string Status = "resumo_sera_enviado";

    /// <summary>Quanto o pedido espera o "SIM". Igual à reserva de horário do agendamento pelo agente.</summary>
    public static readonly TimeSpan Validade = TimeSpan.FromMinutes(10);

    private readonly ConsultaAgendamentosDoClienteAgente _consulta;
    private readonly IGestaoPublicaAgendamento _gestao;
    private readonly PlataformaDbContext _dbContext;
    private readonly PoliticaAgenteIa _politica;
    private readonly LimitadorUsoAgente _limitador;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly ILogger<FerramentaPrepararCancelamento> _logger;

    public FerramentaPrepararCancelamento(
        ConsultaAgendamentosDoClienteAgente consulta, IGestaoPublicaAgendamento gestao, PlataformaDbContext dbContext, PoliticaAgenteIa politica,
        LimitadorUsoAgente limitador, GerenciadorPendenciasAgente pendencias, IContextoNegocio contextoNegocio,
        ILogger<FerramentaPrepararCancelamento> logger)
    {
        _consulta = consulta;
        _gestao = gestao;
        _dbContext = dbContext;
        _politica = politica;
        _limitador = limitador;
        _pendencias = pendencias;
        _contextoNegocio = contextoNegocio;
        _logger = logger;
    }

    public string Nome => "preparar_cancelamento";

    public string Descricao =>
        "Prepara o cancelamento de um agendamento do cliente desta conversa: o sistema envia um resumo e o cliente confirma com SIM. " +
        "Não cancela nada por si. agendamento_id é opcional se o cliente tiver só um agendamento; com mais de um, informe qual (id de listar_meus_agendamentos). " +
        "Depois de chamar, o sistema envia o resumo: não escreva mais nada. Chame no máximo uma vez por resposta.";

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

        // 1. Argumentos: só `agendamento_id` (uuid). Telefone, cliente_id, negocio_id, token, motivo... invalidam o pedido.
        var leitor = new LeitorArgumentos(argumentos, "agendamento_id");
        var pedido = leitor.Identificador("agendamento_id");
        if (!leitor.Valido)
            return Erro(ErrosFerramenta.ArgumentosInvalidos);

        // 2. Modo Cancelar e serviço contratado agora (a situação da rodada pode ter mudado).
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!await ApoioGestaoAgente.GestaoDisponivelAsync(_politica, negocioId, cancellationToken, ModoGestaoAgente.Cancelar))
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

        var alvo = elegiveis[0];

        // 4. As MESMAS regras do link público (status, antecedência mínima, quinzena fechada) pela mesma fonte, mais a do pagamento.
        var detalhe = await _gestao.ObterAsync(alvo.Id, cancellationToken);
        if (detalhe is null)
            return Erro(ErrosFerramenta.AgendamentoDesconhecido);

        if (!detalhe.Acoes.Cancelar.Permitido)
            return new(RespostaFerramenta.ErroComMotivo(ErrosFerramenta.CancelamentoNaoPermitido, detalhe.Acoes.Cancelar.CodigoMotivo ?? MotivosGestaoPublica.NaoPermitido));

        if (await _dbContext.Pagamentos.AsNoTracking().AnyAsync(p => p.AgendamentoId == alvo.Id && p.NegocioId == negocioId, cancellationToken))
            return new(RespostaFerramenta.ErroComMotivo(ErrosFerramenta.CancelamentoNaoPermitido, MotivoComPagamento));

        // 5. O mesmo pedido já aguardando: reaproveita (a rodada refeita não cria duas pendências nem reenvia o resumo).
        var agora = DateTimeOffset.UtcNow;
        var existente = await _pendencias.ObterGestaoAguardandoAsync(contexto.ConversaId, cancellationToken);
        if (existente is { Tipo: TipoGestaoWhatsApp.Cancelar } && existente.AgendamentoId == alvo.Id && existente.ExpiraEm > agora)
        {
            return existente.ResumoEnviadoEm is not null
                ? Erro(ErrosFerramenta.ResumoJaEnviado)
                : Sucesso(existente.Id);
        }

        // 6. Limites de gestão (macios), antes de criar qualquer pendência.
        var limite = await _limitador.VerificarGestaoAsync(contexto.ConversaId, agora, cancellationToken);
        if (limite != MotivoLimiteGestao.Nenhum)
        {
            _logger.LogWarning("Agente: limite de gestão {Motivo} atingido (conversa {ConversaId}, negócio {NegocioId}).", limite, contexto.ConversaId, negocioId);
            return Erro(ErrosFerramenta.LimiteGestao);
        }

        // 7. Efeito: a pendência (substitui a de agendamento aberta e libera a reserva, na mesma transação). O servidor envia o resumo.
        var pendencia = await _pendencias.CriarGestaoAsync(
            contexto.ConversaId, alvo.Id, TipoGestaoWhatsApp.Cancelar, agora, agora + Validade, null, cancellationToken);
        if (pendencia is null)
            return Erro(ErrosFerramenta.GestaoIndisponivel);

        _logger.LogInformation("Agente: cancelamento preparado (conversa {ConversaId}, negócio {NegocioId}, pendência {PendenciaId}).",
            contexto.ConversaId, negocioId, pendencia.Id);
        return Sucesso(pendencia.Id);
    }

    /// <summary>Código do motivo quando há pagamento registrado (o agente não cancela). Fora da lista do link público.</summary>
    public const string MotivoComPagamento = "com_pagamento";

    private static ResultadoFerramentaAgente Sucesso(Guid pendenciaId) =>
        new(RespostaFerramenta.De(new { status = Status, expira_em_minutos = (int)Validade.TotalMinutes }),
            Acao: new AcaoServidor(TipoAcaoServidor.ResumoGestao, pendenciaId));
}
