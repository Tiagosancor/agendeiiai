namespace Plataforma.Dominio.Clientes;

/// <summary>De onde o cliente veio — usado pela recepção para entender o cadastro (seção 8.1.4).</summary>
public enum OrigemCliente
{
    Painel = 1,
    LinkPublico = 2,

    /// <summary>Cadastro rápido no balcão, ao lançar um atendimento sem agendamento (seção 7).</summary>
    Encaixe = 3,

    /// <summary>Cliente criado pelo agente de WhatsApp (Fase 3). Nada grava este valor antes da fatia de escrita.</summary>
    WhatsApp = 4,
}
