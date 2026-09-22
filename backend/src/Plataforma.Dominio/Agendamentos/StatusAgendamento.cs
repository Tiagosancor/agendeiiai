namespace Plataforma.Dominio.Agendamentos;

public enum StatusAgendamento
{
    /// <summary>Reserva temporária (10 min) enquanto o cliente preenche dados/código (seção 8.2.2). Só existe no fluxo público (Sprint 3), mas o mecanismo já é testado agora.</summary>
    Reservado = 1,
    Agendado = 2,
    Concluido = 3,
    Cancelado = 4,
    Faltou = 5,
    /// <summary>Reserva que passou de <c>ReservadoAte</c> sem ser confirmada — sai do conjunto que a exclusion constraint protege (seção 8.2.1).</summary>
    Expirado = 6,
}
