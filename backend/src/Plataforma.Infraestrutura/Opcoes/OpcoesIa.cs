using Microsoft.Extensions.Options;

namespace Plataforma.Infraestrutura.Opcoes;

public enum ProvedorIa
{
    /// <summary>Repete o que o cliente disse (Fase 1). Padrão: sobe sem chave nenhuma.</summary>
    Eco = 0,

    Gemini = 1,

    /// <summary>Salva as mensagens e não responde.</summary>
    Desligado = 2,
}

/// <summary>Agente de WhatsApp, Fase 2 — seção <c>Ai</c> (<c>Ai__Provider</c>, <c>Ai__Gemini__ApiKey</c>, ...).</summary>
public sealed class OpcoesIa
{
    public const string Secao = "Ai";

    public ProvedorIa Provider { get; set; } = ProvedorIa.Eco;

    public OpcoesGemini Gemini { get; set; } = new();

    /// <summary>Teto rígido de voltas do laço de ferramentas (1–5).</summary>
    public int MaxIteracoesFerramentas { get; set; } = 5;

    public int MaxChamadasFerramentaPorIteracao { get; set; } = 3;

    public int MaxMensagensHistorico { get; set; } = 20;

    public int MaxCaracteresMensagem { get; set; } = 1000;

    public int MaxCaracteresHistorico { get; set; } = 8000;

    public int MaxTokensSaida { get; set; } = 512;

    public int MaxCaracteresResposta { get; set; } = 1000;

    /// <summary>Orçamento de tempo total de uma rodada (todas as chamadas ao modelo e às ferramentas).</summary>
    public int TempoMaximoProcessamentoSegundos { get; set; } = 40;

    public int LimiteMensagensPorTelefonePorHora { get; set; } = 15;

    public int LimiteMensagensPorTelefonePorDia { get; set; } = 45;

    public int LimiteChamadasPorNegocioPorDia { get; set; } = 250;

    /// <summary>Tokens (entrada + saída) por negócio no mês civil UTC.</summary>
    public long CotaMensalTokensPorNegocio { get; set; } = 1_500_000;

    /// <summary>A <c>data</c> da ferramenta de horários vai de hoje até hoje + este número de dias (fuso do negócio).</summary>
    public int DiasMaximosConsulta { get; set; } = 60;

    /// <summary>Lista de <c>Guid</c> separada por vírgula. Vazio = todos os negócios (piloto: preencha).</summary>
    public string? NegociosHabilitados { get; set; }

    public bool NegocioHabilitado(Guid negocioId)
    {
        if (string.IsNullOrWhiteSpace(NegociosHabilitados))
            return true;

        return NegociosHabilitados
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => Guid.TryParse(item, out var id) && id == negocioId);
    }
}

public sealed class OpcoesGemini
{
    public string? ApiKey { get; set; }

    /// <summary>Nome do modelo; confira na documentação do Google AI Studio antes de ativar (modelos são descontinuados).</summary>
    public string Model { get; set; } = "gemini-2.5-flash-lite";

    public string UrlBase { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public int TimeoutSegundos { get; set; } = 15;
}

public sealed class ValidadorOpcoesIa : IValidateOptions<OpcoesIa>
{
    public ValidateOptionsResult Validate(string? name, OpcoesIa o)
    {
        var erros = new List<string>();

        if (!Enum.IsDefined(o.Provider))
            erros.Add("Ai__Provider deve ser Eco, Gemini ou Desligado.");

        if (o.Provider == ProvedorIa.Gemini)
        {
            if (string.IsNullOrWhiteSpace(o.Gemini.ApiKey))
                erros.Add("Ai__Gemini__ApiKey é obrigatória com Ai__Provider=Gemini.");
            if (string.IsNullOrWhiteSpace(o.Gemini.Model))
                erros.Add("Ai__Gemini__Model é obrigatório com Ai__Provider=Gemini.");
            if (!Uri.TryCreate(o.Gemini.UrlBase, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                erros.Add("Ai__Gemini__UrlBase deve ser uma URL https.");
        }

        Faixa(erros, nameof(o.MaxIteracoesFerramentas), o.MaxIteracoesFerramentas, 1, 5);
        Faixa(erros, nameof(o.MaxChamadasFerramentaPorIteracao), o.MaxChamadasFerramentaPorIteracao, 1, 10);
        Faixa(erros, nameof(o.MaxMensagensHistorico), o.MaxMensagensHistorico, 1, 100);
        Faixa(erros, nameof(o.MaxCaracteresMensagem), o.MaxCaracteresMensagem, 50, 4000);
        Faixa(erros, nameof(o.MaxCaracteresHistorico), o.MaxCaracteresHistorico, 200, 50_000);
        Faixa(erros, nameof(o.MaxTokensSaida), o.MaxTokensSaida, 32, 8192);
        Faixa(erros, nameof(o.MaxCaracteresResposta), o.MaxCaracteresResposta, 100, 4000);
        Faixa(erros, nameof(o.TempoMaximoProcessamentoSegundos), o.TempoMaximoProcessamentoSegundos, 5, 85);
        Faixa(erros, "Gemini.TimeoutSegundos", o.Gemini.TimeoutSegundos, 1, 60);
        Faixa(erros, nameof(o.LimiteMensagensPorTelefonePorHora), o.LimiteMensagensPorTelefonePorHora, 1, 1000);
        Faixa(erros, nameof(o.LimiteMensagensPorTelefonePorDia), o.LimiteMensagensPorTelefonePorDia, 1, 10_000);
        Faixa(erros, nameof(o.LimiteChamadasPorNegocioPorDia), o.LimiteChamadasPorNegocioPorDia, 1, 1_000_000);
        Faixa(erros, nameof(o.DiasMaximosConsulta), o.DiasMaximosConsulta, 1, 365);
        if (o.CotaMensalTokensPorNegocio < 1)
            erros.Add("Ai__CotaMensalTokensPorNegocio deve ser positiva.");

        if (!string.IsNullOrWhiteSpace(o.NegociosHabilitados)
            && o.NegociosHabilitados.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(item => !Guid.TryParse(item, out _)))
            erros.Add("Ai__NegociosHabilitados deve ser uma lista de GUIDs separada por vírgula.");

        return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
    }

    private static void Faixa(List<string> erros, string nome, int valor, int minimo, int maximo)
    {
        if (valor < minimo || valor > maximo)
            erros.Add($"Ai: {nome} deve estar entre {minimo} e {maximo}.");
    }
}
