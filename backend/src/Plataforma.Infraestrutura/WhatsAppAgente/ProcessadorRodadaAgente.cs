using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Uma rodada do agente numa conversa, FORA da requisição do webhook. Roda num escopo de DI novo: o único ponto sem negócio
/// resolvido é carregar a conversa por Id (<c>IgnoreQueryFilters</c>); logo em seguida o contexto é definido e tudo passa pelo
/// filtro global. Uma rodada por conversa por vez (lease no Postgres): rajadas de mensagens viram uma ou duas rodadas.
/// </summary>
public sealed class ProcessadorRodadaAgente : IProcessadorRodadaAgente
{
    /// <summary>Lease ao tomar a conversa; cobre o prazo da IA (<see cref="PrazoResposta"/>) com folga para consultas e persistência.</summary>
    public static readonly TimeSpan DuracaoLease = TimeSpan.FromSeconds(120);

    /// <summary>Prazo duro para gerar a resposta (laço de IA e ferramentas), qualquer que seja o orçamento configurado.</summary>
    public static readonly TimeSpan PrazoResposta = TimeSpan.FromSeconds(90);

    /// <summary>Quanto o lease vale a partir da renovação feita imediatamente antes de marcar/enviar (Evolution tem timeout próprio).</summary>
    /// <remarks>
    /// Toda a etapa de envio + persistência é cortada por um token que expira antes dele (<see cref="MargemEnvio"/>), então o
    /// timeout da Evolution (<c>Evolution__TimeoutSegundos</c>, padrão 10 s, sem repetição) precisa ficar bem abaixo disto — se for
    /// maior, o corte do lease é quem manda. Só os testes alteram este valor.
    /// </remarks>
    public static TimeSpan DuracaoEnvio { get; set; } = TimeSpan.FromSeconds(45);

    /// <summary>Folga antes do fim do lease em que o envio é abandonado (o dono ainda é o dono ao gravar/soltar).</summary>
    public static TimeSpan MargemEnvio { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Mensagem mais velha que isto (por exemplo depois de um deploy) não é mais respondida: confundiria o cliente.</summary>
    public static readonly TimeSpan IdadeMaximaParaResponder = TimeSpan.FromMinutes(10);

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IRespostaAgente _resposta;
    private readonly IClienteEvolution _evolution;
    private readonly IConsultaSituacaoAssinatura _assinatura;
    private readonly LimitadorUsoAgente _limitador;
    private readonly OpcoesIa _opcoes;
    private readonly OpcoesMarca _marca;
    private readonly GerenciadorPendenciasAgente _pendencias;
    private readonly ILogger<ProcessadorRodadaAgente> _logger;

    public ProcessadorRodadaAgente(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IRespostaAgente resposta, IClienteEvolution evolution,
        IConsultaSituacaoAssinatura assinatura, LimitadorUsoAgente limitador, IOptions<OpcoesIa> opcoes, IOptions<OpcoesMarca> marca,
        GerenciadorPendenciasAgente pendencias, ILogger<ProcessadorRodadaAgente> logger)
    {
        _pendencias = pendencias;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _resposta = resposta;
        _evolution = evolution;
        _assinatura = assinatura;
        _limitador = limitador;
        _opcoes = opcoes.Value;
        _marca = marca.Value;
        _logger = logger;
    }

    public async Task ProcessarAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        // Único ponto sem negócio resolvido: a fila só carrega o Id da conversa.
        var negocioId = await _dbContext.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Id == conversaId).Select(c => (Guid?)c.NegocioId).FirstOrDefaultAsync(cancellationToken);
        if (negocioId is null)
            return;

        _contextoNegocio.Definir(negocioId.Value);

