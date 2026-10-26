namespace Plataforma.Dominio.Agendamentos;

/// <summary>De onde veio o agendamento (seção 7, "Atendimento sem agendamento").</summary>
public enum OrigemAgendamento
{
    LinkPublico = 1,
    Painel = 2,

    /// <summary>Cliente que chegou sem ter agendado, lançado no balcão.</summary>
    Encaixe = 3,

    /// <summary>Marcado pelo agente de WhatsApp (Fase 3). Nada grava este valor antes da fatia de escrita.</summary>
    WhatsApp = 4,
}
