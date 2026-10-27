using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Publico;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>
/// ÚNICA ferramenta de escrita do agente, e mesmo assim só PREPARA: valida o pedido no servidor, reserva o horário (reserva de
/// 10 min, o mesmo caminho do assistente público) e cria a pendência. Nunca confirma — quem confirma é o servidor, depois que o
/// cliente responder "sim" ao resumo que ele mesmo monta. O esquema não tem negócio, telefone, cliente nem forçar: o cliente é
/// sempre o telefone da conversa. Retornos e erros são textos fixos; nada de cadastro volta ao modelo.
/// </summary>
public sealed class FerramentaPrepararAgendamento : IFerramentaDeEscritaAgente
{
    public const string Status = "resumo_sera_enviado";
    private const int MaximoAlternativas = 5;

    private readonly IConsultaCatalogoPublico _catalogo;
    private readonly IConsultaHorariosPublicos _horarios;
    private readonly IServicoAgendamentos _agendamentos;
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IOptions<OpcoesIa> _opcoes;
    private readonly LimitadorUsoAgente _limitador;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly ILogger<FerramentaPrepararAgendamento> _logger;

    public FerramentaPrepararAgendamento(
        IConsultaCatalogoPublico catalogo, IConsultaHorariosPublicos horarios, IServicoAgendamentos agendamentos, PlataformaDbContext dbContext,
        IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes, LimitadorUsoAgente limitador, GerenciadorPendenciasAgente pendencias,
        ILogger<FerramentaPrepararAgendamento> logger)
    {
        _catalogo = catalogo;
        _horarios = horarios;
        _agendamentos = agendamentos;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes;
        _limitador = limitador;
        _pendencias = pendencias;
        _logger = logger;
    }

    public string Nome => "preparar_agendamento";

