namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Instruções do pagamento manual da assinatura enquanto não há gateway (seção 7) — variáveis <c>Cobranca__*</c>.</summary>
public sealed class OpcoesCobranca
{
    public const string Secao = "Cobranca";

    /// <summary><c>Manual</c> (padrão) ou <c>Asaas</c> — qual gateway cobra as assinaturas novas.</summary>
    public ProvedorCobranca Provedor { get; set; } = ProvedorCobranca.Manual;

    public string? ChavePix { get; set; }

    /// <summary>WhatsApp de contato para combinar o pagamento, só dígitos com DDI (ex.: 5571999999999).</summary>
    public string? WhatsAppContato { get; set; }
}

public enum ProvedorCobranca
{
    Manual,
    Asaas,
}
