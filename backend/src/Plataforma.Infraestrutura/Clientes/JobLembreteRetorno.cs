using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Clientes;

/// <summary>
/// Lembrete de retorno por WhatsApp: "faz N dias desde o seu último atendimento", com o link público do negócio. Recorrente
/// (a cada 15 min, como os outros jobs: sobrevive à hibernação da API) — cada negócio só é processado dentro da janela que começa
/// no horário configurado, no fuso dele, e só para o atendimento de exatamente N dias atrás: um dia perdido não é enviado depois.
/// <para>
/// Referência = o último atendimento <see cref="StatusAgendamento.Concluido"/> do cliente; faltas e cancelamentos depois dele não
/// mudam nada. Fica de fora: cliente com agendamento futuro, sem telefone, anonimizado ou que recusou o lembrete; encaixe sem
/// autorização de mensagens; negócio com assinatura suspensa; disparo anterior a quando o lembrete foi ligado. Tudo é conferido de
/// novo no banco imediatamente antes de enviar.
/// </para>
/// </summary>
public sealed class JobLembreteRetorno
{
    private const string ErroProvedor = "O provedor de WhatsApp não aceitou a mensagem.";

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly INotificador _notificador;
    private readonly IConsultaSituacaoAssinatura _assinaturas;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly OpcoesLembreteRetorno _opcoes;
    private readonly ILogger<JobLembreteRetorno> _logger;

    public JobLembreteRetorno(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, INotificador notificador,
        IConsultaSituacaoAssinatura assinaturas, IOptions<OpcoesMarca> opcoesMarca, IOptions<OpcoesLembreteRetorno> opcoes,
        ILogger<JobLembreteRetorno> logger)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _notificador = notificador;
        _assinaturas = assinaturas;
        _opcoesMarca = opcoesMarca.Value;
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public Task ExecutarAsync(CancellationToken cancellationToken = default) => ExecutarAsync(DateTimeOffset.UtcNow, cancellationToken);

    public async Task ExecutarAsync(DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        await MarcarAgendamentosGeradosAsync(agora, cancellationToken);

        // Desligado: o negócio nem entra na varredura.
        var negocios = await _dbContext.Negocios.AsNoTracking()
            .Where(n => n.Ativo && n.LembreteRetornoAtivo)
            .Select(n => n.Id)
            .ToListAsync(cancellationToken);

        foreach (var negocioId in negocios)
        {
            try
            {
                await ProcessarNegocioAsync(negocioId, agora, cancellationToken);
            }
            catch (Exception excecao) when (excecao is not OperationCanceledException)
            {
                // Um negócio com problema não impede os outros.
                _logger.LogError(excecao, "Falha no lembrete de retorno do negócio {NegocioId}.", negocioId);
                _dbContext.ChangeTracker.Clear();
            }
        }
    }

    /// <summary>Quantos lembretes saíram com sucesso para o negócio nesta execução.</summary>
    public async Task<int> ProcessarNegocioAsync(Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        var enviados = 0;

        foreach (var lote in (await SelecionarAsync(negocioId, agora, cancellationToken)).Chunk(_opcoes.TamanhoLote))
        {
            foreach (var agendamentoId in lote)
            {
                try
                {
                    if (await EnviarAsync(negocioId, agendamentoId, agora, cancellationToken))
                        enviados++;
                }
                catch (Exception excecao) when (excecao is not OperationCanceledException)
                {
                    // Uma falha num cliente não interrompe os demais.
                    _logger.LogError(excecao, "Falha no lembrete de retorno do atendimento {AgendamentoId}.", agendamentoId);
                    _dbContext.ChangeTracker.Clear();
                }
            }

            _dbContext.ChangeTracker.Clear();
        }

        return enviados;
    }

    /// <summary>Atendimentos de referência que devem receber o lembrete agora (vazio fora da janela do negócio).</summary>
    public async Task<IReadOnlyList<Guid>> SelecionarAsync(Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        var contexto = await ObterContextoAsync(negocioId, agora, cancellationToken);
        if (contexto is null)
            return [];

        var (negocio, hoje, fuso) = contexto.Value;
        var diaReferencia = hoje.AddDays(-negocio.LembreteRetornoDias);
        var inicioDoDia = ConversorFusoHorario.ParaUtc(diaReferencia, TimeOnly.MinValue, fuso).ToUniversalTime();
        var fimDoDia = ConversorFusoHorario.ParaUtc(diaReferencia.AddDays(1), TimeOnly.MinValue, fuso).ToUniversalTime();

        var candidatos = await Elegiveis(negocioId, agora, hoje)
            .Where(a => a.Inicio >= inicioDoDia && a.Inicio < fimDoDia)
            .Select(a => new { a.Id, ClienteId = a.ClienteId!.Value, a.Inicio })
            .ToListAsync(cancellationToken);

        // Dois concluídos no mesmo instante para o mesmo cliente: um lembrete só.
        return candidatos
            .GroupBy(c => c.ClienteId)
            .Select(g => g.OrderByDescending(c => c.Inicio).ThenBy(c => c.Id).First().Id)
            .ToList();
    }

