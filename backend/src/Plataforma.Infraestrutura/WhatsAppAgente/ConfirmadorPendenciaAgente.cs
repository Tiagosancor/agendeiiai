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

    /// <summary>A confirmação já foi anunciada (entregue ou de resultado ambíguo): só falta dar o lote como tratado, sem chamar a IA.</summary>
    JaAnunciada = 5,

    // ---- Gestão (Fase 4, Fatia 7): cancelar pelo agente. Mesmas regras de duas etapas, lista afirmativa mais estrita.

    /// <summary>Lote só de "sim" (lista estrita) depois do resumo do cancelamento: cancela.</summary>
    ConfirmarCancelamento = 6,

    /// <summary>Lote só de "não" ao resumo do cancelamento: encerra o pedido e mantém o agendamento.</summary>
    RecusarGestao = 7,

    /// <summary>"Sim" depois que o pedido de cancelamento já expirou: avisa que nada foi cancelado.</summary>
    PrazoPassouGestao = 8,

    /// <summary>O cancelamento gravou mas a mensagem ao cliente nunca saiu (rodada interrompida): só falta anunciar.</summary>
    AnunciarGestaoEfetivada = 9,

    /// <summary>O cancelamento já foi anunciado (entregue ou de resultado ambíguo): só falta dar o lote como tratado, sem chamar a IA.</summary>
    JaAnunciadaGestao = 10,
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
    private readonly PoliticaAgenteIa _politica;
    private readonly ILogger<ConfirmadorPendenciaAgente> _logger;

    public ConfirmadorPendenciaAgente(
        PlataformaDbContext dbContext, IServicoAgendamentos agendamentos, GerenciadorPendenciasAgente pendencias,
        IServicoTokenPublico servicoToken, IContextoNegocio contextoNegocio, IOptions<OpcoesMarca> marca,
        PoliticaAgenteIa politica, ILogger<ConfirmadorPendenciaAgente> logger)
    {
        _politica = politica;
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
        // A pendência mais recente desta conversa, entre as DUAS tabelas (agendamento e gestão); as anteriores já terminaram. A criação
        // de uma substitui a outra, então só uma está aberta por vez.
        var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == conversa.Id).OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(cancellationToken);
        var gestao = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking()
            .Where(p => p.ConversaId == conversa.Id).OrderByDescending(p => p.CriadoEm).FirstOrDefaultAsync(cancellationToken);
        if (pendencia is null && gestao is null)
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

        if (gestao is not null && (pendencia is null || gestao.CriadoEm >= pendencia.CriadoEm))
        {
            return await PlanejarGestaoAsync(
                conversa, gestao, lote.Select(m => (m.Id, m.CriadoEm)).ToList(), ultima, agora, ultimaMensagemId, cancellationToken);
        }

        switch (pendencia!.Status)
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
                return new PlanoConfirmacao(jaAnunciada ? TipoPlanoConfirmacao.JaAnunciada : TipoPlanoConfirmacao.AnunciarConfirmada, pendencia.Id, ultimaMensagemId);
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

    /// <summary>
    /// Mesma decisão do agendamento para a pendência de GESTÃO: só o que o cliente escreveu depois do resumo, avaliado pela lista ESTRITA
    /// do cancelamento. Confirmar, recusar e avisar o prazo só valem com <c>Ai__Gestao__Modo</c> em Cancelar (ou acima): com o modo
    /// rebaixado o pedido apenas expira. Anunciar um cancelamento JÁ gravado é fato e nunca depende do modo.
    /// </summary>
    private async Task<PlanoConfirmacao?> PlanejarGestaoAsync(
        ConversaWhatsApp conversa, PendenciaGestaoWhatsApp pendencia, IReadOnlyList<(Guid Id, DateTimeOffset CriadoEm)> lote,
        DateTimeOffset ultima, DateTimeOffset agora, Guid ultimaMensagemId, CancellationToken cancellationToken)
    {
        var modoLigado = (await _politica.ObterAsync(conversa.NegocioId, cancellationToken)).Gestao >= ModoGestaoAgente.Cancelar;

        switch (pendencia.Status)
        {
            case StatusPendenciaAgendamentoWhatsApp.Aguardando:
            {
                if (!modoLigado || pendencia.Tipo != TipoGestaoWhatsApp.Cancelar)
                    return null;
                if (pendencia.ResumoEnviadoEm is not { } resumoEm || lote.Any(m => m.CriadoEm <= resumoEm))
                    return null;

                var desdeOResumo = await MensagensDesdeOResumoAsync(conversa.Id, resumoEm, ultima, cancellationToken);
                if (desdeOResumo is null)
                    return null;

                return ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(desdeOResumo) switch
                {
                    RespostaDoCliente.Afirmativa => new PlanoConfirmacao(TipoPlanoConfirmacao.ConfirmarCancelamento, pendencia.Id, ultimaMensagemId),
                    RespostaDoCliente.Negativa => new PlanoConfirmacao(TipoPlanoConfirmacao.RecusarGestao, pendencia.Id, ultimaMensagemId),
                    _ => null,
                };
            }

            case StatusPendenciaAgendamentoWhatsApp.Confirmada:
            {
                // Recuperação: o cancelamento gravou a partir de uma mensagem deste lote mas o texto ao cliente nunca saiu.
                if (pendencia.ConfirmacaoMensagemId is not { } confirmouEm || lote.All(m => m.Id != confirmouEm) || pendencia.ConcluidaEm is not { } concluida)
                    return null;

                var jaAnunciada = await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                    m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Sistema
                        && m.NomeFerramenta == MarcadoresAgente.GestaoEfetivada && m.CriadoEm >= concluida, cancellationToken);
                return new PlanoConfirmacao(
                    jaAnunciada ? TipoPlanoConfirmacao.JaAnunciadaGestao : TipoPlanoConfirmacao.AnunciarGestaoEfetivada, pendencia.Id, ultimaMensagemId);
            }

            case StatusPendenciaAgendamentoWhatsApp.Expirada:
            {
                // O job já expirou o pedido e o cliente responde "sim" tarde: um aviso só ("nada foi cancelado"), nunca um cancelamento.
                if (!modoLigado || pendencia.ResumoEnviadoEm is not { } resumoEm || resumoEm < agora - JanelaDoAvisoDePrazo
                    || lote.Any(m => m.CriadoEm <= resumoEm))
                    return null;
                var desdeOResumo = await MensagensDesdeOResumoAsync(conversa.Id, resumoEm, ultima, cancellationToken);
                if (desdeOResumo is null || ClassificadorConfirmacaoAgente.ClassificarLoteCancelamento(desdeOResumo) != RespostaDoCliente.Afirmativa)
                    return null;

                var jaAvisou = await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                    m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Sistema
                        && m.NomeFerramenta == MarcadoresAgente.GestaoPrazoPassou && m.CriadoEm >= resumoEm, cancellationToken);
                return jaAvisou ? null : new PlanoConfirmacao(TipoPlanoConfirmacao.PrazoPassouGestao, pendencia.Id, ultimaMensagemId);
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

            case TipoPlanoConfirmacao.JaAnunciada:
            case TipoPlanoConfirmacao.JaAnunciadaGestao:
                return null;

            case TipoPlanoConfirmacao.ConfirmarCancelamento:
                return await ConfirmarCancelamentoAsync(plano, conversa, negocio.NomeExibido, negocio.Fuso, textos, cancellationToken);

            case TipoPlanoConfirmacao.RecusarGestao:
                if (!await _pendencias.EncerrarGestaoAsync(plano.PendenciaId, DesfechoPendenciaAgente.Recusada, cancellationToken))
                    return null;
                _logger.LogInformation("Agente: cancelamento recusado pelo cliente (conversa {ConversaId}, pendência {PendenciaId}).", conversa.Id, plano.PendenciaId);
                return Fixa(TextosAgendamentoAgente.CancelamentoNaoEfetivado, MarcadoresAgente.GestaoNaoEfetivada);

            case TipoPlanoConfirmacao.PrazoPassouGestao:
                return Fixa(TextosAgendamentoAgente.CancelamentoPrazoPassou, MarcadoresAgente.GestaoPrazoPassou);

            case TipoPlanoConfirmacao.AnunciarGestaoEfetivada:
            {
                var gestao = await _dbContext.PendenciasGestaoWhatsApp.AsNoTracking().FirstOrDefaultAsync(p => p.Id == plano.PendenciaId, cancellationToken);
                return gestao is null ? null : await MontarCancelamentoEfetivadoAsync(gestao.AgendamentoId, negocio.NomeExibido, negocio.Fuso, textos, cancellationToken);
            }

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
            case MotivoRecusaConfirmacaoAgente.ServicoNaoContratado:
                // Silêncio: nada gravado; a pendência expira pelo job. (O gate do processador já cobre o caso comum; isto é a corrida.)
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

    private async Task<MensagemDoServidor?> ConfirmarCancelamentoAsync(
        PlanoConfirmacao plano, ConversaWhatsApp conversa, string nomeNegocio, string fuso, TextosAgendamentoAgente textos, CancellationToken cancellationToken)
    {
        // Barreira do modo (o rebaixamento no meio de uma conversa não deixa um "sim" velho cancelar): sem efeito, a pendência expira pelo job.
        if ((await _politica.ObterAsync(conversa.NegocioId, cancellationToken)).Gestao < ModoGestaoAgente.Cancelar)
            return null;

        // O telefone é o da conversa (do webhook); o modelo não participa. A operação é um Guid aleatório, nunca um instante.
        var resultado = await _agendamentos.CancelarPeloAgenteAsync(
            new CancelarPeloAgente(plano.PendenciaId, conversa.TelefoneCliente, Guid.NewGuid(), plano.UltimaMensagemId), cancellationToken);

        if (resultado.Sucesso)
        {
            // Só quem gravou o cancelamento o anuncia: o "sim" repetido e a segunda rodada ficam em silêncio.
            if (!resultado.EfetivadaAgora || resultado.AgendamentoId is not { } agendamentoId)
                return null;

            _logger.LogInformation("Agente: cancelamento efetivado pelo cliente (conversa {ConversaId}, pendência {PendenciaId}).", conversa.Id, plano.PendenciaId);
            return await MontarCancelamentoEfetivadoAsync(agendamentoId, nomeNegocio, fuso, textos, cancellationToken);
        }

        _logger.LogInformation("Agente: cancelamento recusado ({Motivo}; conversa {ConversaId}, pendência {PendenciaId}).", resultado.Motivo, conversa.Id, plano.PendenciaId);

        switch (resultado.Motivo)
        {
            case MotivoRecusaGestaoAgente.AssinaturaSuspensa:
            case MotivoRecusaGestaoAgente.ServicoNaoContratado:
                // Silêncio: nada gravado; o pedido expira pelo job. (O gate do processador já cobre o caso comum; isto é a corrida.)
                return null;

            case MotivoRecusaGestaoAgente.Expirada:
                return Fixa(TextosAgendamentoAgente.CancelamentoPrazoPassou, MarcadoresAgente.GestaoPrazoPassou);

            case MotivoRecusaGestaoAgente.NaoEncontrada:
                await _pendencias.EncerrarGestaoAsync(plano.PendenciaId, DesfechoPendenciaAgente.Falhou, cancellationToken);
                return Fixa(textos.CancelamentoRecusado(resultado.Motivo), MarcadoresAgente.GestaoNaoEfetivada);

            default:
                // Antecedência, status, pagamento, quinzena fechada, vínculo perdido ou pedido já encerrado: texto neutro e nada cancelado
                // (o próprio serviço já encerrou a pendência na mesma transação).
                return Fixa(textos.CancelamentoRecusado(resultado.Motivo), MarcadoresAgente.GestaoNaoEfetivada);
        }
    }

    private async Task<MensagemDoServidor?> MontarCancelamentoEfetivadoAsync(
        Guid agendamentoId, string nomeNegocio, string fuso, TextosAgendamentoAgente textos, CancellationToken cancellationToken)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos).FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);
        if (agendamento is null || agendamento.Servicos.Count == 0)
            return null;

        var profissional = await _dbContext.Profissionais.AsNoTracking().Where(p => p.Id == agendamento.ProfissionalId)
            .Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var dados = new DadosDoAgendamentoParaTexto(
            nomeNegocio, string.Join(", ", agendamento.Servicos.Select(s => s.Nome)), 0m, agendamento.Servicos.Sum(s => s.DuracaoMinutos),
            profissional, agendamento.Inicio, fuso);
        var texto = textos.CancelamentoEfetivado(dados);
        return new MensagemDoServidor(texto, texto, MarcadoresAgente.GestaoEfetivada);
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
