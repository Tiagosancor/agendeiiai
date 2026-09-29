namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Gateway Asaas da cobrança da assinatura (variáveis <c>Asaas__*</c>). Só entra em uso com <c>Cobranca__Provedor=Asaas</c>, e
/// aí chave e token do webhook são obrigatórios (validado na subida). Segredos só em variável de ambiente.
/// </summary>
public sealed class OpcoesAsaas
{
    public const string Secao = "Asaas";

    public const string UrlSandbox = "https://api-sandbox.asaas.com/v3";

    /// <summary>Sandbox por padrão; produção é <c>https://api.asaas.com/v3</c>.</summary>
    public string UrlBase { get; set; } = UrlSandbox;

    /// <summary>Cabeçalho <c>access_token</c> de toda chamada à API.</summary>
    public string? ChaveApi { get; set; }

    /// <summary>O mesmo "token de autenticação" cadastrado no webhook do painel do Asaas (cabeçalho <c>asaas-access-token</c>).</summary>
    public string? TokenWebhook { get; set; }

    public int TimeoutSegundos { get; set; } = 10;

    /// <summary>Cabeçalho <c>User-Agent</c> (obrigatório no Asaas); vazio = o nome do produto (<c>Marca__NomeProduto</c>).</summary>
    public string? NomeAplicacao { get; set; }

    public bool Configurado => !string.IsNullOrWhiteSpace(ChaveApi) && !string.IsNullOrWhiteSpace(TokenWebhook);
}
