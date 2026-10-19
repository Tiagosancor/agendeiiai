using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
