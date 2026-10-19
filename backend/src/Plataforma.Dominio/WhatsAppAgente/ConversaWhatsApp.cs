using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum StatusConversaWhatsApp
{
    Agente = 1,
    Humano = 2,
}

/// <summary>Conversa de um cliente (por telefone) com uma instância. Única por (instância, telefone).</summary>
public class ConversaWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    public Guid InstanciaId { get; private set; }

    /// <summary>Telefone do cliente em E.164 (ex.: +5571988887777).</summary>
    public string TelefoneCliente { get; private set; } = string.Empty;

    public Guid? ClienteId { get; private set; }

    public StatusConversaWhatsApp Status { get; private set; } = StatusConversaWhatsApp.Agente;

    public DateTimeOffset UltimaMensagemEm { get; private set; }

    public string? Resumo { get; private set; }

    /// <summary>Trava (lease) da rodada do agente nesta conversa. Tomada por <c>UPDATE</c> condicional, não por esta propriedade.</summary>
    public DateTimeOffset? ProcessandoAte { get; private set; }

    /// <summary>Instante da última mensagem de usuário já tratada pelo agente (respondida ou descartada por limite).</summary>
    public DateTimeOffset? ProcessadaAte { get; private set; }

    protected ConversaWhatsApp()
    {
    }

    public ConversaWhatsApp(Guid negocioId, Guid instanciaId, string telefoneCliente, DateTimeOffset agora)
    {
        if (string.IsNullOrWhiteSpace(telefoneCliente))
            throw new ArgumentException("O telefone do cliente é obrigatório.", nameof(telefoneCliente));

        NegocioId = negocioId;
        InstanciaId = instanciaId;
        TelefoneCliente = telefoneCliente;
        UltimaMensagemEm = agora;
    }

    public void RegistrarMensagem(DateTimeOffset em)
    {
        if (em > UltimaMensagemEm)
            UltimaMensagemEm = em;
    }

    public bool AgenteResponde => Status == StatusConversaWhatsApp.Agente;
}
