namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>
/// Visão "Dia" da agenda (seção 7): horários na grade de 15 minutos nas linhas e um profissional ativo por
/// coluna, cada célula com o estado do horário — para a recepção ver todo mundo de uma vez.
/// </summary>
public interface IConsultaGradeAgenda
{
    Task<GradeAgenda> ObterAsync(DateOnly data, CancellationToken cancellationToken = default);
}

/// <summary><c>Horarios</c> = rótulos das linhas ("09:00"), na mesma ordem das células de cada coluna.</summary>
public sealed record GradeAgenda(DateOnly Data, IReadOnlyList<string> Horarios, IReadOnlyList<ColunaGrade> Profissionais);

/// <summary><c>DeFolga</c> = sem expediente no dia, ou com o expediente inteiro bloqueado (o filtro "esconder quem está de folga").</summary>
public sealed record ColunaGrade(
    Guid ProfissionalId, string Nome, bool DeFolga, IReadOnlyList<CelulaGrade> Celulas, IReadOnlyList<AgendamentoNaGrade> Agendamentos);

/// <summary>
/// Um horário de 15 minutos de um profissional. <c>Estado</c>: ver <see cref="EstadoCelulaGrade"/>;
/// <c>AgendamentoId</c> só em <c>Ocupado</c>; <c>Descricao</c> = motivo do bloqueio.
/// </summary>
public sealed record CelulaGrade(string Hora, DateTimeOffset Inicio, string Estado, Guid? AgendamentoId = null, string? Descricao = null);

public sealed record AgendamentoNaGrade(
    Guid Id, string ClienteNome, IReadOnlyList<string> Servicos, string Status, bool Forcado, DateTimeOffset Inicio, DateTimeOffset Fim);

public static class EstadoCelulaGrade
{
    /// <summary>Dentro do expediente, sem ninguém: clicável, abre a criação com profissional e horário.</summary>
    public const string Livre = "Livre";

    public const string Ocupado = "Ocupado";

    /// <summary>O espaço entre dois turnos do dia.</summary>
    public const string Almoco = "Almoco";

    /// <summary>Sem expediente nesse dia da semana.</summary>
    public const string Folga = "Folga";

    /// <summary>Folga ou bloqueio por período.</summary>
    public const string Bloqueio = "Bloqueio";

    public const string ForaDoExpediente = "ForaDoExpediente";
}