    public string Descricao =>
        "Prepara o agendamento de UM serviço para o cliente desta conversa: reserva o horário e o sistema envia o resumo para o cliente " +
        "confirmar com SIM. Não confirma nada. Use um horário devolvido por consultar_horarios. Chame no máximo uma vez por resposta.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "servico_id":{"type":"string","description":"UUID do serviço, obtido em listar_servicos"},
          "profissional_id":{"type":"string","description":"UUID do profissional, obtido em listar_profissionais ou consultar_horarios"},
          "data":{"type":"string","description":"Dia no formato AAAA-MM-DD"},
          "hora":{"type":"string","description":"Hora no formato HH:mm (24 horas), igual a um horário devolvido por consultar_horarios"},
          "nome_cliente":{"type":"string","description":"Nome do cliente, SOMENTE se a ferramenta devolver nome_necessario e o cliente tiver digitado o nome"}},
         "required":["servico_id","profissional_id","data","hora"],"additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default) =>
        (await ExecutarComSinalAsync(contexto, argumentos, cancellationToken)).Conteudo;

    public async Task<ResultadoFerramentaAgente> ExecutarComSinalAsync(
        ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        static ResultadoFerramentaAgente Erro(string codigo) => new(RespostaFerramenta.Erro(codigo));

        // 1. Argumentos (campos extras como forcar, telefone, cliente_id e negocio_id invalidam o pedido).
        var (pedido, erroArgumentos) = ArgumentosPrepararAgendamento.Ler(argumentos);
        if (pedido is null)
            return Erro(erroArgumentos ?? ErrosFerramenta.ArgumentosInvalidos);

        // 2. Modo/piloto: o executor já não oferece a ferramenta fora dele; isto é a segunda barreira (modo desligado durante a rodada).
        var negocioId = _contextoNegocio.NegocioId!.Value;
        if (!_opcoes.Value.AgendamentoAtivoPara(negocioId))
            return Erro(ErrosFerramenta.AgendamentoIndisponivel);

        // O telefone vem SÓ da conversa (servidor); o modelo nunca o informa nem o recebe.
        var conversa = await _dbContext.ConversasWhatsApp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == contexto.ConversaId, cancellationToken);
        if (conversa is null || !TelefoneE164.TentarCriar(conversa.TelefoneCliente, out var telefone) || telefone is null)
            return Erro(ErrosFerramenta.AgendamentoIndisponivel);

        // Ids validados contra o catálogo público do negócio (filtro global): id de outro negócio é "desconhecido".
        var servico = (await _catalogo.ListarServicosAsync(cancellationToken)).SelectMany(c => c.Servicos).FirstOrDefault(s => s.Id == pedido.ServicoId);
        if (servico is null)
            return Erro(ErrosFerramenta.ServicoDesconhecido);

        var profissional = (await _catalogo.ListarProfissionaisAsync(cancellationToken)).FirstOrDefault(p => p.Id == pedido.ProfissionalId);
        if (profissional is null)
            return Erro(ErrosFerramenta.ProfissionalDesconhecido);
        if (!profissional.ServicoIds.Contains(servico.Id))
            return Erro(ErrosFerramenta.ProfissionalNaoFazServico);

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
        var agora = DateTimeOffset.UtcNow;
        var hoje = ConversorFusoHorario.ParaLocal(agora, fuso).Dia;
        if (pedido.Data < hoje || pedido.Data > hoje.AddDays(_opcoes.Value.DiasMaximosConsulta))
            return Erro(ErrosFerramenta.DataForaDoPeriodo);

        var inicio = ConversorFusoHorario.ParaUtc(pedido.Data, pedido.Hora, fuso);

        // 3. Pendência igual já aguardando: reaproveita (a rodada refeita pelo modelo de reserva não reserva duas vezes).
        var existente = await _pendencias.ObterAguardandoAsync(conversa.Id, cancellationToken);
        if (existente is not null && existente.ExpiraEm > agora && await EhOMesmoPedidoAsync(existente.AgendamentoId, pedido, inicio, cancellationToken))
        {
            return existente.ResumoEnviadoEm is not null
                ? Erro(ErrosFerramenta.ResumoJaEnviado)
                : Sucesso(existente.Id);
        }

        // 4. Limites de escrita (macios), antes de qualquer reserva.
        var limite = await _limitador.VerificarEscritaAsync(conversa.Id, telefone, agora, cancellationToken);
        if (limite != MotivoLimiteEscrita.Nenhum)
        {
            _logger.LogWarning("Agente: limite de escrita {Motivo} atingido (conversa {ConversaId}, negócio {NegocioId}).", limite, conversa.Id, negocioId);
            return Erro(ErrosFerramenta.LimiteAgendamentos);
        }

        // 5. O horário precisa estar EXATAMENTE na oferta pública (mesma lista do assistente web e de consultar_horarios).
        var livres = await _horarios.ListarAsync(pedido.Data, servico.DuracaoMinutos, [servico.Id], profissional.Id, 200, cancellationToken);
        if (!livres.Any(h => h.ProfissionalId == profissional.Id && h.Inicio == inicio))
            return new(RespostaFerramenta.ErroComAlternativas(ErrosFerramenta.HorarioIndisponivel, Alternativas(livres, profissional.Id, pedido, fuso)));

        // 6. Cliente novo precisa do nome que ELE digitou; cliente existente: o parâmetro é ignorado e o cadastro nunca vai ao modelo.
        string? nomeParaPendencia = null;
        if (!await ClienteExisteAsync(negocioId, telefone, cancellationToken))
        {
            if (pedido.NomeCliente is null)
                return Erro(ErrosFerramenta.NomeNecessario);
            if (!ArgumentosPrepararAgendamento.ValidarNome(pedido.NomeCliente, out var nome))
                return Erro(ErrosFerramenta.NomeInvalido);
            nomeParaPendencia = nome;
        }

        // 7. Libera a pendência anterior desta conversa (ordem de travas: pendência, depois agendamento), reserva e cria a nova.
        if (existente is not null)
            await _pendencias.EncerrarAsync(existente.Id, DesfechoPendenciaAgente.Substituida, cancellationToken);

        var reserva = await _agendamentos.CriarReservaAgenteAsync(
            new CriarReservaAgente(profissional.Id, servico.Id, inicio), cancellationToken);
        if (!reserva.Sucesso || reserva.AgendamentoId is null)
        {
            if (reserva.Conflito)
            {
                var alternativas = (reserva.ProximosHorariosLivres ?? [])
                    .Select(h => ConversorFusoHorario.ParaLocal(h, fuso))
                    .Where(h => h.Dia == pedido.Data)
                    .Select(h => h.Hora.ToString("HH':'mm", CultureInfo.InvariantCulture))
                    .Take(MaximoAlternativas).ToList();
                return new(RespostaFerramenta.ErroComAlternativas(ErrosFerramenta.HorarioIndisponivel, alternativas));
            }

            _logger.LogInformation("Agente: reserva recusada (conversa {ConversaId}, negócio {NegocioId}).", conversa.Id, negocioId);
            return Erro(ErrosFerramenta.AgendamentoIndisponivel);
        }

        var expiraEm = await _dbContext.Agendamentos.AsNoTracking().Where(a => a.Id == reserva.AgendamentoId)
            .Select(a => a.ReservadoAte).FirstOrDefaultAsync(cancellationToken) ?? agora.AddMinutes(10);

        var pendencia = await _pendencias.CriarAsync(conversa.Id, reserva.AgendamentoId.Value, DateTimeOffset.UtcNow, expiraEm, nomeParaPendencia, cancellationToken);
        if (pendencia is null)
            return Erro(ErrosFerramenta.AgendamentoIndisponivel);

        _logger.LogInformation("Agente: agendamento preparado (conversa {ConversaId}, negócio {NegocioId}, pendência {PendenciaId}).", conversa.Id, negocioId, pendencia.Id);
        return Sucesso(pendencia.Id);
    }

    private static ResultadoFerramentaAgente Sucesso(Guid pendenciaId) =>
        new(RespostaFerramenta.De(new { status = Status, expira_em_minutos = (int)ServicoAgendamentos.DuracaoDaReserva.TotalMinutes }), pendenciaId);

    private async Task<bool> EhOMesmoPedidoAsync(Guid agendamentoId, PedidoDeAgendamento pedido, DateTimeOffset inicio, CancellationToken cancellationToken) =>
        await _dbContext.Agendamentos.AsNoTracking().AnyAsync(
            a => a.Id == agendamentoId && a.Status == StatusAgendamento.Reservado && a.ProfissionalId == pedido.ProfissionalId
                && a.Inicio == inicio && a.Servicos.Any(s => s.ServicoId == pedido.ServicoId), cancellationToken);

    private async Task<bool> ClienteExisteAsync(Guid negocioId, TelefoneE164 telefone, CancellationToken cancellationToken)
    {
        foreach (var forma in telefone.FormasEquivalentes())
        {
            if (await _dbContext.Clientes.AsNoTracking().AnyAsync(c => c.NegocioId == negocioId && c.Telefone == forma, cancellationToken))
                return true;
        }

        return false;
    }

    /// <summary>Até 5 horários livres do mesmo profissional no mesmo dia, os mais próximos do pedido, em ordem de hora.</summary>
    private static List<string> Alternativas(IReadOnlyList<HorarioLivre> livres, Guid profissionalId, PedidoDeAgendamento pedido, TimeZoneInfo fuso) =>
        livres.Where(h => h.ProfissionalId == profissionalId)
            .Select(h => ConversorFusoHorario.ParaLocal(h.Inicio, fuso))
            .Where(h => h.Dia == pedido.Data)
            .Select(h => h.Hora)
            .OrderBy(h => Math.Abs((h.ToTimeSpan() - pedido.Hora.ToTimeSpan()).TotalMinutes))
            .Take(MaximoAlternativas)
            .OrderBy(h => h)
            .Select(h => h.ToString("HH':'mm", CultureInfo.InvariantCulture))
            .ToList();
}