    /// <summary>
    /// Confere tudo de novo, reserva o registro (o índice único barra uma segunda execução) e envia. Verdadeiro = saiu.
    /// </summary>
    public async Task<bool> EnviarAsync(Guid negocioId, Guid agendamentoId, DateTimeOffset agora, CancellationToken cancellationToken = default)
    {
        var contexto = await ObterContextoAsync(negocioId, agora, cancellationToken);
        if (contexto is null)
            return false;

        var (negocio, hoje, _) = contexto.Value;

        var referencia = await Elegiveis(negocioId, agora, hoje)
            .Where(a => a.Id == agendamentoId)
            .Select(a => new { a.Id, ClienteId = a.ClienteId!.Value })
            .FirstOrDefaultAsync(cancellationToken);

        if (referencia is null)
            return false;

        // No máximo um lembrete por cliente por dia, qualquer que seja o atendimento de referência.
        if (await _dbContext.LembretesRetornoEnvios.AnyAsync(
                e => e.ClienteId == referencia.ClienteId && e.DiaEnvio == hoje && e.AgendamentoId != referencia.Id, cancellationToken))
            return false;

        var cliente = await _dbContext.Clientes.AsNoTracking().FirstAsync(c => c.Id == referencia.ClienteId, cancellationToken);
        if (cliente.Telefone is null)
            return false;

        var envio = await _dbContext.LembretesRetornoEnvios
            .FirstOrDefaultAsync(e => e.ClienteId == referencia.ClienteId && e.AgendamentoId == referencia.Id, cancellationToken);

        if (envio is null)
        {
            envio = LembreteRetornoEnvio.Iniciar(negocioId, referencia.ClienteId, referencia.Id, hoje, agora);
            _dbContext.LembretesRetornoEnvios.Add(envio);
        }
        else if (envio.PodeTentarDeNovo(hoje))
        {
            envio.NovaTentativa(agora);
        }
        else
        {
            return false;
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Outra execução pegou a mesma nova tentativa.
            _dbContext.ChangeTracker.Clear();
            return false;
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            // Outra execução reservou o mesmo atendimento.
            _dbContext.ChangeTracker.Clear();
            return false;
        }

        var link = ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, "/");
        var mensagem = LembreteRetorno.Montar(
            negocio.LembreteRetornoMensagem, cliente.Nome, negocio.NomeExibido, negocio.LembreteRetornoDias, link);

        ResultadoEnvioWhatsApp resultado;
        try
        {
            resultado = await _notificador.EnviarLembreteRetornoAsync(cliente.Telefone, mensagem, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException)
        {
            _logger.LogError(excecao, "Falha ao enviar o lembrete de retorno do atendimento {AgendamentoId}.", referencia.Id);
            resultado = ResultadoEnvioWhatsApp.Indisponivel();
        }

        // Pendente = o provedor aceitou e confirma a entrega depois (Evolution): conta como enviado.
        if (resultado.Status == StatusCanal.Falhou)
            envio.MarcarFalha(ErroProvedor, agora);
        else
            envio.MarcarEnviado(resultado.IdMensagem, agora);

        // Mesmo com o cancelamento pedido, o resultado de uma mensagem que já saiu precisa ficar gravado.
        await _dbContext.SaveChangesAsync(CancellationToken.None);
        return resultado.Status != StatusCanal.Falhou;
    }

