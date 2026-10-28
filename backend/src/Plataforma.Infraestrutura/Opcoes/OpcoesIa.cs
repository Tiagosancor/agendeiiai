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

    public int MaxTokensSaida { get; set; } = 1536;

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

    /// <summary>Fase 3: agendamento pelo agente. Desligado por padrão (comportamento idêntico ao da Fase 2).</summary>
    public OpcoesAgendamentoAgente Agendamento { get; set; } = new();

    /// <summary>Intervalo, em dias, do aviso fixo de atendimento automatizado por conversa (só sai com o agendamento ligado).</summary>
    public int AvisoAutomatizadoDias { get; set; } = 90;

    /// <summary>
    /// Fase 4: grava uma linha por rodada em <c>rodadas_agente</c> (e os campos extras de <c>usos_ia</c>) — só números e códigos, para
    /// o diagnóstico sobreviver ao log curto da hospedagem. Desligar para de gravar e não muda o comportamento do agente.
    /// </summary>
    public bool RegistrarRodadas { get; set; } = true;

    /// <summary>Dias que <c>rodadas_agente</c> é mantida; o job diário apaga o que for mais velho (7–365).</summary>
    public int RetencaoRodadasDias { get; set; } = 90;

    /// <summary>Mensagem fixa "atendimento automático indisponível" (negócio sem o serviço de IA contratado, agente ligado na tela).</summary>
    public OpcoesIndisponivelAgente Indisponivel { get; set; } = new();

    /// <summary>
    /// Parte GLOBAL da conta (disjuntor): Gemini + piloto da Fase 2 + modo/piloto da Fase 3. NÃO inclui a contratação do negócio —
    /// quem decide se o agente roda para um negócio é a <c>PoliticaAgenteIa</c> (global E contratado).
    /// </summary>
    public bool AgendamentoAtivoPara(Guid negocioId) =>
        Provider == ProvedorIa.Gemini && NegocioHabilitado(negocioId) && Agendamento.Habilitado(negocioId);

    public bool NegocioHabilitado(Guid negocioId)
    {
        if (string.IsNullOrWhiteSpace(NegociosHabilitados))
            return true;

        return NegociosHabilitados
            .Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(item => Guid.TryParse(item, out var id) && id == negocioId);
    }
}

/// <summary>Seção <c>Ai:Indisponivel</c> (<c>Ai__Indisponivel__IntervaloHoras</c>, <c>Ai__Indisponivel__LimitePorNegocioPorDia</c>).</summary>
public sealed class OpcoesIndisponivelAgente
{
    /// <summary>No máximo uma mensagem fixa por conversa neste intervalo (1–168 h).</summary>
    public int IntervaloHoras { get; set; } = 24;

    /// <summary>Teto de mensagens fixas por negócio por dia UTC; acima disso, silêncio (1–10.000).</summary>
    public int LimitePorNegocioPorDia { get; set; } = 200;
}

public enum ModoAgendamentoAgente
{
    Desligado = 0,
    Piloto = 1,
    Todos = 2,
}

/// <summary>Agente de WhatsApp, Fase 3 — seção <c>Ai:Agendamento</c> (<c>Ai__Agendamento__Modo</c>, ...).</summary>
public sealed class OpcoesAgendamentoAgente
{
    public ModoAgendamentoAgente Modo { get; set; } = ModoAgendamentoAgente.Desligado;

    /// <summary>GUIDs separados por vírgula. Em <c>Piloto</c>, vazio = NENHUM negócio (fail-closed).</summary>
    public string? Negocios { get; set; }

    public int LimitePreparosPorTelefonePorDia { get; set; } = 5;

    public int LimiteFuturosPorTelefone { get; set; } = 3;

    public int LimiteConfirmacoesPorTelefonePorDia { get; set; } = 3;

    public int LimiteReservasAtivasPorNegocio { get; set; } = 20;

    /// <summary>Só vale com <c>Ai__Provider=Gemini</c> (a checagem do provedor é de quem chama).</summary>
    public bool Habilitado(Guid negocioId) => Modo switch
    {
        ModoAgendamentoAgente.Todos => true,
        ModoAgendamentoAgente.Piloto => ListaDeNegocios(Negocios).Contains(negocioId),
        _ => false,
    };

    internal static IReadOnlyCollection<Guid> ListaDeNegocios(string? texto) =>
        string.IsNullOrWhiteSpace(texto)
            ? []
            : texto.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(item => Guid.TryParse(item, out var id) ? id : Guid.Empty)
                .Where(id => id != Guid.Empty)
                .ToList();
}

public sealed class OpcoesGemini
{
    public string? ApiKey { get; set; }

    /// <summary>Nome do modelo; confira na documentação do Google AI Studio antes de ativar (modelos são descontinuados).</summary>
    public string Model { get; set; } = "gemini-3.1-flash-lite";

    /// <summary>
    /// Modelo de reserva (<c>Ai__Gemini__ModeloReserva</c>). Vazio = desligado. Usado só quando o principal falha por capacidade
    /// (503/429/404/5xx, timeout, rede) — nunca em 400/401/403.
    /// </summary>
    public string? ModeloReserva { get; set; }

    public bool TemModeloReserva => !string.IsNullOrWhiteSpace(ModeloReserva);

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
            if (o.Gemini.TemModeloReserva)
            {
                var reserva = o.Gemini.ModeloReserva!.Trim();
                if (!System.Text.RegularExpressions.Regex.IsMatch(reserva, "^[A-Za-z0-9._-]+$"))
                    erros.Add("Ai__Gemini__ModeloReserva deve ser só o nome do modelo (letras, números, ponto, hífen e sublinhado).");
                else if (string.Equals(reserva, o.Gemini.Model?.Trim(), StringComparison.OrdinalIgnoreCase))
                    erros.Add("Ai__Gemini__ModeloReserva deve ser diferente de Ai__Gemini__Model (reserva igual ao principal não protege de nada).");
            }
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
        Faixa(erros, nameof(o.AvisoAutomatizadoDias), o.AvisoAutomatizadoDias, 1, 365);
        if (!Enum.IsDefined(o.Agendamento.Modo))
            erros.Add("Ai__Agendamento__Modo deve ser Desligado, Piloto ou Todos.");
        Faixa(erros, "Agendamento.LimitePreparosPorTelefonePorDia", o.Agendamento.LimitePreparosPorTelefonePorDia, 1, 100);
        Faixa(erros, "Agendamento.LimiteFuturosPorTelefone", o.Agendamento.LimiteFuturosPorTelefone, 1, 50);
        Faixa(erros, "Agendamento.LimiteConfirmacoesPorTelefonePorDia", o.Agendamento.LimiteConfirmacoesPorTelefonePorDia, 1, 50);
        Faixa(erros, "Agendamento.LimiteReservasAtivasPorNegocio", o.Agendamento.LimiteReservasAtivasPorNegocio, 1, 1000);
        if (!string.IsNullOrWhiteSpace(o.Agendamento.Negocios)
            && o.Agendamento.Negocios.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(item => !Guid.TryParse(item, out _)))
            erros.Add("Ai__Agendamento__Negocios deve ser uma lista de GUIDs separada por vírgula.");
        Faixa(erros, nameof(o.RetencaoRodadasDias), o.RetencaoRodadasDias, 7, 365);
        Faixa(erros, "Indisponivel.IntervaloHoras", o.Indisponivel.IntervaloHoras, 1, 168);
        Faixa(erros, "Indisponivel.LimitePorNegocioPorDia", o.Indisponivel.LimitePorNegocioPorDia, 1, 10_000);
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
