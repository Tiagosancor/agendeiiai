using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

public enum TipoPlanoConfirmacao
{
    /// <summary>Lote só de "sim" depois do resumo: confirma.</summary>
    Confirmar = 1,

    /// <summary>Lote só de "não": recusa e libera o horário.</summary>
    Recusar = 2,

    /// <summary>"Sim" depois que a pendência já expirou (o job passou): avisa que o prazo terminou.</summary>
    PrazoPassou = 3,

    /// <summary>A confirmação gravou mas a mensagem ao cliente nunca saiu (rodada interrompida): só falta anunciar.</summary>
    AnunciarConfirmada = 4,
}

public sealed record PlanoConfirmacao(TipoPlanoConfirmacao Tipo, Guid PendenciaId, Guid UltimaMensagemId);

/// <summary>
/// Mensagem fixa do servidor. <c>Texto</c> é o que o cliente recebe; <c>TextoGravado</c> é o que vai ao banco (e, dali, à IA): na
/// confirmação o link de gestão é trocado por um marcador — o token nunca é persistido nem enviado ao modelo.
/// </summary>
public sealed record MensagemDoServidor(string Texto, string TextoGravado, string Marcador);

/// <summary>
/// A confirmação do agendamento pelo WhatsApp, decidida pelo SERVIDOR e SEM chamar a IA: olha só o que o cliente escreveu depois do
/// resumo e aplica a lista fechada do <see cref="ClassificadorConfirmacaoAgente"/>. O modelo nunca confirma nada e não existe
/// ferramenta de confirmar. Roda no <see cref="ProcessadorRodadaAgente"/> antes dos limites de IA (custo zero) e em duas etapas —
/// <see cref="PlanejarAsync"/> (só lê) e <see cref="ExecutarAsync"/> (escreve) — para o processador conferir o lease entre elas.
/// </summary>
public sealed class ConfirmadorPendenciaAgente
{
    /// <summary>Quanto tempo depois do resumo um "sim" tardio ainda recebe o aviso de prazo (depois disso vai para a IA).</summary>
    private static readonly TimeSpan JanelaDoAvisoDePrazo = TimeSpan.FromHours(24);

    private const int MaximoMensagensNoLote = 20;

    private readonly PlataformaDbContext _dbContext;
    private readonly IServicoAgendamentos _agendamentos;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesMarca _marca;
    private readonly ILogger<ConfirmadorPendenciaAgente> _logger;

    public ConfirmadorPendenciaAgente(
        PlataformaDbContext dbContext, IServicoAgendamentos agendamentos, GerenciadorPendenciasAgente pendencias,
        IServicoTokenPublico servicoToken, IContextoNegocio contextoNegocio, IOptions<OpcoesMarca> marca,
        ILogger<ConfirmadorPendenciaAgente> logger)
    {
        _dbContext = dbContext;
        _agendamentos = agendamentos;
        _pendencias = pendencias;
        _servicoToken = servicoToken;
        _contextoNegocio = contextoNegocio;
        _marca = marca.Value;
        _logger = logger;
    }

    /// <summary>
    /// Decide, sem escrever nada, se o lote pendente da conversa (as mensagens do cliente depois de <c>ProcessadaAte</c> e até
    /// <paramref name="ultima"/>) é a resposta ao resumo. Nulo = não é: segue o fluxo normal (IA).
    /// </summary>
    public async Task<PlanoConfirmacao?> PlanejarAsync(ConversaWhatsApp conversa, DateTimeOffset ultima, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        // A pendência mais recente desta conversa; as anteriores já terminaram.
        var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == conversa.Id).OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(cancellationToken);
        if (pendencia is null)
            return null;

