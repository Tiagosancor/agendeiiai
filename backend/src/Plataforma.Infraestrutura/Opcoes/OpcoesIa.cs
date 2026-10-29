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

    /// <summary>
    /// Dias que as mensagens de <c>mensagens_whatsapp</c> são mantidas; o job diário <c>limpar-conversas-whatsapp</c> apaga as mais velhas.
    /// 0 (padrão) = desligado: apagar é irreversível e só liga com decisão do dono (recomendado ao ligar: 180). Faixa: 0 ou 30–730.
    /// </summary>
    public int RetencaoMensagensDias { get; set; }

    /// <summary>Mensagem fixa "atendimento automático indisponível" (negócio sem o serviço de IA contratado, agente ligado na tela).</summary>
    public OpcoesIndisponivelAgente Indisponivel { get; set; } = new();

    /// <summary>Fase 4: atendimento humano (ferramenta <c>chamar_atendente</c>, devolução automática, avisos ao dono).</summary>
    public OpcoesHumanoAgente Humano { get; set; } = new();

    /// <summary>Fase 4: gestão dos agendamentos do cliente pelo agente (por ora só o link). Desligada por padrão.</summary>
    public OpcoesGestaoAgente Gestao { get; set; } = new();

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

/// <summary>Seção <c>Ai:Humano</c> (<c>Ai__Humano__FerramentaAtiva</c>, ...). Assumir pelo painel não depende de nada disto.</summary>
public sealed class OpcoesHumanoAgente
{
    /// <summary>Oferece ao modelo a ferramenta <c>chamar_atendente</c>. Desligada por padrão.</summary>
    public bool FerramentaAtiva { get; set; }

    /// <summary>
    /// Horas sem mensagem do cliente até a próxima mensagem devolver a conversa ao agente (preguiçoso, sem job). 0 = nunca devolve
    /// sozinha. O prazo é renovado a cada mensagem do cliente. Faixa: 0 ou 1–168.
    /// </summary>
    public int DevolverAposHoras { get; set; } = 24;

    /// <summary>No máximo um e-mail ao atendente por conversa neste intervalo (1–72 h).</summary>
    public int IntervaloAvisoPorConversaHoras { get; set; } = 6;

    /// <summary>Teto de conversas avisadas por e-mail por negócio por dia UTC (1–500). Acima disso a conversa vai a Humano sem e-mail.</summary>
    public int LimiteAvisosPorNegocioPorDia { get; set; } = 20;

    /// <summary>Prazo da devolução automática; nulo = nunca.</summary>
    public TimeSpan? PrazoDevolucao => DevolverAposHoras > 0 ? TimeSpan.FromHours(DevolverAposHoras) : null;
}

/// <summary>
/// Quanto o agente pode fazer com os agendamentos que o cliente já tem (<c>Ai__Gestao__Modo</c>). Cada degrau inclui o anterior.
/// <see cref="Link"/> e <see cref="Cancelar"/> têm efeito; <c>Completo</c> é aceito na validação e vale como <c>Cancelar</c> até a Fatia 8.
/// </summary>
public enum ModoGestaoAgente
{
    Desligado = 0,

    /// <summary>Lista os agendamentos do número e envia o link de gestão (a página que já existe).</summary>
    Link = 1,

    /// <summary>Além do link: prepara o cancelamento (duas etapas, o servidor efetiva depois do "SIM").</summary>
    Cancelar = 2,

    Completo = 3,
}

/// <summary>Seção <c>Ai:Gestao</c> (<c>Ai__Gestao__Modo</c> e <c>Ai__Gestao__Limite*</c>).</summary>
public sealed class OpcoesGestaoAgente
{
    public ModoGestaoAgente Modo { get; set; } = ModoGestaoAgente.Desligado;

    /// <summary>Links de gestão que o servidor envia por telefone (conversa) por dia UTC (1–100).</summary>
    public int LimiteLinksPorTelefonePorDia { get; set; } = 3;

