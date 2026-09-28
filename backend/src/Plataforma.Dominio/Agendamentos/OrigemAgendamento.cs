namespace Plataforma.Dominio.Agendamentos;

/// <summary>De onde veio o agendamento (seção 7, "Atendimento sem agendamento").</summary>
public enum OrigemAgendamento
{
    LinkPublico = 1,
    Painel = 2,

    /// <summary>Cliente que chegou sem ter agendado, lançado no balcão.</summary>
    Encaixe = 3,
}