        var lote = await _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Usuario
                && (conversa.ProcessadaAte == null || m.CriadoEm > conversa.ProcessadaAte) && m.CriadoEm <= ultima)
            .OrderBy(m => m.CriadoEm).Take(MaximoMensagensNoLote + 1)
            .Select(m => new { m.Id, m.CriadoEm, m.Conteudo })
            .ToListAsync(cancellationToken);
        if (lote.Count == 0 || lote.Count > MaximoMensagensNoLote)
            return null;

        var ultimaMensagemId = lote[^1].Id;

        switch (pendencia.Status)
        {
            case StatusPendenciaAgendamentoWhatsApp.Aguardando:
            {
                // Sem resumo entregue não há o que responder; mensagem que veio ANTES do resumo (a rodada do preparo ainda corria) nunca vale.
                if (pendencia.ResumoEnviadoEm is not { } resumoEm || lote.Any(m => m.CriadoEm <= resumoEm))
                    return null;

                // Conservador: classifica TODO o intervalo depois do resumo, não só o lote pendente. Uma contestação já processada
                // ("troca para 15h", respondida pela IA sem substituir a pendência) invalida um "sim" posterior.
                var desdeOResumo = await MensagensDesdeOResumoAsync(conversa.Id, resumoEm, ultima, cancellationToken);
                if (desdeOResumo is null)
                    return null;

                return ClassificadorConfirmacaoAgente.ClassificarLote(desdeOResumo) switch
                {
                    RespostaDoCliente.Afirmativa => new PlanoConfirmacao(TipoPlanoConfirmacao.Confirmar, pendencia.Id, ultimaMensagemId),
                    RespostaDoCliente.Negativa => new PlanoConfirmacao(TipoPlanoConfirmacao.Recusar, pendencia.Id, ultimaMensagemId),
                    _ => null,
                };
            }

            case StatusPendenciaAgendamentoWhatsApp.Confirmada:
            {
                // Recuperação: a confirmação gravou a partir de uma mensagem deste lote mas o texto ao cliente nunca saiu.
                if (pendencia.ConfirmacaoMensagemId is not { } confirmouEm || lote.All(m => m.Id != confirmouEm) || pendencia.ConcluidaEm is not { } concluida)
                    return null;

                var jaAnunciada = await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                    m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Sistema
                        && m.NomeFerramenta == MarcadoresAgente.Confirmado && m.CriadoEm >= concluida, cancellationToken);
                return jaAnunciada ? null : new PlanoConfirmacao(TipoPlanoConfirmacao.AnunciarConfirmada, pendencia.Id, ultimaMensagemId);
            }

            case StatusPendenciaAgendamentoWhatsApp.Expirada:
            {
                // O job já expirou a pendência e o cliente responde "sim" tarde: um aviso só ("o prazo terminou"), nunca um agendamento.
                if (pendencia.ResumoEnviadoEm is not { } resumoEm || resumoEm < agora - JanelaDoAvisoDePrazo || lote.Any(m => m.CriadoEm <= resumoEm))
                    return null;
                var desdeOResumo = await MensagensDesdeOResumoAsync(conversa.Id, resumoEm, ultima, cancellationToken);
                if (desdeOResumo is null || ClassificadorConfirmacaoAgente.ClassificarLote(desdeOResumo) != RespostaDoCliente.Afirmativa)
                    return null;

                var jaAvisou = await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                    m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Sistema
                        && m.NomeFerramenta == MarcadoresAgente.PrazoPassou && m.CriadoEm >= resumoEm, cancellationToken);
                return jaAvisou ? null : new PlanoConfirmacao(TipoPlanoConfirmacao.PrazoPassou, pendencia.Id, ultimaMensagemId);
            }

            default:
                return null;
        }
    }

    /// <summary>Todas as mensagens do cliente depois do resumo e até <paramref name="ultima"/>; nulo se passam do máximo (não confirma).</summary>
    private async Task<IReadOnlyCollection<string?>?> MensagensDesdeOResumoAsync(Guid conversaId, DateTimeOffset resumoEm, DateTimeOffset ultima, CancellationToken cancellationToken)
    {
        var textos = await _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Usuario && m.CriadoEm > resumoEm && m.CriadoEm <= ultima)
            .OrderBy(m => m.CriadoEm).Take(MaximoMensagensNoLote + 1)
            .Select(m => m.Conteudo).ToListAsync(cancellationToken);
        return textos.Count == 0 || textos.Count > MaximoMensagensNoLote ? null : textos.Select(t => (string?)t).ToList();
    }

    /// <summary>Faz o que o plano manda e devolve a mensagem fixa a enviar (nulo = silêncio).</summary>
    public async Task<MensagemDoServidor?> ExecutarAsync(
        PlanoConfirmacao plano, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, CancellationToken cancellationToken)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == conversa.NegocioId)
            .Select(n => new { n.NomeExibido, n.Fuso, Slug = n.Slug.Valor }).FirstOrDefaultAsync(cancellationToken);
        if (negocio is null)
            return null;

        var textos = new TextosAgendamentoAgente(ConstrutorUrlPublica.Construir(_marca, negocio.Slug, string.Empty), instancia.Telefone);

        switch (plano.Tipo)
        {
            case TipoPlanoConfirmacao.Recusar:
                if (!await _pendencias.EncerrarAsync(plano.PendenciaId, DesfechoPendenciaAgente.Recusada, cancellationToken))
                    return null;
                _logger.LogInformation("Agente: agendamento recusado pelo cliente (conversa {ConversaId}, pendência {PendenciaId}).", conversa.Id, plano.PendenciaId);
                return Fixa(TextosAgendamentoAgente.AgendamentoNaoConfirmado, MarcadoresAgente.NaoConfirmado);

            case TipoPlanoConfirmacao.PrazoPassou:
                return Fixa(TextosAgendamentoAgente.PrazoPassou, MarcadoresAgente.PrazoPassou);

            case TipoPlanoConfirmacao.AnunciarConfirmada:
            {
                var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plano.PendenciaId, cancellationToken);
                return pendencia is null ? null : await MontarConfirmacaoAsync(pendencia.AgendamentoId, negocio.NomeExibido, negocio.Fuso, negocio.Slug, textos, cancellationToken);
            }

            default:
                return await ConfirmarAsync(plano, conversa, negocio.NomeExibido, negocio.Fuso, negocio.Slug, textos, cancellationToken);
        }
    }

    private async Task<MensagemDoServidor?> ConfirmarAsync(
        PlanoConfirmacao plano, ConversaWhatsApp conversa, string nomeNegocio, string fuso, string slug, TextosAgendamentoAgente textos, CancellationToken cancellationToken)
    {
        // O telefone é o da conversa (do webhook), o nome (cliente novo) é o guardado na pendência; o modelo não participa.
        var resultado = await _agendamentos.ConfirmarReservaAgenteAsync(
            new ConfirmarReservaAgente(plano.PendenciaId, conversa.TelefoneCliente, null, plano.UltimaMensagemId), cancellationToken);

        if (resultado.Sucesso)
        {
            // Só quem gravou a confirmação a anuncia: o "sim" repetido e a segunda rodada ficam em silêncio.
            if (!resultado.ConfirmadaAgora || resultado.AgendamentoId is not { } agendamentoId)
                return null;

            _logger.LogInformation("Agente: agendamento confirmado pelo cliente (conversa {ConversaId}, pendência {PendenciaId}).", conversa.Id, plano.PendenciaId);
            return await MontarConfirmacaoAsync(agendamentoId, nomeNegocio, fuso, slug, textos, cancellationToken);
        }

        _logger.LogInformation("Agente: confirmação recusada ({Motivo}; conversa {ConversaId}, pendência {PendenciaId}).", resultado.Motivo, conversa.Id, plano.PendenciaId);

        switch (resultado.Motivo)
        {
            case MotivoRecusaConfirmacaoAgente.AssinaturaSuspensa:
                return null;

            case MotivoRecusaConfirmacaoAgente.Expirada:
                return Fixa(TextosAgendamentoAgente.PrazoPassou, MarcadoresAgente.PrazoPassou);

            case MotivoRecusaConfirmacaoAgente.IndisponivelAgora:
                return Fixa(TextosAgendamentoAgente.HorarioIndisponivelNaConfirmacao, MarcadoresAgente.NaoConfirmado);

            case MotivoRecusaConfirmacaoAgente.Conflito:
                await _pendencias.EncerrarAsync(plano.PendenciaId, DesfechoPendenciaAgente.Falhou, cancellationToken);
                return Fixa(textos.HorarioIndisponivelComAlternativas(resultado.ProximosHorariosLivres, fuso), MarcadoresAgente.NaoConfirmado);

            default:
                // Nome faltando/inválido, pendência de outra conversa ou já encerrada: nada a fazer por aqui. Libera o que ficou preso.
                await _pendencias.EncerrarAsync(plano.PendenciaId, DesfechoPendenciaAgente.Falhou, cancellationToken);
                return Fixa(textos.NaoConsegui(), MarcadoresAgente.NaoConfirmado);
        }
    }

    private async Task<MensagemDoServidor?> MontarConfirmacaoAsync(
        Guid agendamentoId, string nomeNegocio, string fuso, string slug, TextosAgendamentoAgente textos, CancellationToken cancellationToken)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos).FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);
        var linha = agendamento?.Servicos.FirstOrDefault();
        if (agendamento is null || linha is null)
            return null;

        var profissional = await _dbContext.Profissionais.AsNoTracking().Where(p => p.Id == agendamento.ProfissionalId)
            .Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var dados = new DadosDoAgendamentoParaTexto(nomeNegocio, linha.Nome, linha.ValorCobrado, linha.DuracaoMinutos, profissional, agendamento.Inicio, fuso);

        // O link de gestão leva um token assinado (cancelar/remarcar, seção 6.3): vai SÓ ao cliente. O banco guarda o marcador.
        var token = _servicoToken.GerarTokenAgendamento(_contextoNegocio.NegocioId!.Value, agendamentoId);
        var link = ConstrutorUrlPublica.Construir(_marca, slug, $"/agendamentos/{token}");
        return new MensagemDoServidor(textos.Confirmacao(dados, link), textos.Confirmacao(dados, null), MarcadoresAgente.Confirmado);
    }

    private static MensagemDoServidor Fixa(string texto, string marcador) => new(texto, texto, marcador);
}
