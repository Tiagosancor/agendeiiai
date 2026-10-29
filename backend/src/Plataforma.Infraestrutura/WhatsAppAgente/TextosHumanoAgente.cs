namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>Textos do servidor no atendimento humano (Fase 4). O modelo nunca escreve nenhum deles. Puro.</summary>
public static class TextosHumanoAgente
{
    /// <summary>
    /// Aviso ao cliente quando o agente chama um atendente: texto fixo + a mensagem de transferência do negócio. O nome do negócio e a
    /// mensagem são cadastro livre e passam, cada um sozinho, pelo mesmo sanitizador da resposta do modelo (link e telefone alheios saem).
    /// </summary>
    public static string Transferencia(string nomeNegocio, string? mensagemDoNegocio, string linkPublico, string? telefoneInstancia)
    {
        var host = Uri.TryCreate(linkPublico, UriKind.Absolute, out var uri) ? uri.Authority : linkPublico;
        var nome = SanitizadorRespostaAgente.SanitizarFragmento(ConstrutorPromptAgente.LimparNome(nomeNegocio), host, telefoneInstancia, 120);
        var fixo = $"Vou chamar alguém da equipe de {(nome.Length > 0 ? nome : "nosso estabelecimento")}. Assim que possível, uma pessoa responde por aqui.";

        var extra = SanitizadorRespostaAgente.SanitizarFragmento(mensagemDoNegocio, host, telefoneInstancia, 300);
        return extra.Length > 0 ? fixo + "\n\n" + extra : fixo;
    }

    /// <summary>Motivo em texto para o e-mail ao dono (códigos fechados; nunca texto do modelo).</summary>
    public static string MotivoParaEmail(string? motivo) => motivo switch
    {
        "pedido_do_cliente" => "o cliente pediu para falar com uma pessoa",
        "reclamacao" => "reclamação do cliente",
        "fora_do_escopo" => "assunto fora do que o assistente atende",
        _ => "outro motivo",
    };
}