    /// <summary>
    /// Atendimentos concluídos que hoje dariam lembrete (qualquer dia): é o último concluído do cliente, que aceita lembrete, tem
    /// telefone, não foi anonimizado, não tem agendamento futuro e ainda não recebeu (ou pode tentar de novo hoje).
    /// </summary>
    private IQueryable<Agendamento> Elegiveis(Guid negocioId, DateTimeOffset agora, DateOnly hoje)
    {
        var agendamentos = _dbContext.Agendamentos.Where(a => a.NegocioId == negocioId);

        return agendamentos
            .Where(a => a.Status == StatusAgendamento.Concluido && a.ClienteId != null)
            .Where(a => a.Origem != OrigemAgendamento.Encaixe || a.MensagensAutorizadasEm != null)
            .Where(a => !agendamentos.Any(b =>
                b.ClienteId == a.ClienteId && b.Status == StatusAgendamento.Concluido && b.Inicio > a.Inicio))
            .Where(a => _dbContext.Clientes.Any(c =>
                c.Id == a.ClienteId && c.AceitaLembreteRetorno && !c.Excluido && c.Telefone != null))
            .Where(a => !agendamentos.Any(f =>
                f.ClienteId == a.ClienteId && f.Inicio > agora
                && (f.Status == StatusAgendamento.Agendado
                    || f.Status == StatusAgendamento.EmAtendimento
                    || (f.Status == StatusAgendamento.Reservado && f.ReservadoAte > agora))))
            .Where(a => !_dbContext.LembretesRetornoEnvios.Any(e =>
                e.ClienteId == a.ClienteId && e.AgendamentoId == a.Id
                && !(e.Status == StatusLembreteRetorno.Falhou && e.Tentativas < LembreteRetornoEnvio.MaximoTentativas && e.DiaEnvio == hoje)));
    }

    /// <summary>Nulo quando o negócio não envia agora: desligado, fora da janela do dia, ligado depois do disparo ou suspenso.</summary>
    private async Task<(Negocio Negocio, DateOnly Hoje, TimeZoneInfo Fuso)?> ObterContextoAsync(
        Guid negocioId, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        // O job varre todos os negócios: o filtro global passa a valer para este.
        _contextoNegocio.Definir(negocioId);

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstOrDefaultAsync(n => n.Id == negocioId, cancellationToken);
        if (negocio is not { Ativo: true, LembreteRetornoAtivo: true })
            return null;

        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
        var (hoje, _) = ConversorFusoHorario.ParaLocal(agora, fuso);
        var disparo = ConversorFusoHorario.ParaUtc(hoje, negocio.LembreteRetornoHorario, fuso);

        if (agora < disparo || agora >= disparo.AddMinutes(_opcoes.JanelaEnvioMinutos))
            return null;

        // Ligado (ou religado) depois do disparo de hoje: hoje não sai nada; amanhã já vale.
        if (negocio.LembreteRetornoAtivadoEm is { } ativadoEm && disparo < ativadoEm)
            return null;

        if (await _assinaturas.ObterAsync(negocioId, cancellationToken) is { PermiteOperar: false })
            return null;

        return (negocio, hoje, fuso);
    }

    /// <summary>
    /// Relatório: marca no envio o primeiro agendamento que o cliente fez até 7 dias depois dele, para depois do envio (um atendimento
    /// antigo lançado tarde no painel não conta; reserva não confirmada também não).
    /// Todos os negócios de uma vez, inclusive os que desligaram o lembrete depois.
    /// </summary>
    private async Task MarcarAgendamentosGeradosAsync(DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var desde = agora.AddDays(-(DiasParaGerarAgendamento + 1));

        var gerados = await _dbContext.LembretesRetornoEnvios.IgnoreQueryFilters()
            .Where(e => e.Status == StatusLembreteRetorno.Enviado && e.AgendamentoGeradoId == null && e.EnviadoEm >= desde)
            .Select(e => new
            {
                e.Id,
                Gerado = _dbContext.Agendamentos.IgnoreQueryFilters()
                    .Where(a => a.NegocioId == e.NegocioId && a.ClienteId == e.ClienteId
                        && a.CriadoEm > e.EnviadoEm && a.CriadoEm <= e.EnviadoEm.AddDays(DiasParaGerarAgendamento)
                        && a.Inicio > e.EnviadoEm
                        && a.Status != StatusAgendamento.Reservado && a.Status != StatusAgendamento.Expirado)
                    .OrderBy(a => a.CriadoEm)
                    .Select(a => (Guid?)a.Id)
                    .FirstOrDefault(),
            })
            .Where(x => x.Gerado != null)
            .ToListAsync(cancellationToken);

        foreach (var item in gerados)
        {
            await _dbContext.LembretesRetornoEnvios.IgnoreQueryFilters()
                .Where(e => e.Id == item.Id && e.AgendamentoGeradoId == null)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.AgendamentoGeradoId, item.Gerado), cancellationToken);
        }
    }

    /// <summary>Até quantos dias depois do envio um agendamento novo conta como "gerado" pelo lembrete.</summary>
    public const int DiasParaGerarAgendamento = 7;
}