        // Uma rodada + no máximo uma extra se chegou mensagem nova no meio.
        for (var rodada = 0; rodada < 2; rodada++)
        {
            var lease = await TomarLeaseAsync(conversaId, cancellationToken);
            if (lease is null)
                return;

            try
            {
                await ExecutarRodadaAsync(conversaId, lease, cancellationToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning("Agente: falha na rodada (conversa {ConversaId}): {Tipo}.", conversaId, excecao.GetType().Name);
            }
            finally
            {
                // Solta o lease mesmo cancelado (senão a conversa fica presa até vencer), mas SÓ se ainda for nosso.
                await SoltarLeaseAsync(lease);
            }

            if (!await HaPendentesAsync(conversaId, cancellationToken))
                return;
        }
    }

    // ---------------------------------------------------------------- lease

    /// <summary>
    /// Posse de uma rodada. O "dono" é o próprio valor gravado em <c>processando_ate</c> (microssegundos + sufixo aleatório, único por
    /// tomada): renovar e soltar só atingem a linha enquanto ela ainda tiver exatamente este valor, então quem perdeu o lease por
    /// expiração não consegue limpar nem estender o de quem o assumiu depois.
    /// </summary>
    private sealed class Lease(Guid conversaId, DateTimeOffset valor)
    {
        public Guid ConversaId { get; } = conversaId;

        public DateTimeOffset Valor { get; set; } = valor;
    }

