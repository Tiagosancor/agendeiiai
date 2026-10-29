using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

public enum MotivoLimiteAgente
{
    Nenhum = 0,
    TelefonePorHora = 1,
    TelefonePorDia = 2,
    NegocioPorDia = 3,
    CotaMensal = 4,
}

public enum MotivoLimiteGestao
{
    Nenhum = 0,
    PreparosPorDia = 1,
    CancelamentosPorDia = 2,
    OperacoesNegocioPorDia = 3,
    RemarcacoesPorDia = 4,
    RemarcacoesPorAgendamento = 5,
}

public enum MotivoLimiteEscrita
{
    Nenhum = 0,
    PreparosPorDia = 1,
    FuturosPorTelefone = 2,
    ConfirmacoesPorDia = 3,
    ReservasAtivasNegocio = 4,
}

/// <summary>
/// Limites de custo aplicados ANTES de chamar a IA (A4), contados no Postgres (não dependem do Redis): mensagens do cliente
/// por hora/dia, chamadas ao modelo do negócio no dia e tokens do negócio no mês civil UTC. São "macios" sob concorrência
/// extrema (contagem antes da chamada). Roda sob o filtro global: só enxerga o negócio do contexto.
/// </summary>
public sealed class LimitadorUsoAgente
{
    private const string MarcadorAviso = "limite_aviso";

    private readonly PlataformaDbContext _dbContext;
    private readonly OpcoesIa _opcoes;

    public LimitadorUsoAgente(PlataformaDbContext dbContext, IOptions<OpcoesIa> opcoes)
    {
        _dbContext = dbContext;
        _opcoes = opcoes.Value;
    }

    public async Task<MotivoLimiteAgente> VerificarAsync(Guid conversaId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var umaHoraAtras = agora.AddHours(-1);
        var umDiaAtras = agora.AddDays(-1);

        // Conversa devolvida do atendimento humano: o que veio antes de `devolvida_em` (acumulado durante o atendimento) não conta (G12-f).
        var devolvidaEm = await _dbContext.ConversasWhatsApp.AsNoTracking().Where(c => c.Id == conversaId)
            .Select(c => c.DevolvidaEm).FirstOrDefaultAsync(cancellationToken);
        if (devolvidaEm is { } devolvida)
        {
            if (devolvida > umaHoraAtras)
                umaHoraAtras = devolvida;
            if (devolvida > umDiaAtras)
                umDiaAtras = devolvida;
        }

        // Uma conversa = um telefone numa instância; contar por conversa é contar por telefone.
        var mensagens = _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Usuario);

        if (await mensagens.CountAsync(m => m.CriadoEm >= umaHoraAtras, cancellationToken) > _opcoes.LimiteMensagensPorTelefonePorHora)
            return MotivoLimiteAgente.TelefonePorHora;

        if (await mensagens.CountAsync(m => m.CriadoEm >= umDiaAtras, cancellationToken) > _opcoes.LimiteMensagensPorTelefonePorDia)
            return MotivoLimiteAgente.TelefonePorDia;

