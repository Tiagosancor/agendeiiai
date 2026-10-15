using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Notificacoes.Outbox;

/// <summary>Item reivindicado por um despachante: o token é o fence de toda gravação seguinte.</summary>
public sealed record ItemReivindicado(Guid Id, Guid NegocioId, Guid Token);

/// <summary>
/// Despacho dos itens reivindicados de UM negócio, num escopo de DI próprio (o <see cref="IContextoNegocio"/> só aceita um tenant por
/// escopo — mesmo padrão dos jobs de lembrete): o negócio é definido uma vez e o filtro global do <c>DbContext</c> vale normalmente.
/// <para>
/// Por item: relê o item (a reivindicação ainda é minha?), confere o prazo, monta destinatário e conteúdo com o ESTADO ATUAL
/// (agendamento cancelado ou remarcado, cliente que saiu, canal desligado → cancelada, sem envio), grava o ponto sem retorno
/// ("vou chamar o provedor"), chama o provedor FORA de qualquer transação e grava o resultado, sempre conferindo a reivindicação.
/// </para>
/// </summary>
public sealed class ProcessadorNotificacoesDoNegocio
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly DespachanteDeCanais _canais;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly IConsultaSituacaoAssinatura _assinaturas;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly OpcoesNotificacoes _opcoes;
    private readonly PoliticaDeTentativas _politica;
    private readonly TimeProvider _relogio;
    private readonly ILogger<ProcessadorNotificacoesDoNegocio> _logger;

    public ProcessadorNotificacoesDoNegocio(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, DespachanteDeCanais canais, IServicoTokenPublico servicoToken,
        IConsultaSituacaoAssinatura assinaturas, IOptions<OpcoesMarca> opcoesMarca, IOptions<OpcoesNotificacoes> opcoes,
        TimeProvider relogio, ILogger<ProcessadorNotificacoesDoNegocio> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _canais = canais;
        _servicoToken = servicoToken;
        _assinaturas = assinaturas;
        _opcoesMarca = opcoesMarca.Value;
        _opcoes = opcoes.Value;
        _politica = new PoliticaDeTentativas(_opcoes);
        _relogio = relogio;
        _logger = logger;
    }

    public async Task ProcessarAsync(Guid negocioId, IReadOnlyList<ItemReivindicado> itens, CancellationToken cancellationToken)
    {
        _contextoNegocio.Definir(negocioId);

        foreach (var reivindicado in itens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _dbContext.ChangeTracker.Clear();
            await ProcessarItemAsync(reivindicado, cancellationToken);
        }
    }

    private async Task ProcessarItemAsync(ItemReivindicado reivindicado, CancellationToken cancellationToken)
    {
        var item = await _dbContext.NotificacoesPendentes.AsNoTracking().FirstOrDefaultAsync(
            n => n.Id == reivindicado.Id && n.ReivindicacaoId == reivindicado.Token && n.Status == StatusNotificacao.Enviando, cancellationToken);

        // A reivindicação não é mais minha (lease vencido e recuperado por outra instância): quem tem o item cuida dele.
        if (item is null)
            return;

        var tentativasFeitas = item.Tentativas;
        var marcado = false;

        try
        {
            var agora = _relogio.GetUtcNow();

            if (agora >= item.ExpiraEm)
            {
                await GravarAsync(item, reivindicado.Token, PoliticaDeTentativas.Vencida(item, tentativasFeitas, agora), null, null, cancellationToken);
                return;
            }

            var montagem = await MontarAsync(item, agora, cancellationToken);
            if (montagem.CodigoDeCancelamento is { } codigo)
            {
                await GravarAsync(item, reivindicado.Token, PoliticaDeTentativas.Cancelada(item, agora, codigo), null, null, cancellationToken);
                return;
            }

            // Ponto sem retorno. Daqui em diante, uma queda deixa um item que PODE ter sido aceito pelo provedor.
            tentativasFeitas = item.Tentativas + 1;
            if (!await MarcarInicioAsync(item, reivindicado.Token, tentativasFeitas, agora))
                return;
            marcado = true;

            var resultado = montagem.Telefone is not null
                ? await _canais.EnviarWhatsAppAsync(montagem.Telefone, montagem.Texto!, cancellationToken)
                : await _canais.EnviarEmailAsync(montagem.Destinatario!, montagem.Email!, cancellationToken);

            var transicao = _politica.AposResultado(item, tentativasFeitas, resultado, _relogio.GetUtcNow());

            // Mesmo com o cancelamento pedido, o resultado de uma mensagem que já saiu precisa ficar gravado.
            await GravarAsync(item, reivindicado.Token, transicao, montagem, resultado, CancellationToken.None);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            // Só ids e o tipo do erro: a mensagem de uma exceção do banco pode carregar valores de colunas.
            _logger.LogError("Falha ao despachar a notificação {NotificacaoId} do negócio {NegocioId} ({TipoErro}).",
                item.Id, item.NegocioId, excecao.GetType().Name);

            // Antes do ponto sem retorno nada foi enviado: conta a tentativa e agenda outra (ou encerra). Depois dele, o lease vence e o
            // item é recuperado pela regra de "pode ter sido aceito".
            if (!marcado)
            {
                try
                {
                    await GravarAsync(item, reivindicado.Token,
                        _politica.AposFalhaRetentavel(item, item.Tentativas + 1, _relogio.GetUtcNow(), CodigosOutbox.ErroInterno), null, null, CancellationToken.None);
                }
                catch (Exception falhaAoGravar) when (falhaAoGravar is not OperationCanceledException)
                {
                    // O lease vencido cuida do resto.
                }
            }
        }
    }

    // ------------------------------------------------------------------ montagem

    private sealed record Montagem(
        string? CodigoDeCancelamento = null, string? Destinatario = null, EmailMontado? Email = null, TelefoneE164? Telefone = null,
        string? Texto = null, Agendamento? Agendamento = null, EventoAgendamentoProfissional? Evento = null)
    {
        public static Montagem Cancelar(string codigo) => new(codigo);
    }

    private static readonly StatusAgendamento[] StatusQueMantemAConfirmacao =
        [StatusAgendamento.Agendado, StatusAgendamento.EmAtendimento, StatusAgendamento.Concluido];

    private Task<Montagem> MontarAsync(NotificacaoPendente item, DateTimeOffset agora, CancellationToken cancellationToken) =>
        item.Tipo switch
        {
            TipoNotificacao.LembreteRetorno => MontarRetornoAsync(item, agora, cancellationToken),
            TipoNotificacao.ProfissionalNovo or TipoNotificacao.ProfissionalRemarcado or TipoNotificacao.ProfissionalCancelado =>
                MontarAvisoAoProfissionalAsync(item, cancellationToken),
            _ => MontarMensagemAoClienteAsync(item, agora, cancellationToken),
        };

    private async Task<Montagem> MontarMensagemAoClienteAsync(NotificacaoPendente item, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var agendamento = await CarregarAgendamentoAsync(item, cancellationToken);
        if (agendamento is null)
            return Montagem.Cancelar(CodigosOutbox.ReferenciaInexistente);

        // Estado atual do agendamento: nunca envia o que sabidamente ficou obsoleto.
        var valida = item.Tipo switch
        {
            TipoNotificacao.Confirmacao => StatusQueMantemAConfirmacao.Contains(agendamento.Status),
            TipoNotificacao.Cancelamento => agendamento.Status == StatusAgendamento.Cancelado,
            TipoNotificacao.Lembrete60 => agendamento.Status == StatusAgendamento.Agendado && agendamento.Inicio > agora,
            _ => false,
        };
        if (!valida)
            return Montagem.Cancelar(CodigosOutbox.AgendamentoObsoleto);

        // O lembrete é do horário em que foi criado; remarcado, a mensagem antiga não vale (a nova nasce com a nova versão).
        if (item.Tipo == TipoNotificacao.Lembrete60 && item.VersaoHorario != TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio))
            return Montagem.Cancelar(CodigosOutbox.HorarioAlterado);

        if (item.Tipo == TipoNotificacao.Lembrete60 && !agendamento.PodeReceberMensagens)
            return Montagem.Cancelar(CodigosOutbox.OptOut);

        if (agendamento.ClienteId is not { } clienteId)
            return Montagem.Cancelar(CodigosOutbox.SemDestinatario);

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);
        if (cliente is null || cliente.Excluido)
            return Montagem.Cancelar(CodigosOutbox.ClienteInelegivel);

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == item.NegocioId, cancellationToken);

        // Cancelamento fala da página do negócio; confirmação e lembrete levam o link de gestão do agendamento (token gerado agora).
        var link = item.Tipo == TipoNotificacao.Cancelamento
            ? ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, "/")
            : ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, $"/agendamentos/{_servicoToken.GerarTokenAgendamento(negocio.Id, agendamento.Id)}");

        var dados = new DadosNotificacaoAgendamento(
            cliente.Nome, cliente.Email, cliente.Telefone, agendamento.Inicio, agendamento.Fim,
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, link, link);

        if (item.Canal == CanalNotificacao.Email)
        {
            if (string.IsNullOrWhiteSpace(cliente.Email))
                return Montagem.Cancelar(CodigosOutbox.SemDestinatario);

            var email = item.Tipo switch
            {
                TipoNotificacao.Confirmacao => MensagensDeAgendamento.EmailConfirmacao(_opcoesMarca.NomeProduto, negocio, dados),
                TipoNotificacao.Lembrete60 => MensagensDeAgendamento.EmailLembrete(_opcoesMarca.NomeProduto, negocio, dados),
                _ => MensagensDeAgendamento.EmailCancelamento(_opcoesMarca.NomeProduto, negocio, dados),
            };
            return new Montagem(Destinatario: cliente.Email, Email: email);
        }

        if (!negocio.WhatsAppAtivoParaConfirmacoes)
            return Montagem.Cancelar(CodigosOutbox.CanalDesativado);
        if (cliente.Telefone is null)
            return Montagem.Cancelar(CodigosOutbox.SemDestinatario);

        var texto = item.Tipo switch
        {
            TipoNotificacao.Confirmacao => MensagensDeAgendamento.WhatsAppConfirmacao(negocio, dados),
            TipoNotificacao.Lembrete60 => MensagensDeAgendamento.WhatsAppLembrete(negocio, dados),
            _ => MensagensDeAgendamento.WhatsAppCancelamento(negocio, dados),
        };
        return new Montagem(Telefone: cliente.Telefone, Texto: texto);
    }

    private async Task<Montagem> MontarAvisoAoProfissionalAsync(NotificacaoPendente item, CancellationToken cancellationToken)
    {
        var agendamento = await CarregarAgendamentoAsync(item, cancellationToken);
        if (agendamento is null)
            return Montagem.Cancelar(CodigosOutbox.ReferenciaInexistente);

        var evento = item.Tipo switch
        {
            TipoNotificacao.ProfissionalNovo => EventoAgendamentoProfissional.Novo,
            TipoNotificacao.ProfissionalRemarcado => EventoAgendamentoProfissional.Remarcado,
            _ => EventoAgendamentoProfissional.Cancelado,
        };

        var valida = item.Tipo == TipoNotificacao.ProfissionalCancelado
            ? agendamento.Status == StatusAgendamento.Cancelado
            : StatusQueMantemAConfirmacao.Contains(agendamento.Status) && agendamento.ProfissionalId == item.ProfissionalId;
        if (!valida)
            return Montagem.Cancelar(CodigosOutbox.AgendamentoObsoleto);

        // Remarcado de um horário que já foi remarcado de novo: a mensagem do horário mais novo é a que vale.
        if (item.Tipo == TipoNotificacao.ProfissionalRemarcado && item.VersaoHorario != TiposDeNotificacao.VersaoDoHorario(agendamento.Inicio))
            return Montagem.Cancelar(CodigosOutbox.HorarioAlterado);

        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == item.ProfissionalId, cancellationToken);
        if (profissional is null)
            return Montagem.Cancelar(CodigosOutbox.ReferenciaInexistente);

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == item.NegocioId, cancellationToken);

        var nomeCliente = "—";
        if (agendamento.ClienteId is { } clienteId)
        {
            var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);
            nomeCliente = cliente?.Nome ?? agendamento.NomeInformado ?? "—";
        }

        var dados = new DadosNotificacaoProfissional(
            evento, profissional.Email, profissional.Telefone, nomeCliente, agendamento.Inicio, agendamento.Fim,
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Observacoes, agendamento.Forcado);

        if (item.Canal == CanalNotificacao.Email)
        {
            return string.IsNullOrWhiteSpace(profissional.Email)
                ? Montagem.Cancelar(CodigosOutbox.SemDestinatario)
                : new Montagem(
                    Destinatario: profissional.Email, Email: MensagensDeAgendamento.EmailAvisoProfissional(_opcoesMarca.NomeProduto, negocio, dados),
                    Agendamento: agendamento, Evento: evento);
        }

        if (!negocio.WhatsAppAvisoProfissional)
            return Montagem.Cancelar(CodigosOutbox.CanalDesativado);
        if (!TelefoneE164.TentarCriar(profissional.Telefone ?? "", out var telefone))
            return Montagem.Cancelar(CodigosOutbox.SemDestinatario);

        return new Montagem(
            Telefone: telefone, Texto: MensagensDeAgendamento.WhatsAppAvisoProfissional(negocio, dados), Agendamento: agendamento, Evento: evento);
    }

    /// <summary>
    /// Lembrete de retorno: tudo o que o job conferia ao decidir é conferido DE NOVO agora — o cliente pode ter saído do lembrete, sido
    /// anonimizado, marcado um horário ou o negócio ter desligado o recurso entre a decisão e o envio.
    /// </summary>
    private async Task<Montagem> MontarRetornoAsync(NotificacaoPendente item, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstOrDefaultAsync(n => n.Id == item.NegocioId, cancellationToken);
        if (negocio is not { Ativo: true })
            return Montagem.Cancelar(CodigosOutbox.NegocioInativo);
        if (!negocio.LembreteRetornoAtivo)
            return Montagem.Cancelar(CodigosOutbox.CanalDesativado);

        if (await _assinaturas.ObterAsync(item.NegocioId, cancellationToken) is { PermiteOperar: false })
            return Montagem.Cancelar(CodigosOutbox.NegocioInativo);

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == item.ClienteId, cancellationToken);
        if (cliente is null || cliente.Excluido)
            return Montagem.Cancelar(CodigosOutbox.ClienteInelegivel);
        if (!cliente.AceitaLembreteRetorno)
            return Montagem.Cancelar(CodigosOutbox.OptOut);
        if (cliente.Telefone is null)
            return Montagem.Cancelar(CodigosOutbox.SemDestinatario);

        var temFuturo = await _dbContext.Agendamentos.AnyAsync(f =>
            f.ClienteId == cliente.Id && f.Inicio > agora
            && (f.Status == StatusAgendamento.Agendado || f.Status == StatusAgendamento.EmAtendimento
                || (f.Status == StatusAgendamento.Reservado && f.ReservadoAte > agora)), cancellationToken);
        if (temFuturo)
            return Montagem.Cancelar(CodigosOutbox.ClienteInelegivel);

        var link = ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, "/");
        var texto = LembreteRetorno.Montar(negocio.LembreteRetornoMensagem, cliente.Nome, negocio.NomeExibido, negocio.LembreteRetornoDias, link);
        return new Montagem(Telefone: cliente.Telefone, Texto: texto);
    }

    private Task<Agendamento?> CarregarAgendamentoAsync(NotificacaoPendente item, CancellationToken cancellationToken) =>
        _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos).FirstOrDefaultAsync(a => a.Id == item.AgendamentoId, cancellationToken);

    // ------------------------------------------------------------------ gravação (sempre fenceada pela reivindicação)

    /// <summary>
    /// Grava "vou chamar o provedor": conta a tentativa, renova o lease e registra o ponto sem retorno. Valores absolutos (não
    /// <c>tentativas + 1</c>) para uma repetição automática do comando não contar a tentativa duas vezes.
    /// </summary>
    private async Task<bool> MarcarInicioAsync(NotificacaoPendente item, Guid token, int tentativas, DateTimeOffset agora)
    {
        var leaseAte = agora.AddSeconds(_opcoes.LeaseSegundos).ToUniversalTime();
        var instante = agora.ToUniversalTime();

        var linhas = await _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
            .Where(n => n.Id == item.Id && n.ReivindicacaoId == token && n.Status == StatusNotificacao.Enviando)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.EnvioIniciadoEm, (DateTimeOffset?)instante)
                .SetProperty(n => n.Tentativas, tentativas)
                .SetProperty(n => n.LeaseAte, (DateTimeOffset?)leaseAte)
                .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)instante), CancellationToken.None);

        return linhas == 1;
    }

    private async Task GravarAsync(
        NotificacaoPendente item, Guid token, Transicao transicao, Montagem? montagem, ResultadoDespacho? resultado, CancellationToken cancellationToken)
    {
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        var gravada = await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (await AplicarTransicaoAsync(item.Id, token, transicao, cancellationToken) == 0)
            {
                // A reivindicação passou para outra instância: o que ela decidir vale.
                await transacao.RollbackAsync(cancellationToken);
                return false;
            }

            await RegistrarEfeitosAsync(item, transicao, montagem, resultado, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return true;
        });

        if (gravada && transicao.Status == StatusNotificacao.Falhou)
        {
            // Log estruturado sem dado pessoal: ids, tipo, canal, código e tentativas.
            _logger.LogWarning(
                "Notificação {NotificacaoId} do negócio {NegocioId} falhou definitivamente ({Tipo}/{Canal}): {Codigo} após {Tentativas} tentativa(s).",
                item.Id, item.NegocioId, item.Tipo, item.Canal, transicao.Codigo, transicao.Tentativas);
        }
        else if (gravada && transicao.Status == StatusNotificacao.Desconhecida)
        {
            _logger.LogWarning(
                "Notificação {NotificacaoId} do negócio {NegocioId} ficou com resultado desconhecido ({Tipo}/{Canal}); não será reenviada.",
                item.Id, item.NegocioId, item.Tipo, item.Canal);
        }
    }

    private Task<int> AplicarTransicaoAsync(Guid id, Guid token, Transicao t, CancellationToken cancellationToken)
    {
        var instante = t.Agora.ToUniversalTime();
        DateTimeOffset? proxima = t.ProximaTentativaEm?.ToUniversalTime();
        DateTimeOffset? enviadaEm = t.Status == StatusNotificacao.Enviada ? instante : null;
        DateTimeOffset? canceladaEm = t.Status == StatusNotificacao.Cancelada ? instante : null;
        var status = t.Status;
        var tentativas = t.Tentativas;
        var codigo = t.Codigo;
        var idMensagem = t.IdMensagem;

        return _dbContext.NotificacoesPendentes.IgnoreQueryFilters()
            .Where(n => n.Id == id && n.ReivindicacaoId == token && n.Status == StatusNotificacao.Enviando)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Status, status)
                .SetProperty(n => n.Tentativas, tentativas)
                .SetProperty(n => n.ProximaTentativaEm, n => proxima ?? n.ProximaTentativaEm)
                .SetProperty(n => n.LeaseAte, (DateTimeOffset?)null)
                .SetProperty(n => n.ReivindicacaoId, (Guid?)null)
                .SetProperty(n => n.EnviadaEm, n => enviadaEm ?? n.EnviadaEm)
                .SetProperty(n => n.CanceladaEm, n => canceladaEm ?? n.CanceladaEm)
                .SetProperty(n => n.UltimoErroCodigo, codigo)
                .SetProperty(n => n.IdMensagemProvedor, n => idMensagem ?? n.IdMensagemProvedor)
                .SetProperty(n => n.AtualizadoEm, (DateTimeOffset?)instante), cancellationToken);
    }

    /// <summary>
    /// Efeitos de dentro da mesma transação do resultado: o aviso ao profissional aceito ganha o registro por canal (é onde o webhook de
    /// status do WhatsApp grava depois) e o lembrete de retorno atualiza o seu registro de relatório e de idempotência.
    /// </summary>
    private async Task RegistrarEfeitosAsync(
        NotificacaoPendente item, Transicao transicao, Montagem? montagem, ResultadoDespacho? resultado, CancellationToken cancellationToken)
    {
        if (item.Tipo == TipoNotificacao.LembreteRetorno)
        {
            if (transicao.Status == StatusNotificacao.Pendente)
                return;

            var envio = await _dbContext.LembretesRetornoEnvios
                .FirstOrDefaultAsync(e => e.ClienteId == item.ClienteId && e.AgendamentoId == item.AgendamentoId, cancellationToken);
            if (envio is null)
                return;

            switch (transicao.Status)
            {
                case StatusNotificacao.Enviada:
                    envio.MarcarEnviado(transicao.IdMensagem, transicao.Agora);
                    break;
                case StatusNotificacao.Desconhecida:
                    envio.MarcarDesconhecido(transicao.Codigo ?? CodigosOutbox.TimeoutAmbiguo, transicao.Agora);
                    break;
                default:
                    envio.MarcarFalha(transicao.Codigo ?? CodigosOutbox.ErroInterno, transicao.Agora);
                    break;
            }

            return;
        }

        if (transicao.Status != StatusNotificacao.Enviada || montagem?.Agendamento is not { } agendamento || montagem.Evento is not { } evento)
            return;

        var email = item.Canal == CanalNotificacao.Email ? StatusCanal.Enviado : (StatusCanal?)null;
        var whatsApp = item.Canal == CanalNotificacao.WhatsApp ? resultado?.Status ?? StatusCanal.Enviado : (StatusCanal?)null;
        var idMensagem = item.Canal == CanalNotificacao.WhatsApp ? transicao.IdMensagem : null;

        var notificacao = new NotificacaoProfissional(
            item.NegocioId, agendamento.Id, item.ProfissionalId ?? agendamento.ProfissionalId, evento.ToString(), email, whatsApp, idMensagem);
        foreach (var status in await EventosWhatsAppRecebidos.ListarAsync(_dbContext, idMensagem, cancellationToken))
            notificacao.AtualizarStatusWhatsApp(status);

        _dbContext.NotificacoesProfissional.Add(notificacao);
    }
}
