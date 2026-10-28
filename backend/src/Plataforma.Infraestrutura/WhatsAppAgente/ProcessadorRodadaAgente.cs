using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly ConfirmadorPendenciaAgente _confirmador;
    private readonly ILogger<ProcessadorRodadaAgente> _logger;
    private readonly IServiceScopeFactory _escopos;
    private readonly PoliticaAgenteIa _politica;

    public ProcessadorRodadaAgente(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IRespostaAgente resposta, IClienteEvolution evolution,
        IConsultaSituacaoAssinatura assinatura, LimitadorUsoAgente limitador, IOptions<OpcoesIa> opcoes, IOptions<OpcoesMarca> marca,
        GerenciadorPendenciasAgente pendencias, ConfirmadorPendenciaAgente confirmador, ILogger<ProcessadorRodadaAgente> logger,
        IServiceScopeFactory escopos, PoliticaAgenteIa politica)
    {
        _politica = politica;
        _escopos = escopos;
        _pendencias = pendencias;
        _confirmador = confirmador;
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

            // O escopo atravessa até duas rodadas: a situação do serviço premium é lida de novo a cada uma.
            _politica.Invalidar();
            var diagnostico = new DiagnosticoRodada(_opcoes.RegistrarRodadas ? Guid.NewGuid() : null);
            try
            {
                await ExecutarRodadaAsync(conversaId, lease, diagnostico, cancellationToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                _dbContext.ChangeTracker.Clear();
                diagnostico.Desfecho = DesfechoRodadaAgente.Erro;
                _logger.LogWarning("Agente: falha na rodada (conversa {ConversaId}): {Tipo}.", conversaId, excecao.GetType().Name);
            }
            finally
            {
                // Solta o lease mesmo cancelado (senão a conversa fica presa até vencer), mas SÓ se ainda for nosso.
                await SoltarLeaseAsync(lease);
                await GravarRodadaAsync(diagnostico, negocioId.Value, conversaId);
            }

            if (!await HaPendentesAsync(conversaId, cancellationToken))
                return;
        }
    }

    // ---------------------------------------------------------------- diagnóstico persistente (Fase 4)

    /// <summary>Acumula, durante a rodada, o que vai para <c>rodadas_agente</c>. Só números e códigos fechados.</summary>
    private sealed class DiagnosticoRodada(Guid? id)
    {
        public Guid? Id { get; } = id;

        public DateTimeOffset IniciadaEm { get; } = DateTimeOffset.UtcNow;

        public System.Diagnostics.Stopwatch Relogio { get; } = System.Diagnostics.Stopwatch.StartNew();

        /// <summary>Nulo = a rodada não chegou a tratar mensagem nenhuma (nada a registrar).</summary>
        public DesfechoRodadaAgente? Desfecho { get; set; }

        public MotivoSilencioRodada? MotivoSilencio { get; set; }

        public TelemetriaRodadaAgente? Telemetria { get; set; }

        public string? PromptVersao { get; set; }
    }

    /// <summary>
    /// Grava a linha da rodada. É a última coisa que a rodada faz, depois de responder ao cliente: qualquer falha aqui (banco,
    /// prazo) é só registrada em log — nunca derruba a rodada nem altera o que o cliente já recebeu.
    /// </summary>
    private async Task GravarRodadaAsync(DiagnosticoRodada diagnostico, Guid negocioId, Guid conversaId)
    {
        if (diagnostico.Id is not { } id || diagnostico.Desfecho is not { } desfecho)
            return;

        try
        {
            var t = diagnostico.Telemetria;
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            // Contexto e escopo PRÓPRIOS: o da rodada pode ter entidades funcionais pendentes (por exemplo, uma gravação cancelada
            // antes do envio) que nunca devem ser persistidas pelo diagnóstico. Aqui só a RodadaAgente existe.
            using var escopo = _escopos.CreateScope();
            escopo.ServiceProvider.GetRequiredService<IContextoNegocio>().Definir(negocioId);
            var banco = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
            banco.RodadasAgente.Add(new RodadaAgente(
                id, negocioId, conversaId, diagnostico.IniciadaEm, (int)diagnostico.Relogio.ElapsedMilliseconds, desfecho,
                diagnostico.MotivoSilencio, t?.Chamadas ?? 0, t?.TokensEntrada ?? 0, t?.TokensSaida ?? 0, t?.Modelo, t?.MotivoFim,
                t?.Ferramentas, t?.ErrosFerramenta, t?.FalhaProvedor, t?.PromptVersao ?? diagnostico.PromptVersao));
            await banco.SaveChangesAsync(prazo.Token);
        }
        catch (Exception excecao)
        {
            _logger.LogWarning("Agente: não foi possível registrar a rodada (conversa {ConversaId}): {Tipo}.", conversaId, excecao.GetType().Name);
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

    private Task<bool> ConversaAindaNossaAsync(Lease lease, CancellationToken cancellationToken)
    {
        var dono = lease.Valor;
        var agora = DateTimeOffset.UtcNow;
        return _dbContext.ConversasWhatsApp.AsNoTracking().AnyAsync(
            c => c.Id == lease.ConversaId && c.ProcessandoAte == dono && c.ProcessandoAte > agora
                && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado), cancellationToken);
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

    private async Task ExecutarRodadaAsync(Guid conversaId, Lease lease, DiagnosticoRodada diagnostico, CancellationToken cancellationToken)
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

        var decisao = await DecidirAsync(conversa, instancia, cancellationToken);
        if (decisao.Tipo == DecisaoRodada.Silencio)
        {
            diagnostico.Desfecho = DesfechoRodadaAgente.Silencio;
            diagnostico.MotivoSilencio = decisao.Motivo;
            if (await RenovarLeaseAsync(lease, cancellationToken))
                await MarcarProcessadaAsync(lease, ultima, cancellationToken);
            return;
        }

        // Só responde o que é recente; o resto (ex.: pendente antes de um deploy) é dado como tratado.
        // Se a mais recente já é velha, todas as pendentes são.
        if (maisRecente.CriadoEm < agora - IdadeMaximaParaResponder)
        {
            diagnostico.Desfecho = DesfechoRodadaAgente.Silencio;
            diagnostico.MotivoSilencio = MotivoSilencioRodada.Antiga;
            if (await RenovarLeaseAsync(lease, cancellationToken))
                await MarcarProcessadaAsync(lease, ultima, cancellationToken);
            return;
        }

        // Serviço de IA não contratado: nada da IA roda (0 chamadas, 0 usos_ia, 0 escritas). Só a mensagem fixa, com limites.
        if (decisao.Tipo == DecisaoRodada.Indisponivel)
        {
            // Exceção D7: uma confirmação JÁ gravada e ainda não anunciada (envio que falhou, processo que caiu) continua sendo recuperada
            // mesmo com o serviço desligado — é um fato. Só esses dois tipos de plano; nova confirmação, recusa e aviso de prazo continuam barrados.
            var recuperacao = await _confirmador.PlanejarAsync(conversa, ultima, agora, cancellationToken);
            if (recuperacao is { Tipo: TipoPlanoConfirmacao.AnunciarConfirmada or TipoPlanoConfirmacao.JaAnunciada })
            {
                diagnostico.Desfecho = await TratarRespostaAoResumoAsync(lease, conversa, instancia, recuperacao, ultima, cancellationToken);
                return;
            }

            diagnostico.Desfecho = await TratarIndisponivelAsync(lease, conversa, instancia, diagnostico, ultima, agora, cancellationToken);
            return;
        }

        // Resposta ao resumo de um agendamento ("sim"/"não"): decidida pelo servidor, sem chamar a IA e antes dos limites de custo.
        if (decisao.Situacao.AgendamentoLiberado)
        {
            var plano = await _confirmador.PlanejarAsync(conversa, ultima, agora, cancellationToken);
            if (plano is not null)
            {
                diagnostico.Desfecho = await TratarRespostaAoResumoAsync(lease, conversa, instancia, plano, ultima, cancellationToken);
                return;
            }
        }

        if (_opcoes.Provider == ProvedorIa.Gemini)
        {
            var motivo = await _limitador.VerificarAsync(conversaId, agora, cancellationToken);
            if (motivo != MotivoLimiteAgente.Nenhum)
            {
                _logger.LogWarning("Agente: limite {Motivo} atingido (conversa {ConversaId}, negócio {NegocioId}).",
                    motivo, conversaId, conversa.NegocioId);
                diagnostico.Desfecho = motivo is MotivoLimiteAgente.TelefonePorHora or MotivoLimiteAgente.TelefonePorDia
                    ? DesfechoRodadaAgente.LimiteTelefone
                    : DesfechoRodadaAgente.LimiteCusto;
                if (!await RenovarLeaseAsync(lease, cancellationToken))
                {
                    diagnostico.Desfecho = DesfechoRodadaAgente.LeasePerdido;
                    return;
                }

                // Marca (com a posse conferida) ANTES de avisar: o aviso nunca é reenviado em loop e quem perdeu o lease não o envia.
                if (!await MarcarProcessadaAsync(lease, ultima, cancellationToken))
                {
                    diagnostico.Desfecho = DesfechoRodadaAgente.LeasePerdido;
                    return;
                }

                await AvisarLimiteAsync(lease, conversa, instancia, motivo, agora, cancellationToken);
                return;
            }
        }

        // Prazo duro só para gerar a resposta; a renovação logo abaixo dá ao envio o seu próprio fôlego.
        var resposta = new RespostaAgente(null);
        var falhouAoResponder = false;
        diagnostico.PromptVersao = _opcoes.Provider == ProvedorIa.Eco ? "eco" : null;
        using (var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            prazo.CancelAfter(PrazoResposta);
            try
            {
                // Mesmo lote para histórico e para o "processado": a IA só vê mensagens até `ultima`.
                resposta = await _resposta.ResponderAsync(
                    new ContextoRespostaAgente(conversa.NegocioId, instancia.Id, conversa.Id, conversa.TelefoneCliente,
                        maisRecente.Conteudo, ultima, diagnostico.Id),
                    prazo.Token);
            }
            catch (Exception excecao) when (!(excecao is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                _dbContext.ChangeTracker.Clear();
                falhouAoResponder = true;
                _logger.LogWarning("Agente: falha ao responder (conversa {ConversaId}, instância {InstanciaId}): {Tipo}.",
                    conversaId, instancia.Id, excecao.GetType().Name);
            }
        }

        diagnostico.Telemetria = resposta.Telemetria;
        diagnostico.Desfecho = falhouAoResponder ? DesfechoRodadaAgente.Erro : DesfechoRodadaAgente.Silencio;

        // Quem perdeu o lease durante a IA não envia nem marca: o dono atual cuida da conversa.
        if (!await RenovarLeaseAsync(lease, cancellationToken))
        {
            diagnostico.Desfecho = DesfechoRodadaAgente.LeasePerdido;
            return;
        }

        // Serviço desligado enquanto a IA pensava: relê o banco (sem memória) e descarta o que o modelo produziu — não envia nada.
        // Uma confirmação JÁ gravada antes do desligamento continua sendo anunciada (é fato; está no ramo do confirmador).
        if (!await _politica.ContratadoAgoraAsync(conversa.NegocioId, cancellationToken))
        {
            diagnostico.Desfecho = DesfechoRodadaAgente.Silencio;
            diagnostico.MotivoSilencio = MotivoSilencioRodada.NaoContratado;
            _logger.LogInformation("Agente: serviço de IA desligado durante a rodada; resposta descartada (conversa {ConversaId}, negócio {NegocioId}).",
                conversa.Id, conversa.NegocioId);
            if (!await MarcarProcessadaAsync(lease, ultima, cancellationToken))
                diagnostico.Desfecho = DesfechoRodadaAgente.LeasePerdido;
            return;
        }

        // Marca antes de enviar: se o envio falhar, não reenvia em loop (a mensagem do cliente já está salva).
        if (!await MarcarProcessadaAsync(lease, ultima, cancellationToken))
        {
            diagnostico.Desfecho = DesfechoRodadaAgente.LeasePerdido;
            return;
        }

        // Preparou um agendamento nesta rodada: o texto do modelo é descartado e o servidor envia o resumo.
        if (resposta.PendenciaPreparadaId is { } pendenciaId)
        {
            var estadoResumo = await EnviarResumoAsync(lease, conversa, instancia, pendenciaId, cancellationToken);
            diagnostico.Desfecho = estadoResumo switch
            {
                null => DesfechoRodadaAgente.Silencio,
                EstadoEnvio.Enviado => DesfechoRodadaAgente.ResumoEnviado,
                EstadoEnvio.Ambiguo => DesfechoRodadaAgente.EnvioAmbiguo,
                _ => DesfechoRodadaAgente.EnvioFalhou,
            };
        }
        else if (!string.IsNullOrWhiteSpace(resposta.Texto))
        {
            // O aviso de atendimento automatizado vai colado no início da primeira resposta (e de novo a cada N dias).
            var aviso = await AvisoAutomatizadoPendenteAsync(conversa, instancia, cancellationToken);
            var texto = aviso is null ? resposta.Texto : aviso + "\n\n" + resposta.Texto;
            var envio = await EnviarEGravarAsync(lease, conversa, instancia, texto, PapelMensagemWhatsApp.Assistente, null, cancellationToken);
            if (aviso is not null && envio.Estado == EstadoEnvio.Enviado)
                await RegistrarAvisoAutomatizadoAsync(conversa.Id);

            diagnostico.Desfecho = envio.Estado switch
            {
                EstadoEnvio.Enviado => resposta.Telemetria?.Fallback == true ? DesfechoRodadaAgente.Fallback : DesfechoRodadaAgente.Respondida,
                EstadoEnvio.Ambiguo => DesfechoRodadaAgente.EnvioAmbiguo,
                _ => DesfechoRodadaAgente.EnvioFalhou,
            };
        }
    }

    // ---------------------------------------------------------------- confirmação pelo servidor (Fase 3)

    /// <summary>
    /// Executa o plano do confirmador com o lease conferido ANTES de escrever e DEPOIS, quando se marca o lote como tratado. A
    /// confirmação em si é idempotente (id aleatório da operação); se o lease se perder entre o commit e o envio, a rodada seguinte
    /// acha a pendência <c>Confirmada</c> sem a mensagem <c>agendamento_confirmado</c> e só anuncia.
    /// </summary>
    private async Task<DesfechoRodadaAgente> TratarRespostaAoResumoAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, PlanoConfirmacao plano, DateTimeOffset ultima, CancellationToken cancellationToken)
    {
        if (!await RenovarLeaseAsync(lease, cancellationToken))
            return DesfechoRodadaAgente.LeasePerdido;

        var mensagem = await _confirmador.ExecutarAsync(plano, conversa, instancia, cancellationToken);

        if (!await RenovarLeaseAsync(lease, cancellationToken))
            return DesfechoRodadaAgente.LeasePerdido;

        if (mensagem is null)
        {
            await MarcarProcessadaAsync(lease, ultima, cancellationToken);
            return DesfechoRodadaAgente.Confirmacao;
        }

        // A confirmação do agendamento é a única mensagem cuja entrega não pode se perder: o lote só é marcado como tratado DEPOIS
        // do envio. Queda no meio => o lote continua pendente e a recuperação acha a pendência Confirmada sem a mensagem anunciada.
        // Ambíguo (timeout) mantém a reserva gravada (supressão: nunca duplica) e marca o lote; falha definitiva comprovada apaga a
        // reserva e deixa o lote pendente para a recuperação reenviar. Os demais textos fixos mantêm "marca antes de enviar".
        var confirmacao = mensagem.Marcador == MarcadoresAgente.Confirmado;
        if (!confirmacao && !await MarcarProcessadaAsync(lease, ultima, cancellationToken))
            return DesfechoRodadaAgente.LeasePerdido;

        var envio = await EnviarEGravarAsync(lease, conversa, instancia, mensagem.Texto, PapelMensagemWhatsApp.Sistema, mensagem.Marcador, cancellationToken, mensagem.TextoGravado);
        var desfecho = envio.Estado switch
        {
            EstadoEnvio.Enviado => DesfechoRodadaAgente.Confirmacao,
            EstadoEnvio.Ambiguo => DesfechoRodadaAgente.EnvioAmbiguo,
            _ => DesfechoRodadaAgente.EnvioFalhou,
        };

        if (!confirmacao)
            return desfecho;

        if (envio.Estado is EstadoEnvio.FalhaDefinitiva or EstadoEnvio.SemPrazo)
        {
            // Comprovadamente não enviada: a reserva não pode ficar parecendo "confirmação anunciada".
            if (envio.MensagemId is { } mensagemId)
                await _dbContext.MensagensWhatsApp.Where(m => m.Id == mensagemId).ExecuteDeleteAsync(CancellationToken.None);
            _logger.LogWarning("Agente: confirmação não enviada, ficará para a recuperação (conversa {ConversaId}).", conversa.Id);
            return desfecho;
        }

        await MarcarProcessadaAsync(lease, ultima, cancellationToken);
        return desfecho;
    }

    // ---------------------------------------------------------------- aviso de atendimento automatizado (Fase 3)

    /// <summary>
    /// Texto fixo (uma vez por conversa a cada <c>Ai__AvisoAutomatizadoDias</c>) dizendo que o atendimento é automatizado e por IA.
    /// Só com o agendamento ligado para o negócio; fora disso o comportamento é o da Fase 2.
    /// </summary>
    private async Task<string?> AvisoAutomatizadoPendenteAsync(ConversaWhatsApp conversa, InstanciaWhatsApp instancia, CancellationToken cancellationToken)
    {
        if (!(await _politica.ObterAsync(conversa.NegocioId, cancellationToken)).AgendamentoLiberado)
            return null;
        if (conversa.AvisoAutomatizadoEm is { } ultimo && ultimo > DateTimeOffset.UtcNow.AddDays(-_opcoes.AvisoAutomatizadoDias))
            return null;

        var negocio = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == conversa.NegocioId)
            .Select(n => new { n.NomeExibido, Slug = n.Slug.Valor }).FirstOrDefaultAsync(cancellationToken);
        if (negocio is null)
            return null;

        return new TextosAgendamentoAgente(ConstrutorUrlPublica.Construir(_marca, negocio.Slug, string.Empty), instancia.Telefone)
            .AvisoAutomatizado(negocio.NomeExibido);
    }

    private Task RegistrarAvisoAutomatizadoAsync(Guid conversaId) =>
        _dbContext.ConversasWhatsApp.Where(c => c.Id == conversaId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.AvisoAutomatizadoEm, (DateTimeOffset?)DateTimeOffset.UtcNow), CancellationToken.None);

    // ---------------------------------------------------------------- resumo do agendamento (Fase 3)

    /// <summary>
    /// Envia o resumo montado pelo SERVIDOR (nunca pelo modelo) da pendência recém-criada. A mensagem é gravada como <c>Sistema</c>
    /// (marcador <c>agendamento_resumo</c>) ANTES do envio, junto com o <c>ResumoEnviadoEm</c> da pendência: o "sim" só vale para
    /// mensagens recebidas depois dele. Falha definitiva da Evolution (ou sem prazo de lease) libera o horário; timeout, que é
    /// resultado ambíguo, mantém a pendência.
    /// </summary>
    private async Task<EstadoEnvio?> EnviarResumoAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, Guid pendenciaId, CancellationToken cancellationToken)
    {
        var pendencia = await _dbContext.PendenciasAgendamentoWhatsApp.FirstOrDefaultAsync(
            p => p.Id == pendenciaId && p.ConversaId == conversa.Id, cancellationToken);
        if (pendencia is null || !pendencia.Aguardando)
            return null;

        var texto = await MontarResumoAsync(pendencia, instancia, cancellationToken);
        if (texto is null)
        {
            await _pendencias.EncerrarAsync(pendenciaId, DesfechoPendenciaAgente.Falhou, cancellationToken);
            return null;
        }

        var aviso = await AvisoAutomatizadoPendenteAsync(conversa, instancia, cancellationToken);
        if (aviso is not null)
            texto = aviso + "\n\n" + texto;

        var envio = await EnviarEGravarAsync(
            lease, conversa, instancia, texto, PapelMensagemWhatsApp.Sistema, MarcadoresAgente.Resumo, cancellationToken,
            aoReservar: reserva => pendencia.RegistrarResumoEnviado(reserva.Id, DateTimeOffset.UtcNow));

        if (aviso is not null && envio.Estado == EstadoEnvio.Enviado)
            await RegistrarAvisoAutomatizadoAsync(conversa.Id);

        if (envio.Estado is EstadoEnvio.FalhaDefinitiva or EstadoEnvio.SemPrazo)
        {
            // O cliente não recebeu o resumo: a mensagem reservada sai do histórico e a reserva do horário é liberada.
            if (envio.MensagemId is { } mensagemId)
                await _dbContext.MensagensWhatsApp.Where(m => m.Id == mensagemId).ExecuteDeleteAsync(CancellationToken.None);
            await _pendencias.EncerrarAsync(pendenciaId, DesfechoPendenciaAgente.Falhou, CancellationToken.None);
        }

        return envio.Estado;
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

    private enum DecisaoRodada
    {
        /// <summary>Não responde (e dá o lote como tratado).</summary>
        Silencio,

        /// <summary>Serviço de IA não contratado com o agente ligado na tela: só a mensagem fixa, com limites.</summary>
        Indisponivel,

        /// <summary>Provedor Eco (Fase 1): repete o que o cliente disse. Só para negócio que contratou.</summary>
        Eco,

        /// <summary>Confirmador de pendência, limites de custo e IA.</summary>
        Agente,
    }

    private readonly record struct Decisao(DecisaoRodada Tipo, SituacaoAgenteIa Situacao, MotivoSilencioRodada? Motivo = null);

    /// <summary>
    /// O gate principal do serviço premium, nesta ordem: provedor desligado; conversa com humano ou agente desligado na tela;
    /// assinatura que não permite operar (vale para o Eco e para a mensagem fixa também); serviço não contratado (mensagem fixa);
    /// Eco; fora da lista global (piloto); e por fim o agente.
    /// </summary>
    private async Task<Decisao> DecidirAsync(ConversaWhatsApp conversa, InstanciaWhatsApp instancia, CancellationToken cancellationToken)
    {
        var situacao = await _politica.ObterAsync(conversa.NegocioId, cancellationToken);

        if (_opcoes.Provider == ProvedorIa.Desligado)
            return new(DecisaoRodada.Silencio, situacao, MotivoSilencioRodada.AgenteDesligado);

        if (!conversa.AgenteResponde)
            return new(DecisaoRodada.Silencio, situacao, MotivoSilencioRodada.Humano);

        if (!instancia.AgenteAtivo)
            return new(DecisaoRodada.Silencio, situacao, MotivoSilencioRodada.AgenteDesligado);

        // Assinatura suspensa: silêncio, sem IA (custo) e sem a mensagem fixa (não se fala de pagamento). Sem assinatura cadastrada não bloqueia.
        var assinatura = await _assinatura.ObterAsync(conversa.NegocioId, cancellationToken);
        if (assinatura is not null && !assinatura.PermiteOperar)
            return new(DecisaoRodada.Silencio, situacao, MotivoSilencioRodada.Assinatura);

        if (!situacao.Contratado)
            return new(DecisaoRodada.Indisponivel, situacao);

        if (_opcoes.Provider == ProvedorIa.Eco)
            return new(DecisaoRodada.Eco, situacao);

        // A lista global só restringe: contratado e fora dela = silêncio.
        if (!situacao.IaLiberada)
            return new(DecisaoRodada.Silencio, situacao, MotivoSilencioRodada.ForaDoPiloto);

        return new(DecisaoRodada.Agente, situacao);
    }

    // ---------------------------------------------------------------- serviço não contratado (mensagem fixa)

    /// <summary>
    /// Sem o serviço de IA: no máximo uma mensagem fixa por conversa a cada <c>Ai__Indisponivel__IntervaloHoras</c> e
    /// <c>Ai__Indisponivel__LimitePorNegocioPorDia</c> por negócio/dia UTC; acima disso, silêncio. Não grava <c>UsoIa</c>, não conta nos
    /// limites de IA e não leva o aviso de atendimento automatizado. O lote é marcado ANTES do envio (nunca reenvia em laço) e a
    /// mensagem é reservada no banco antes da chamada externa (é ela que a contagem enxerga).
    /// </summary>
    private async Task<DesfechoRodadaAgente> TratarIndisponivelAsync(
        Lease lease, ConversaWhatsApp conversa, InstanciaWhatsApp instancia, DiagnosticoRodada diagnostico, DateTimeOffset ultima,
        DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (!await RenovarLeaseAsync(lease, cancellationToken) || !await MarcarProcessadaAsync(lease, ultima, cancellationToken))
            return DesfechoRodadaAgente.LeasePerdido;

        var negocio = await _dbContext.Negocios.AsNoTracking().Where(n => n.Id == conversa.NegocioId)
            .Select(n => new { n.NomeExibido, Slug = n.Slug.Valor }).FirstOrDefaultAsync(cancellationToken);
        if (negocio is null)
            return DesfechoRodadaAgente.Silencio;

        var link = ConstrutorUrlPublica.Construir(_marca, negocio.Slug, string.Empty);
        // O nome do negócio é cadastro livre: o texto fixo passa pelo mesmo sanitizador da resposta do modelo.
        var texto = SanitizadorRespostaAgente.SanitizarTextoFixo(
            TextosAgendamentoAgente.AtendimentoIndisponivel(negocio.NomeExibido, link), link, instancia.Telefone);

        // Contagem e reserva na MESMA transação, sob trava por negócio/dia: conversas concorrentes do mesmo negócio nunca passam do teto.
        // A chamada externa vem DEPOIS do commit.
        var reserva = await ReservarAvisoIndisponivelAsync(conversa, instancia, texto, agora, cancellationToken);
        if (reserva is null)
        {
            diagnostico.MotivoSilencio = MotivoSilencioRodada.NaoContratado;
            return DesfechoRodadaAgente.Silencio;
        }

        var envio = await EnviarEGravarAsync(
            lease, conversa, instancia, texto, PapelMensagemWhatsApp.Sistema, MarcadoresAgente.IndisponivelAviso, cancellationToken,
            reservaPronta: reserva);

        // Comprovadamente não enviada: a reserva sai (não conta nos limites) e a próxima mensagem do cliente pode tentar de novo.
        if (envio.Estado is EstadoEnvio.FalhaDefinitiva or EstadoEnvio.SemPrazo && envio.MensagemId is { } mensagemId)
            await _dbContext.MensagensWhatsApp.Where(m => m.Id == mensagemId).ExecuteDeleteAsync(CancellationToken.None);

        return envio.Estado switch
        {
            EstadoEnvio.Enviado => DesfechoRodadaAgente.Indisponivel,
            EstadoEnvio.Ambiguo => DesfechoRodadaAgente.EnvioAmbiguo,
            _ => DesfechoRodadaAgente.EnvioFalhou,
        };
    }

    /// <summary>
    /// Decide (intervalo por conversa e teto diário por negócio) e grava a reserva da mensagem numa transação com
    /// <c>pg_advisory_xact_lock</c> por negócio/dia UTC (lock isolado: nada mais é segurado enquanto ele vale, então não há ciclo com a ordem
    /// pendência → agendamento → cliente). Nulo = acima de algum limite (silêncio).
    /// </summary>
    private async Task<MensagemWhatsApp?> ReservarAvisoIndisponivelAsync(
        ConversaWhatsApp conversa, InstanciaWhatsApp instancia, string texto, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var chave = $"indisponivel:{conversa.NegocioId:N}:{agora.UtcDateTime:yyyyMMdd}";
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({chave}, 0))", cancellationToken);

            if (!await PodeAvisarIndisponivelAsync(conversa, agora, cancellationToken))
            {
                await transacao.RollbackAsync(cancellationToken);
                return null;
            }

            var reserva = new MensagemWhatsApp(
                instancia.NegocioId, instancia.Id, conversa.Id, null, PapelMensagemWhatsApp.Sistema, texto, MarcadoresAgente.IndisponivelAviso);
            _dbContext.MensagensWhatsApp.Add(reserva);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return reserva;
        });
    }

    private async Task<bool> PodeAvisarIndisponivelAsync(ConversaWhatsApp conversa, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var desde = agora.AddHours(-_opcoes.Indisponivel.IntervaloHoras);
        if (await _dbContext.MensagensWhatsApp.AsNoTracking().AnyAsync(
                m => m.ConversaId == conversa.Id && m.Papel == PapelMensagemWhatsApp.Sistema
                    && m.NomeFerramenta == MarcadoresAgente.IndisponivelAviso && m.CriadoEm >= desde, cancellationToken))
            return false;

        var inicioDoDia = new DateTimeOffset(agora.UtcDateTime.Date, TimeSpan.Zero);
        var hoje = await _dbContext.MensagensWhatsApp.AsNoTracking().CountAsync(
            m => m.NegocioId == conversa.NegocioId && m.Papel == PapelMensagemWhatsApp.Sistema
                && m.NomeFerramenta == MarcadoresAgente.IndisponivelAviso && m.CriadoEm >= inicioDoDia, cancellationToken);
        return hoje < _opcoes.Indisponivel.LimitePorNegocioPorDia;
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
        CancellationToken cancellationToken, string? textoGravado = null, Action<MensagemWhatsApp>? aoReservar = null,
        MensagemWhatsApp? reservaPronta = null)
    {
        var restante = lease.Valor - DateTimeOffset.UtcNow - MargemEnvio;
        if (restante <= TimeSpan.Zero)
        {
            _logger.LogWarning("Agente: sem prazo de lease para enviar (conversa {ConversaId}).", conversa.Id);
            return new ResultadoEnvio(EstadoEnvio.SemPrazo);
        }

        // Última conferência ANTES de ir ao provedor: a anonimização (LGPD) zera o lease e troca o telefone, então conversa anonimizada
        // ou lease que não é mais nosso = não envia. Não fecha a janela entre esta leitura e a chamada (ver ConversaWhatsApp.Anonimizar).
        if (!await ConversaAindaNossaAsync(lease, cancellationToken))
        {
            _logger.LogWarning("Agente: conversa anonimizada ou lease perdido antes do envio (conversa {ConversaId}); nada é enviado.", conversa.Id);
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
            // reservaPronta: já gravada (e commitada) por quem precisou serializar a decisão antes — não grava de novo.
            var reserva = reservaPronta ?? new MensagemWhatsApp(instancia.NegocioId, instancia.Id, conversa.Id, null, papel, textoGravado ?? texto, marcador);
            mensagemId = reserva.Id;
            if (reservaPronta is null)
            {
                _dbContext.MensagensWhatsApp.Add(reserva);
                aoReservar?.Invoke(reserva);
                await _dbContext.SaveChangesAsync(prazo.Token);
            }

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
