using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Infraestrutura.Comissoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Decisão canônica do link público de gerenciamento (seção 6.3): o que o cliente pode fazer com o agendamento e
/// quais horários existem para remarcá-lo. Reaproveita as regras que já valiam nos POSTs (status do domínio,
/// antecedência mínima, trava de quinzena) e o mesmo motor de <see cref="IConsultaDisponibilidade"/>.
/// </summary>
public sealed class GestaoPublicaAgendamento : IGestaoPublicaAgendamento
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IConsultaDisponibilidade _consultaDisponibilidade;
    private readonly TravaQuinzenas _travaQuinzenas;
    private readonly OpcoesAgendamentoPublico _opcoes;

    public GestaoPublicaAgendamento(
        PlataformaDbContext dbContext, IConsultaDisponibilidade consultaDisponibilidade, TravaQuinzenas travaQuinzenas,
        IOptions<OpcoesAgendamentoPublico> opcoes)
    {
        _dbContext = dbContext;
        _consultaDisponibilidade = consultaDisponibilidade;
        _travaQuinzenas = travaQuinzenas;
        _opcoes = opcoes.Value;
    }

    public async Task<DetalheGestaoPublica?> ObterAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var agendamento = await CarregarAsync(agendamentoId, cancellationToken);
        if (agendamento is null)
            return null;

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == agendamento.NegocioId, cancellationToken);
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == agendamento.ProfissionalId)
            .Select(p => new ProfissionalGestaoPublica(p.Id, p.Nome))
            .FirstAsync(cancellationToken);

        var local = string.Join(", ", new[] { negocio.Endereco.Rua, negocio.Endereco.Numero, negocio.Endereco.Bairro, negocio.Endereco.Cidade }
            .Where(parte => !string.IsNullOrWhiteSpace(parte)));

        var agora = DateTimeOffset.UtcNow;
        var acoes = await AvaliarAsync(agendamento, agora, cancellationToken);
        var antecedencia = _opcoes.AntecedenciaMinimaCancelamentoHoras;

        return new DetalheGestaoPublica(
            agendamento.Id, negocio.NomeExibido, local, agendamento.Inicio.ToUniversalTime(), agendamento.Fim.ToUniversalTime(),
            agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, agendamento.Status.ToString(),
            negocio.Fuso, agendamento.Servicos.Sum(s => s.DuracaoMinutos), profissional,
            agendamento.Servicos.Select(s => new ServicoGestaoPublica(s.ServicoId, s.Nome, s.DuracaoMinutos, s.Preco)).ToList(),
            new RegrasGestaoPublica(antecedencia, agendamento.Inicio.ToUniversalTime().AddHours(-antecedencia)),
            acoes);
    }

    public async Task<ResultadoHorariosRemarcacao?> ListarHorariosParaRemarcacaoAsync(
        Guid agendamentoId, DateOnly data, CancellationToken cancellationToken = default)
    {
        var agendamento = await CarregarAsync(agendamentoId, cancellationToken);
        if (agendamento is null)
            return null;

        var remarcar = AvaliarRemarcar(agendamento, DateTimeOffset.UtcNow);
        if (!remarcar.Permitido)
            return new ResultadoHorariosRemarcacao(null, remarcar);

        var fuso = await _dbContext.Negocios.AsNoTracking()
            .Where(n => n.Id == agendamento.NegocioId).Select(n => n.Fuso).FirstAsync(cancellationToken);
        var duracao = agendamento.Servicos.Sum(s => s.DuracaoMinutos);

        var horarios = await _consultaDisponibilidade.ListarHorariosLivresAsync(
            agendamento.ProfissionalId, data, duracao, cancellationToken, ignorarAgendamentoId: agendamento.Id);

        return new ResultadoHorariosRemarcacao(
            new HorariosRemarcacao(fuso, data, duracao, agendamento.ProfissionalId, horarios), null);
    }

    private Task<Agendamento?> CarregarAsync(Guid agendamentoId, CancellationToken cancellationToken) =>
        _dbContext.Agendamentos.AsNoTracking().Include(a => a.Servicos)
            .FirstOrDefaultAsync(a => a.Id == agendamentoId, cancellationToken);

    private async Task<AcoesGestaoPublica> AvaliarAsync(Agendamento agendamento, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        var cancelar = AvaliarCancelar(agendamento, agora);

        // Só consulta o banco quando o resto já deixaria cancelar; é a mesma trava que o POST aplica.
        if (cancelar.Permitido && await _travaQuinzenas.AtendimentoTravadoAsync(agendamento.ProfissionalId, agendamento.Inicio, cancellationToken))
            cancelar = AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.NaoPermitido, MensagemNaoPermitido);

        return new AcoesGestaoPublica(cancelar, AvaliarRemarcar(agendamento, agora));
    }

    public const string MensagemNaoPermitido = "Não é possível alterar este agendamento pelo link. Fale com o estabelecimento.";

    /// <summary>
    /// O domínio ainda deixa o painel cancelar Faltou/EmAtendimento (uso administrativo); o cliente, pelo link, não:
    /// quem já está em atendimento ou faltou não tem o que cancelar sozinho.
    /// </summary>
    private AcaoGestaoPublica AvaliarCancelar(Agendamento agendamento, DateTimeOffset agora) =>
        !agendamento.PodeSerCancelado || agendamento.Status is StatusAgendamento.EmAtendimento or StatusAgendamento.Faltou
            ? AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.StatusNaoPermite, "Este agendamento não pode mais ser cancelado.")
            : Temporal(agendamento, agora, $"Cancele com pelo menos {_opcoes.AntecedenciaMinimaCancelamentoHoras}h de antecedência.");

    private AcaoGestaoPublica AvaliarRemarcar(Agendamento agendamento, DateTimeOffset agora) =>
        !agendamento.PodeSerMovido
            ? AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.StatusNaoPermite, "Este agendamento não pode mais ser remarcado.")
            : Temporal(agendamento, agora, $"Remarque com pelo menos {_opcoes.AntecedenciaMinimaCancelamentoHoras}h de antecedência.");

    /// <summary>Mesma conta que o POST sempre fez: início menos agora precisa ser ao menos a antecedência mínima.</summary>
    private AcaoGestaoPublica Temporal(Agendamento agendamento, DateTimeOffset agora, string mensagemAntecedencia)
    {
        var faltam = agendamento.Inicio - agora;

        if (faltam < TimeSpan.Zero)
            return AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.AgendamentoPassado, "Este horário já passou.");

        return faltam < TimeSpan.FromHours(_opcoes.AntecedenciaMinimaCancelamentoHoras)
            ? AcaoGestaoPublica.Bloqueada(MotivosGestaoPublica.AntecedenciaMinima, mensagemAntecedencia)
            : AcaoGestaoPublica.Liberada;
    }
}