    /// <summary>Pedidos de gestão preparados (pendências criadas, cancelar e remarcar) por telefone por dia UTC (1–100).</summary>
    public int LimitePreparosPorTelefonePorDia { get; set; } = 6;

    /// <summary>Cancelamentos efetivados pelo agente por telefone por dia UTC (1–100).</summary>
    public int LimiteCancelamentosPorTelefonePorDia { get; set; } = 2;

    /// <summary>Remarcações efetivadas pelo agente por telefone por dia UTC (1–100). Consumido pela Fatia 8 (remarcar).</summary>
    public int LimiteRemarcacoesPorTelefonePorDia { get; set; } = 2;

    /// <summary>Remarcações efetivadas pelo agente no total para o mesmo agendamento (1–100). Consumido pela Fatia 8 (remarcar).</summary>
    public int LimiteRemarcacoesPorAgendamento { get; set; } = 2;

    /// <summary>Operações efetivadas (cancelar + remarcar) pelo agente por negócio por dia UTC (1–1000).</summary>
    public int LimiteOperacoesPorNegocioPorDia { get; set; } = 30;

    /// <summary>
    /// O que o agente de fato faz neste momento: <c>Completo</c> ainda não tem ferramenta de remarcar (Fatia 8), então vale como
    /// <c>Cancelar</c>. Fonte única para a política, o status do painel e o executor de ferramentas.
    /// </summary>
    public ModoGestaoAgente ModoEfetivo => Modo switch
    {
        ModoGestaoAgente.Desligado => ModoGestaoAgente.Desligado,
        ModoGestaoAgente.Link => ModoGestaoAgente.Link,
        _ => ModoGestaoAgente.Cancelar,
    };
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
        if (!Enum.IsDefined(o.Gestao.Modo))
            erros.Add("Ai__Gestao__Modo deve ser Desligado, Link, Cancelar ou Completo.");
        Faixa(erros, "Gestao.LimiteLinksPorTelefonePorDia", o.Gestao.LimiteLinksPorTelefonePorDia, 1, 100);
        Faixa(erros, "Gestao.LimitePreparosPorTelefonePorDia", o.Gestao.LimitePreparosPorTelefonePorDia, 1, 100);
        Faixa(erros, "Gestao.LimiteCancelamentosPorTelefonePorDia", o.Gestao.LimiteCancelamentosPorTelefonePorDia, 1, 100);
        Faixa(erros, "Gestao.LimiteRemarcacoesPorTelefonePorDia", o.Gestao.LimiteRemarcacoesPorTelefonePorDia, 1, 100);
        Faixa(erros, "Gestao.LimiteRemarcacoesPorAgendamento", o.Gestao.LimiteRemarcacoesPorAgendamento, 1, 100);
        Faixa(erros, "Gestao.LimiteOperacoesPorNegocioPorDia", o.Gestao.LimiteOperacoesPorNegocioPorDia, 1, 1000);
        Faixa(erros, nameof(o.RetencaoRodadasDias), o.RetencaoRodadasDias, 7, 365);
        if (o.RetencaoMensagensDias != 0)
            Faixa(erros, nameof(o.RetencaoMensagensDias), o.RetencaoMensagensDias, 30, 730);
        Faixa(erros, "Indisponivel.IntervaloHoras", o.Indisponivel.IntervaloHoras, 1, 168);
        Faixa(erros, "Indisponivel.LimitePorNegocioPorDia", o.Indisponivel.LimitePorNegocioPorDia, 1, 10_000);
        if (o.Humano.DevolverAposHoras != 0)
            Faixa(erros, "Humano.DevolverAposHoras", o.Humano.DevolverAposHoras, 1, 168);
        Faixa(erros, "Humano.IntervaloAvisoPorConversaHoras", o.Humano.IntervaloAvisoPorConversaHoras, 1, 72);
        Faixa(erros, "Humano.LimiteAvisosPorNegocioPorDia", o.Humano.LimiteAvisosPorNegocioPorDia, 1, 500);
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
