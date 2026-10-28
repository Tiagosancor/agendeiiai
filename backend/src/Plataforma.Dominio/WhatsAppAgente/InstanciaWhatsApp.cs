using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.WhatsAppAgente;

public enum StatusInstanciaWhatsApp
{
    Desconectada = 1,
    AguardandoQrCode = 2,
    Conectada = 3,
}

/// <summary>
/// Instância da Evolution API de um negócio (agente de WhatsApp, Fase 1 do plano). Uma por negócio —
/// o webhook acha o negócio por <see cref="NomeInstancia"/> (único), nunca por parâmetro do cliente.
/// </summary>
public class InstanciaWhatsApp : EntidadeBase, IEntidadeDoNegocio
{
    public Guid NegocioId { get; private set; }

    /// <summary>Nome da instância na Evolution (único globalmente).</summary>
    public string NomeInstancia { get; private set; } = string.Empty;

    /// <summary>Número conectado, só dígitos com DDI (vem do evento de conexão).</summary>
    public string? Telefone { get; private set; }

    public StatusInstanciaWhatsApp Status { get; private set; } = StatusInstanciaWhatsApp.Desconectada;

    public bool AgenteAtivo { get; private set; }

    public string? MensagemBoasVindas { get; private set; }

    /// <summary>Horário de atendimento do agente, em JSON (formato definido na Fase 4).</summary>
    public string? HorarioAtendimentoJson { get; private set; }

    public string? MensagemTransferencia { get; private set; }

    protected InstanciaWhatsApp()
    {
    }

    public InstanciaWhatsApp(Guid negocioId, string nomeInstancia)
    {
        if (string.IsNullOrWhiteSpace(nomeInstancia))
            throw new ArgumentException("O nome da instância é obrigatório.", nameof(nomeInstancia));

        NegocioId = negocioId;
        NomeInstancia = nomeInstancia.Trim();
    }

    public void AtualizarStatus(StatusInstanciaWhatsApp status, string? telefone = null)
    {
        Status = status;

        if (status == StatusInstanciaWhatsApp.Conectada && !string.IsNullOrWhiteSpace(telefone))
            Telefone = telefone.Trim();
    }

    public void DefinirAgente(bool ativo) => AgenteAtivo = ativo;

    public const int MaximoMensagemTransferencia = 300;

    /// <summary>
    /// Texto que o negócio acrescenta ao aviso fixo de transferência para atendimento humano. Vazio tira. Sem caractere de controle e
    /// até <see cref="MaximoMensagemTransferencia"/> caracteres (o texto ainda passa pelo sanitizador antes de sair).
    /// </summary>
    public void DefinirMensagemTransferencia(string? mensagem)
    {
        if (string.IsNullOrWhiteSpace(mensagem))
        {
            MensagemTransferencia = null;
            return;
        }

        var limpa = mensagem.Trim();
        if (limpa.Length > MaximoMensagemTransferencia)
            throw new ArgumentException($"A mensagem de transferência tem no máximo {MaximoMensagemTransferencia} caracteres.", nameof(mensagem));
        if (limpa.Any(c => char.IsControl(c) && c != '\n'))
            throw new ArgumentException("A mensagem de transferência tem caracteres inválidos.", nameof(mensagem));

        MensagemTransferencia = limpa;
    }
}
