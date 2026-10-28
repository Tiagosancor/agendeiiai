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

    /// <summary>Quando o aviso de atendimento automatizado foi enviado pela última vez (Fase 3; controla o reenvio a cada N dias).</summary>
    public DateTimeOffset? AvisoAutomatizadoEm { get; private set; }

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

    /// <summary>Atalho para o cliente identificado na confirmação. A busca do cliente é sempre pelo telefone, nunca por este campo.</summary>
    public void VincularCliente(Guid clienteId) => ClienteId = clienteId;

    /// <summary>Usado na anonimização do cliente (LGPD): a conversa deixa de apontar para ele.</summary>
    public void DesvincularCliente() => ClienteId = null;

    /// <summary>Prefixo do telefone de uma conversa anonimizada (LGPD). O restante é um pedaço do <c>Id</c>: a coluna tem 20 posições.</summary>
    public const string PrefixoTelefoneAnonimizado = "anonimizado:";

    public bool Anonimizada => TelefoneCliente.StartsWith(PrefixoTelefoneAnonimizado, StringComparison.Ordinal);

    /// <summary>
    /// LGPD (G11): tira da linha tudo o que identifica a pessoa — telefone (vira <c>anonimizado:xxxxxxxx</c>, que cabe nas 20 posições da
    /// coluna e mantém o índice único por instância+telefone), cliente e resumo. A linha FICA porque <c>usos_ia</c> e <c>rodadas_agente</c>
    /// dependem dela (cota e diagnóstico, só números). Volta a <see cref="StatusConversaWhatsApp.Agente"/> e perde o lease: uma rodada em
    /// andamento deixa de ser dona da conversa (não envia nem marca). O mesmo número escrevendo depois abre uma conversa nova.
    /// </summary>
    /// <param name="telefonesEmUso">Telefones já usados por outras conversas da mesma instância (evita colisão no índice único).</param>
    public void Anonimizar(ISet<string> telefonesEmUso)
    {
        var texto = Id.ToString("N");
        string? escolhido = null;
        for (var inicio = 0; inicio + 8 <= texto.Length && escolhido is null; inicio += 8)
        {
            var candidato = PrefixoTelefoneAnonimizado + texto.Substring(inicio, 8);
            if (!telefonesEmUso.Contains(candidato))
                escolhido = candidato;
        }

        // Quatro pedaços de 32 bits de um Guid aleatório todos ocupados: praticamente impossível; cai no último.
        TelefoneCliente = escolhido ?? PrefixoTelefoneAnonimizado + texto[^8..];
        telefonesEmUso.Add(TelefoneCliente);
        ClienteId = null;
        Resumo = null;
        Status = StatusConversaWhatsApp.Agente;
        ProcessandoAte = null;
    }

    public void RegistrarAvisoAutomatizado(DateTimeOffset em) => AvisoAutomatizadoEm = em;

    public bool AgenteResponde => Status == StatusConversaWhatsApp.Agente;
}