        return await VerificarCustoAsync(agora, cancellationToken);
    }

    /// <summary>
    /// Só os limites de custo do negócio (chamadas no dia e tokens no mês). Reavaliado ANTES DE CADA chamada ao modelo dentro
    /// da rodada, para o laço de ferramentas não passar do teto que a primeira checagem deixou passar.
    /// </summary>
    public async Task<MotivoLimiteAgente> VerificarCustoAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        if (await _dbContext.UsosIa.AsNoTracking().CountAsync(u => u.CriadoEm >= inicioDoDia, cancellationToken) >= _opcoes.LimiteChamadasPorNegocioPorDia)
            return MotivoLimiteAgente.NegocioPorDia;

        var inicioDoMes = new DateTimeOffset(agora.UtcDateTime.Year, agora.UtcDateTime.Month, 1, 0, 0, 0, TimeSpan.Zero);
        var tokens = await _dbContext.UsosIa.AsNoTracking()
            .Where(u => u.CriadoEm >= inicioDoMes)
            .SumAsync(u => (long)u.TokensEntrada + u.TokensSaida, cancellationToken);
        if (tokens >= _opcoes.CotaMensalTokensPorNegocio)
            return MotivoLimiteAgente.CotaMensal;

        return MotivoLimiteAgente.Nenhum;
    }

    /// <summary>
    /// Limites de ESCRITA (Fase 3), chamados pela ferramenta de preparar agendamento ANTES de reservar: preparos por telefone no dia,
    /// agendamentos futuros de origem WhatsApp do telefone, confirmações por telefone no dia e reservas ativas do agente no negócio.
    /// Contados no Postgres sob o filtro global (só o negócio do contexto). São "macios" sob concorrência extrema; o rígido
    /// (uma pendência por conversa) é o índice único parcial. Dia = dia UTC, como os demais limites.
    /// </summary>
    public async Task<MotivoLimiteEscrita> VerificarEscritaAsync(
        Guid conversaId, TelefoneE164 telefone, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var limites = _opcoes.Agendamento;
        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);

        // Uma conversa = um telefone numa instância (uma instância por negócio): contar por conversa é contar por telefone.
        var daConversa = _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking().Where(p => p.ConversaId == conversaId);

        if (await daConversa.CountAsync(p => p.CriadoEm >= inicioDoDia, cancellationToken) >= limites.LimitePreparosPorTelefonePorDia)
            return MotivoLimiteEscrita.PreparosPorDia;

        if (await daConversa.CountAsync(
                p => p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= inicioDoDia, cancellationToken)
            >= limites.LimiteConfirmacoesPorTelefonePorDia)
            return MotivoLimiteEscrita.ConfirmacoesPorDia;

        // A reserva desta própria conversa será substituída pelo preparo novo, então não conta.
        if (await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking().CountAsync(
                p => p.ConversaId != conversaId && p.Status == StatusPendenciaAgendamentoWhatsApp.Aguardando && p.ExpiraEm > agora,
                cancellationToken) >= limites.LimiteReservasAtivasPorNegocio)
            return MotivoLimiteEscrita.ReservasAtivasNegocio;

        var clienteIds = new List<Guid>();
        foreach (var forma in telefone.FormasEquivalentes())
        {
            clienteIds.AddRange(await _dbContext.Clientes.AsNoTracking()
                .Where(c => c.Telefone == forma).Select(c => c.Id).ToListAsync(cancellationToken));
        }

        if (clienteIds.Count > 0
            && await _dbContext.Agendamentos.AsNoTracking().CountAsync(
                a => a.Origem == OrigemAgendamento.WhatsApp && a.Status == StatusAgendamento.Agendado && a.Inicio > agora
                    && a.ClienteId != null && clienteIds.Contains(a.ClienteId.Value), cancellationToken) >= limites.LimiteFuturosPorTelefone)
            return MotivoLimiteEscrita.FuturosPorTelefone;

        return MotivoLimiteEscrita.Nenhum;
    }

    /// <summary>
    /// Fase 4: o telefone (uma conversa = um telefone numa instância) já recebeu <c>Ai__Gestao__LimiteLinksPorTelefonePorDia</c> links de
    /// gestão no dia UTC? Conta as mensagens <c>gestao_link</c> que o servidor reservou ao enviar. "Macio" sob concorrência.
    /// </summary>
    public async Task<bool> LimiteDeLinksDeGestaoAtingidoAsync(Guid conversaId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        var enviados = await _dbContext.MensagensWhatsApp.AsNoTracking().CountAsync(
            m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Sistema
                && m.NomeFerramenta == MarcadoresAgente.GestaoLink && m.CriadoEm >= inicioDoDia, cancellationToken);
        return enviados >= _opcoes.Gestao.LimiteLinksPorTelefonePorDia;
    }

    /// <summary>
    /// Fase 4 (Fatia 7): limites de cancelar ANTES de criar a pendência — pedidos preparados por telefone no dia, cancelamentos efetivados
    /// por telefone no dia e operações efetivadas (cancelar + remarcar) por negócio no dia. Dia UTC; contados no Postgres sob o filtro
    /// global; "macios" sob concorrência (o rígido é o índice único de uma pendência por conversa). Uma conversa = um telefone.
    /// </summary>
    public async Task<MotivoLimiteGestao> VerificarGestaoAsync(
        Guid conversaId, DateTimeOffset agora, CancellationToken cancellationToken,
        TipoGestaoWhatsApp tipo = TipoGestaoWhatsApp.Cancelar, Guid? agendamentoId = null)
    {
        var limites = _opcoes.Gestao;
        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        var daConversa = _dbContext.PendenciasGestaoWhatsApp.AsNoTracking().Where(p => p.ConversaId == conversaId);

        if (await daConversa.CountAsync(p => p.CriadoEm >= inicioDoDia, cancellationToken) >= limites.LimitePreparosPorTelefonePorDia)
            return MotivoLimiteGestao.PreparosPorDia;

        if (tipo == TipoGestaoWhatsApp.Cancelar
            && await daConversa.CountAsync(
                p => p.Tipo == TipoGestaoWhatsApp.Cancelar && p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= inicioDoDia,
                cancellationToken) >= limites.LimiteCancelamentosPorTelefonePorDia)
            return MotivoLimiteGestao.CancelamentosPorDia;

        // Remarcações (Fatia 8): efetivadas pelo telefone no dia e, no total, para o mesmo agendamento.
        if (tipo == TipoGestaoWhatsApp.Remarcar)
        {
            if (await daConversa.CountAsync(
                    p => p.Tipo == TipoGestaoWhatsApp.Remarcar && p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= inicioDoDia,
                    cancellationToken) >= limites.LimiteRemarcacoesPorTelefonePorDia)
                return MotivoLimiteGestao.RemarcacoesPorDia;

            if (agendamentoId is { } idAgendamento
                && await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking().CountAsync(
                    p => p.AgendamentoId == idAgendamento && p.Tipo == TipoGestaoWhatsApp.Remarcar && p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada,
                    cancellationToken) >= limites.LimiteRemarcacoesPorAgendamento)
                return MotivoLimiteGestao.RemarcacoesPorAgendamento;
        }

        if (await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking().CountAsync(
                p => p.Status == StatusPendenciaAgendamentoWhatsApp.Confirmada && p.ConcluidaEm >= inicioDoDia, cancellationToken)
            >= limites.LimiteOperacoesPorNegocioPorDia)
            return MotivoLimiteGestao.OperacoesNegocioPorDia;

        return MotivoLimiteGestao.Nenhum;
    }

    /// <summary>
    /// Já avisamos esta conversa neste tipo de janela? O aviso é uma mensagem fixa registrada como <c>Sistema</c>;
    /// uma por hora (limite horário) ou por dia (demais), depois silêncio — corta também o laço entre dois bots.
    /// </summary>
    public Task<bool> JaAvisouAsync(Guid conversaId, MotivoLimiteAgente motivo, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var desde = motivo == MotivoLimiteAgente.TelefonePorHora ? agora.AddHours(-1) : agora.AddDays(-1);
        return _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
            m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Sistema && m.NomeFerramenta == MarcadorAviso && m.CriadoEm >= desde,
            cancellationToken);
    }

    public static MensagemWhatsApp CriarAviso(Guid negocioId, Guid instanciaId, Guid conversaId, string idExterno, string texto) =>
        new(negocioId, instanciaId, conversaId, idExterno, PapelMensagemWhatsApp.Sistema, texto, MarcadorAviso);

    public static string TextoAviso(string nomeNegocio, string link) =>
        $"Recebi muitas mensagens por agora e preciso pausar o atendimento automático de {ConstrutorPromptAgente.LimparNome(nomeNegocio)}. " +
        $"Para ver os horários e agendar, acesse: {link}";
}