    private static DateTimeOffset NovoValorDeLease(DateTimeOffset agora, TimeSpan duracao)
    {
        // O Postgres guarda microssegundos: o valor tem que sobreviver à ida e volta sem perder precisão. O sufixo evita colisão
        // entre duas tomadas no mesmo microssegundo.
        var ticks = (agora + duracao).UtcTicks;
        ticks -= ticks % 10;
        ticks += Random.Shared.Next(0, 1000) * 10L;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private async Task<Lease?> TomarLeaseAsync(Guid conversaId, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var ate = NovoValorDeLease(agora, DuracaoLease);
        var linhas = await _dbContext.ConversasWhatsApp
            .Where(c => c.Id == conversaId && (c.ProcessandoAte == null || c.ProcessandoAte < agora))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProcessandoAte, (DateTimeOffset?)ate), cancellationToken);
        return linhas == 1 ? new Lease(conversaId, ate) : null;
    }

    /// <summary>
    /// Confere a posse e a renova (por <see cref="DuracaoEnvio"/>) numa única operação atômica. Falso = perdemos o lease
    /// (expirou ou outro processador assumiu): não se envia nem se marca nada.
    /// </summary>
    private async Task<bool> RenovarLeaseAsync(Lease lease, CancellationToken cancellationToken)
    {
        var agora = DateTimeOffset.UtcNow;
        var atual = lease.Valor;
        var novo = NovoValorDeLease(agora, DuracaoEnvio);
        var linhas = await _dbContext.ConversasWhatsApp
            .Where(c => c.Id == lease.ConversaId && c.ProcessandoAte == atual && c.ProcessandoAte > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProcessandoAte, (DateTimeOffset?)novo), cancellationToken);
        if (linhas == 1)
            lease.Valor = novo;
        else
            _logger.LogWarning("Agente: lease perdido (conversa {ConversaId}); a rodada não envia nem marca nada.", lease.ConversaId);
        return linhas == 1;
    }

    private async Task SoltarLeaseAsync(Lease lease)
    {
        var atual = lease.Valor;
        await _dbContext.ConversasWhatsApp.Where(c => c.Id == lease.ConversaId && c.ProcessandoAte == atual)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProcessandoAte, (DateTimeOffset?)null), CancellationToken.None);
    }

    /// <summary>
    /// Marca o lote como tratado SÓ se o lease ainda for nosso (e não tiver vencido). Falso = perdemos a posse: o chamador não
    /// envia nada. Linha já marcada mais adiante não é falha (continua verdadeiro).
    /// </summary>
    private async Task<bool> MarcarProcessadaAsync(Lease lease, DateTimeOffset ate, CancellationToken cancellationToken)
    {
        var dono = lease.Valor;
        var agora = DateTimeOffset.UtcNow;
        var linhas = await _dbContext.ConversasWhatsApp
            .Where(c => c.Id == lease.ConversaId && c.ProcessandoAte == dono && c.ProcessandoAte > agora)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ProcessadaAte,
                c => c.ProcessadaAte == null || c.ProcessadaAte < ate ? (DateTimeOffset?)ate : c.ProcessadaAte), cancellationToken);
        if (linhas != 1)
            _logger.LogWarning("Agente: lease perdido ao marcar (conversa {ConversaId}); a rodada não envia nada.", lease.ConversaId);
        return linhas == 1;
    }

    private async Task<bool> HaPendentesAsync(Guid conversaId, CancellationToken cancellationToken)
    {
        var conversa = await _dbContext.ConversasWhatsApp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversaId, cancellationToken);
        if (conversa is null)
            return false;

        return await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
            m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Usuario
                && (conversa.ProcessadaAte == null || m.CriadoEm > conversa.ProcessadaAte), cancellationToken);
    }

    // ---------------------------------------------------------------- rodada

    private async Task ExecutarRodadaAsync(Guid conversaId, Lease lease, CancellationToken cancellationToken)
    {
        var conversa = await _dbContext.ConversasWhatsApp.AsNoTracking().FirstOrDefaultAsync(c => c.Id == conversaId, cancellationToken);
        var instancia = conversa is null
            ? null
            : await _dbContext.InstanciasWhatsApp.AsNoTracking().FirstOrDefaultAsync(i => i.Id == conversa.InstanciaId, cancellationToken);
        if (conversa is null || instancia is null)
            return;

        // Só a pendente MAIS RECENTE é lida: ela dá o limite do lote ("processado até"), a idade e o texto recebido. O histórico tem
        // consulta própria, limitada em quantidade; um backlog grande nunca é materializado aqui.
        var maisRecente = await _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == conversaId && m.Papel == PapelMensagemWhatsApp.Usuario
                && (conversa.ProcessadaAte == null || m.CriadoEm > conversa.ProcessadaAte))
            .OrderByDescending(m => m.CriadoEm)
            .Select(m => new { m.CriadoEm, m.Conteudo })
            .FirstOrDefaultAsync(cancellationToken);
        if (maisRecente is null)
            return;

        var ultima = maisRecente.CriadoEm;
        var agora = DateTimeOffset.UtcNow;

        if (!await DeveResponderAsync(conversa, instancia, cancellationToken))
        {
            if (await RenovarLeaseAsync(lease, cancellationToken))
                await MarcarProcessadaAsync(lease, ultima, cancellationToken);
            return;
        }

        // Só responde o que é recente; o resto (ex.: pendente antes de um deploy) é dado como tratado.
        // Se a mais recente já é velha, todas as pendentes são.
        if (maisRecente.CriadoEm < agora - IdadeMaximaParaResponder)
        {
            if (await RenovarLeaseAsync(lease, cancellationToken))
                await MarcarProcessadaAsync(lease, ultima, cancellationToken);
            return;
        }

        if (_opcoes.Provider == ProvedorIa.Gemini)
        {
            var motivo = await _limitador.VerificarAsync(conversaId, agora, cancellationToken);
            if (motivo != MotivoLimiteAgente.Nenhum)
            {
                _logger.LogWarning("Agente: limite {Motivo} atingido (conversa {ConversaId}, negócio {NegocioId}).",
                    motivo, conversaId, conversa.NegocioId);
                if (!await RenovarLeaseAsync(lease, cancellationToken))
                    return;

                // Marca (com a posse conferida) ANTES de avisar: o aviso nunca é reenviado em loop e quem perdeu o lease não o envia.
                if (!await MarcarProcessadaAsync(lease, ultima, cancellationToken))
                    return;

                await AvisarLimiteAsync(lease, conversa, instancia, motivo, agora, cancellationToken);
                return;
            }
        }

        // Prazo duro só para gerar a resposta; a renovação logo abaixo dá ao envio o seu próprio fôlego.
        var resposta = new RespostaAgente(null);
        using (var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            prazo.CancelAfter(PrazoResposta);
            try
            {
                // Mesmo lote para histórico e para o "processado": a IA só vê mensagens até `ultima`.
                resposta = await _resposta.ResponderAsync(
                    new ContextoRespostaAgente(conversa.NegocioId, instancia.Id, conversa.Id, conversa.TelefoneCliente,
                        maisRecente.Conteudo, ultima),
                    prazo.Token);
            }
            catch (Exception excecao) when (!(excecao is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning("Agente: falha ao responder (conversa {ConversaId}, instância {InstanciaId}): {Tipo}.",
                    conversaId, instancia.Id, excecao.GetType().Name);
            }
        }

        // Quem perdeu o lease durante a IA não envia nem marca: o dono atual cuida da conversa.
        if (!await RenovarLeaseAsync(lease, cancellationToken))
            return;

        // Marca antes de enviar: se o envio falhar, não reenvia em loop (a mensagem do cliente já está salva).
        if (!await MarcarProcessadaAsync(lease, ultima, cancellationToken))
            return;

        // Preparou um agendamento nesta rodada: o texto do modelo é descartado e o servidor envia o resumo.
        if (resposta.PendenciaPreparadaId is { } pendenciaId)
            await EnviarResumoAsync(lease, conversa, instancia, pendenciaId, cancellationToken);
        else if (!string.IsNullOrWhiteSpace(resposta.Texto))
            await EnviarEGravarAsync(lease, conversa, instancia, resposta.Texto, PapelMensagemWhatsApp.Assistente, null, cancellationToken);
    }

    // ---------------------------------------------------------------- resumo do agendamento (Fase 3)

    /// <summary>
    /// Envia o resumo montado pelo SERVIDOR (nunca pelo modelo) da pendência recém-criada. A mensagem é gravada como <c>Sistema</c>
    /// (marcador <c>agendamento_resumo</c>) ANTES do envio, junto com o <c>ResumoEnviadoEm</c> da pendência: o "sim" só vale para
    /// mensagens recebidas depois dele. Falha definitiva da Evolution (ou sem prazo de lease) libera o horário; timeout, que é
    /// resultado ambíguo, mantém a pendência.
    /// </summary>
    private async Task EnviarResumoAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, Guid pendenciaId, CancellationToken cancellationToken)
    {
        var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.FirstOrDefaultAsync(
            p => p.Id == pendenciaId && p.ConversaId == conversa.Id, cancellationToken);
        if (pendencia is null || !pendencia.Aguardando)
            return;

        var texto = await MontarResumoAsync(pendencia, instancia, cancellationToken);
        if (texto is null)
        {
            await _pendencias.EncerrarAsync(pendenciaId, DesfechoPendenciaAgente.Falhou, cancellationToken);
            return;
        }

        var envio = await EnviarEGravarAsync(
            lease, conversa, instancia, texto, PapelMensagemWhatsApp.Sistema, MarcadoresAgente.Resumo, cancellationToken,
            aoReservar: reserva => pendencia.RegistrarResumoEnviado(reserva.Id, DateTimeOffset.UtcNow));

        if (envio.Estado is EstadoEnvio.FalhaDefinitiva or EstadoEnvio.SemPrazo)
        {
            // O cliente não recebeu o resumo: a mensagem reservada sai do histórico e a reserva do horário é liberada.
            if (envio.MensagemId is { } mensagemId)
                await _dbContext.MensagensWhatsApp.Where(m => m.Id == mensagemId).ExecuteDeleteAsync(CancellationToken.None);
            await _pendencias.EncerrarAsync(pendenciaId, DesfechoPendenciaAgente.Falhou, CancellationToken.None);
        }
    }

    private async Task<string?> MontarResumoAsync(PendenciaAgendamentoWhatsApp pendencia, InstanciaWhatsApp instancia, CancellationToken cancellationToken)
    {
        var agendamento = await _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == pendencia.AgendamentoId, cancellationToken);
        var linha = agendamento?.Servicos.FirstOrDefault();
        if (agendamento is null || linha is null)
            return null;

        var profissional = await _dbContext.Profissionais.AsNoTracking().Where(p => p.Id == agendamento.ProfissionalId)
            .Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken);
        var negocio = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == pendencia.NegocioId)
            .Select(n => new { n.NomeExibido, n.Fuso, Slug = n.Slug.Valor }).FirstOrDefaultAsync(cancellationToken);
        if (profissional is null || negocio is null)
            return null;

        var textos = new TextosAgendamentoAgente(ConstrutorUrlPublica.Construir(_marca, negocio.Slug, string.Empty), instancia.Telefone);
        var dados = new DadosDoAgendamentoParaTexto(
            negocio.NomeExibido, linha.Nome, linha.ValorCobrado, linha.DuracaoMinutos, profissional, agendamento.Inicio, negocio.Fuso);
        return textos.Resumo(dados, pendencia.NomeInformado, (int)Agendamentos.ServicoAgendamentos.DuracaoDaReserva.TotalMinutes);
    }

    private async Task<bool> DeveResponderAsync(ConversaWhatsApp conversa, InstanciaWhatsApp instancia, CancellationToken cancellationToken)
    {
        if (_opcoes.Provider == ProvedorIa.Desligado || !instancia.AgenteAtivo || !conversa.AgenteResponde)
            return false;

        // Eco é o comportamento da Fase 1: sem piloto nem checagem de assinatura (não gasta nada).
        if (_opcoes.Provider == ProvedorIa.Eco)
            return true;

        if (!_opcoes.NegocioHabilitado(conversa.NegocioId))
            return false;

        // Assinatura suspensa: silêncio, sem chamar a IA (custo). Sem assinatura cadastrada não bloqueia.
        var situacao = await _assinatura.ObterAsync(conversa.NegocioId, cancellationToken);
        return situacao is null || situacao.PermiteOperar;
    }

    private async Task AvisarLimiteAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, MotivoLimiteAgente motivo, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (await _limitador.JaAvisouAsync(conversa.Id, motivo, agora, cancellationToken))
            return;

        var slug = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == conversa.NegocioId)
            .Select(n => new { n.NomeExibido, Slug = n.Slug.Valor }).FirstOrDefaultAsync(cancellationToken);
        if (slug is null)
            return;

        var link = ConstrutorUrlPublica.Construir(_marca, slug.Slug, string.Empty);
        // O nome do negócio é cadastro livre: o aviso fixo passa pelo mesmo sanitizador da resposta do modelo.
        var aviso = SanitizadorRespostaAgente.SanitizarTextoFixo(
            LimitadorUsoAgente.TextoAviso(slug.NomeExibido, link), link, instancia.Telefone);
        await EnviarEGravarAsync(lease, conversa, instancia, aviso, PapelMensagemWhatsApp.Sistema, "limite_aviso", cancellationToken);
    }

    /// <summary>
    /// Envia pela Evolution e grava, tudo dentro do prazo restante do lease (menos a margem): estourou = abandona, loga só IDs e
    /// não grava, para nunca responder junto com quem assumiu a conversa. Falha do provedor é contida.
    /// </summary>
    private async Task<ResultadoEnvio> EnviarEGravarAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, string texto, PapelMensagemWhatsApp papel, string? marcador,
        CancellationToken cancellationToken, string? textoGravado = null, Action<MensagemWhatsApp>? aoReservar = null)
    {
        var restante = lease.Valor - DateTimeOffset.UtcNow - MargemEnvio;
        if (restante <= TimeSpan.Zero)
        {
            _logger.LogWarning("Agente: sem prazo de lease para enviar (conversa {ConversaId}).", conversa.Id);
            return new ResultadoEnvio(EstadoEnvio.SemPrazo);
        }

        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(restante);
        Guid? mensagemId = null;
        var enviou = false;
        try
        {
            if (marcador is null)
            {
                string? idEnvio;
                try
                {
                    idEnvio = await _evolution.EnviarTextoAsync(instancia.NomeInstancia, conversa.TelefoneCliente, texto, prazo.Token);
                }
                catch (FalhaEvolutionException falha) when (!falha.Ambigua)
                {
                    _logger.LogWarning("Agente: falha ao enviar resposta (conversa {ConversaId}, instância {InstanciaId}): {Tipo}.",
                        conversa.Id, instancia.Id, falha.GetType().Name);
                    return new ResultadoEnvio(EstadoEnvio.FalhaDefinitiva);
                }

                enviou = true;
                _dbContext.MensagensWhatsApp.Add(new MensagemWhatsApp(
                    instancia.NegocioId, instancia.Id, conversa.Id, idEnvio, papel, textoGravado ?? texto, marcador));
                await _dbContext.SaveChangesAsync(prazo.Token);
                return new ResultadoEnvio(EstadoEnvio.Enviado);
            }

            // Mensagem com marcador de supressão (aviso de limite, resumo, confirmação): a reserva é gravada ANTES da chamada externa, ainda
            // sob a posse do lease. Se a Evolution aceitar e depois vier timeout/falha ao gravar, o resultado é desconhecido e a mensagem
            // fica registrada: um aviso perdido é melhor que um aviso repetido a cada mensagem do cliente na mesma janela.
            // O que vai ao banco pode diferir do que vai ao cliente (a confirmação grava "[link de gestão enviado]", nunca o token).
            var reserva = new MensagemWhatsApp(instancia.NegocioId, instancia.Id, conversa.Id, null, papel, textoGravado ?? texto, marcador);
            mensagemId = reserva.Id;
            _dbContext.MensagensWhatsApp.Add(reserva);
            aoReservar?.Invoke(reserva);
            await _dbContext.SaveChangesAsync(prazo.Token);

            string? id;
            try
            {
                id = await _evolution.EnviarTextoAsync(instancia.NomeInstancia, conversa.TelefoneCliente, texto, prazo.Token);
            }
            catch (FalhaEvolutionException falha) when (!falha.Ambigua)
            {
                _dbContext.ChangeTracker.Clear();
                _logger.LogWarning("Agente: falha ao enviar resposta (conversa {ConversaId}, instância {InstanciaId}): {Tipo}.",
                    conversa.Id, instancia.Id, falha.GetType().Name);
                return new ResultadoEnvio(EstadoEnvio.FalhaDefinitiva, mensagemId);
            }

            enviou = true;
            reserva.DefinirIdExterno(id);
            await _dbContext.SaveChangesAsync(prazo.Token);
            return new ResultadoEnvio(EstadoEnvio.Enviado, mensagemId);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _dbContext.ChangeTracker.Clear();
            _logger.LogWarning("Agente: envio abandonado por fim do prazo do lease (conversa {ConversaId}, instância {InstanciaId}).",
                conversa.Id, instancia.Id);
            return new ResultadoEnvio(EstadoEnvio.Ambiguo, mensagemId);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _dbContext.ChangeTracker.Clear();
            _logger.LogWarning("Agente: falha ao enviar resposta (conversa {ConversaId}, instância {InstanciaId}): {Tipo}.",
                conversa.Id, instancia.Id, excecao.GetType().Name);
            return new ResultadoEnvio(enviou ? EstadoEnvio.Enviado : EstadoEnvio.Ambiguo, mensagemId);
        }
    }

    private enum EstadoEnvio
    {
        Enviado,

        /// <summary>A Evolution recusou ou estava fora do ar: o cliente com certeza não recebeu.</summary>
        FalhaDefinitiva,

        /// <summary>Timeout ou fim do prazo do lease: pode ter sido enviado.</summary>
        Ambiguo,

        /// <summary>Não sobrou prazo de lease: nada foi enviado nem gravado.</summary>
        SemPrazo,
    }

    private readonly record struct ResultadoEnvio(EstadoEnvio Estado, Guid? MensagemId = null);
}
