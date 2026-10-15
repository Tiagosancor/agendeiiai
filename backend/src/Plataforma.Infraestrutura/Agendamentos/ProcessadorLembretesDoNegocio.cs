using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Notificacoes.Outbox;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Lembrete de 60 minutos de UM negócio. Resolvido num escopo de DI próprio, criado por <see cref="JobEnviarLembretes"/> para cada
/// negócio: o <see cref="IContextoNegocio"/> (scoped) só aceita um tenant por escopo, então aqui o negócio é definido uma única vez
/// e o filtro global do <c>DbContext</c> vale normalmente — nada de <c>IgnoreQueryFilters</c>, nada de entidade rastreada de outro negócio.
/// </summary>
public sealed class ProcessadorLembretesDoNegocio
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly INotificador _notificador;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly OpcoesLembretes _opcoesLembretes;
    private readonly OutboxNotificacoes _outbox;
    private readonly OpcoesNotificacoes _opcoesNotificacoes;

    public ProcessadorLembretesDoNegocio(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, INotificador notificador,
        IServicoTokenPublico servicoToken, IOptions<OpcoesMarca> opcoesMarca, IOptions<OpcoesLembretes> opcoesLembretes,
        OutboxNotificacoes outbox, IOptions<OpcoesNotificacoes> opcoesNotificacoes)
    {
        _outbox = outbox;
        _opcoesNotificacoes = opcoesNotificacoes.Value;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _notificador = notificador;
        _servicoToken = servicoToken;
        _opcoesMarca = opcoesMarca.Value;
        _opcoesLembretes = opcoesLembretes.Value;
    }

    public async Task ProcessarAsync(Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        _contextoNegocio.Definir(negocioId);

        // Com a outbox, o job não envia: grava a mensagem (por canal, idempotente) na mesma transação que marca o lembrete.
        if (_outbox.Usa(FamiliaNotificacao.Lembrete60))
        {
            await _dbContext.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                _dbContext.ChangeTracker.Clear();
                await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                await ProcessarComOutboxAsync(negocioId, agora, cancellationToken);
                await transacao.CommitAsync(cancellationToken);
            });
            return;
        }

        var antecedencia = TimeSpan.FromMinutes(_opcoesLembretes.AntecedenciaMinutos);
        var intervaloMinimo = TimeSpan.FromMinutes(_opcoesLembretes.IntervaloMinimoAposMarcarMinutos);
        var limite = agora + antecedencia;

        var candidatos = await _dbContext.Agendamentos
            .Include(a => a.Servicos)
            .Where(a => a.Status == StatusAgendamento.Agendado && !a.LembreteEnviado && a.Inicio > agora && a.Inicio <= limite)
            .ToListAsync(cancellationToken);

        if (candidatos.Count == 0)
            return;

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);

        foreach (var agendamento in candidatos)
        {
            if (!agendamento.NaJanelaDoLembrete(agora, antecedencia))
                continue;

            // Marcado em cima da hora (a confirmação acabou de sair) ou encaixe sem autorização do
            // cliente para receber mensagens (seção 7) — dispensa (e não volta a olhar).
            if (!agendamento.LembreteFazSentido(antecedencia, intervaloMinimo) || !agendamento.PodeReceberMensagens)
            {
                agendamento.MarcarLembreteEnviado();
                continue;
            }

            if (agendamento.ClienteId is null)
                continue;

            var cliente = await _dbContext.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == agendamento.ClienteId, cancellationToken);

            if (cliente is null)
                continue;

            var tokenAgendamento = _servicoToken.GerarTokenAgendamento(negocio.Id, agendamento.Id);
            var link = ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, $"/agendamentos/{tokenAgendamento}");

            await _notificador.EnviarLembreteAsync(new DadosNotificacaoAgendamento(
                cliente.Nome, cliente.Email, cliente.Telefone, agendamento.Inicio, agendamento.Fim,
                agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, link, link), cancellationToken);

            agendamento.MarcarLembreteEnviado();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Caminho da outbox. <c>LembreteEnviado</c> passa a significar "enfileirado" (decidido, mensagem gravada): é o que impede a
    /// varredura de enfileirar de novo. O "enviado" de verdade mora na própria outbox (<c>Enviada</c>). Rollback para o envio direto
    /// não reenvia o que já foi enfileirado, pois o campo já está marcado.
    /// </summary>
    private async Task ProcessarComOutboxAsync(Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var antecedencia = TimeSpan.FromMinutes(_opcoesLembretes.AntecedenciaMinutos);
        var intervaloMinimo = TimeSpan.FromMinutes(_opcoesLembretes.IntervaloMinimoAposMarcarMinutos);
        var naoEnviarFaltando = TimeSpan.FromMinutes(_opcoesNotificacoes.LembreteNaoEnviarFaltandoMinutos);
        var limite = agora + antecedencia;

        var candidatos = await _dbContext.Agendamentos
            .Include(a => a.Servicos)
            .Where(a => a.Status == StatusAgendamento.Agendado && !a.LembreteEnviado && a.Inicio > agora && a.Inicio <= limite)
            .ToListAsync(cancellationToken);

        if (candidatos.Count == 0)
            return;

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);

        foreach (var agendamento in candidatos)
        {
            if (!agendamento.NaJanelaDoLembrete(agora, antecedencia))
                continue;

            if (!agendamento.LembreteFazSentido(antecedencia, intervaloMinimo) || !agendamento.PodeReceberMensagens)
            {
                agendamento.MarcarLembreteEnviado();
                continue;
            }

            if (agendamento.ClienteId is null)
                continue;

            var cliente = await _dbContext.Clientes.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == agendamento.ClienteId, cancellationToken);

            if (cliente is null)
                continue;

            await _outbox.EnfileirarLembreteAsync(agendamento, cliente, negocio, naoEnviarFaltando, cancellationToken);
            agendamento.MarcarLembreteEnviado();
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
