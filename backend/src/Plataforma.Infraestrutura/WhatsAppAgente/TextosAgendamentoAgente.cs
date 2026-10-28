using System.Globalization;
using Plataforma.Infraestrutura.Comum;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>O que os textos do servidor precisam saber de um agendamento. Nunca nome, e-mail ou telefone do cliente.</summary>
public sealed record DadosDoAgendamentoParaTexto(
    string NomeNegocio, string NomeServico, decimal Valor, int DuracaoMinutos, string NomeProfissional, DateTimeOffset Inicio, string Fuso);

/// <summary>
/// Textos montados pelo SERVIDOR no agendamento pelo WhatsApp (resumo, confirmação, avisos). O modelo nunca escreve nenhum deles.
/// Cada fragmento de cadastro livre (nome do negócio, do serviço, do profissional, nome digitado) é sanitizado SOZINHO e o texto
/// só é montado depois — assim o filtro de telefone (8+ dígitos) nunca toca o token do link de gestão. Puro.
/// </summary>
public sealed class TextosAgendamentoAgente
{
    public const string AgendamentoNaoConfirmado =
        "Tudo bem, não marquei nada. Se quiser agendar outro horário, é só me dizer.";

    public const string PrazoPassou =
        "O prazo para confirmar terminou e o horário foi liberado. Se ainda quiser, me diga o serviço, o dia e o horário de novo.";

    public const string HorarioIndisponivelNaConfirmacao =
        "Não consegui confirmar: esse horário não está mais disponível. Me diga outro dia ou horário que eu verifico.";

    /// <summary>
    /// Mensagem fixa de um negócio sem o serviço de IA contratado (e com o agente ligado na tela). Sem nome de produto, sem prometer
    /// resposta humana e sem falar de pagamento. O chamador ainda a passa pelo <c>SanitizarTextoFixo</c> (o nome é cadastro livre).
    /// </summary>
    public static string AtendimentoIndisponivel(string nomeNegocio, string link) =>
        $"Olá! O atendimento automático de {ConstrutorPromptAgente.LimparNome(nomeNegocio)} não está disponível no momento. " +
        $"Para ver horários e agendar, acesse: {link}";

    /// <summary>Texto gravado no lugar do link de gestão: o token nunca é persistido nem vai à IA.</summary>
    public const string LinkDeGestaoOculto = "[link de gestão enviado]";

    private readonly string _hostPermitido;
    private readonly string? _telefoneInstancia;
    private readonly string _linkPublico;

    /// <param name="linkPublico">Link público do negócio (sem barra final).</param>
    /// <param name="telefoneInstancia">Telefone conectado da instância (o único número que pode aparecer).</param>
    public TextosAgendamentoAgente(string linkPublico, string? telefoneInstancia)
    {
        _linkPublico = linkPublico.TrimEnd('/');
        _hostPermitido = Uri.TryCreate(_linkPublico, UriKind.Absolute, out var uri) ? uri.Authority : _linkPublico;
        _telefoneInstancia = telefoneInstancia;
    }

    public string LinkPrivacidade => _linkPublico + "/privacidade";

    private string Fragmento(string? texto, string padrao, int maximo = 100)
    {
        var limpo = SanitizadorRespostaAgente.SanitizarFragmento(texto, _hostPermitido, _telefoneInstancia, maximo);
        return limpo.Length == 0 ? padrao : limpo;
    }

    private static void DiaEHora(DateTimeOffset inicio, string fuso, out string dia, out string hora)
    {
        var local = TimeZoneInfo.ConvertTime(inicio, TimeZoneInfo.FindSystemTimeZoneById(fuso));
        dia = $"{ConstrutorPromptAgente.NomeDoDia(local.DayOfWeek)}, {local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";
        hora = local.ToString("HH:mm", CultureInfo.InvariantCulture);
    }

    /// <summary>Resumo que pede o "SIM". O nome só aparece quando é o que o cliente novo digitou (nunca o do cadastro).</summary>
    public string Resumo(DadosDoAgendamentoParaTexto dados, string? nomeDigitado, int minutosDeValidade)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var negocio = Fragmento(dados.NomeNegocio, "o estabelecimento");
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        var linhas = new List<string>
        {
            $"Confira o seu agendamento em {negocio}:",
            $"- Serviço: {servico} ({FormatacaoBrasil.Reais(dados.Valor)}, {dados.DuracaoMinutos} min)",
            $"- Profissional: {profissional}",
            $"- Quando: {dia} às {hora}",
        };
        if (!string.IsNullOrWhiteSpace(nomeDigitado))
            linhas.Add($"- Nome: {Fragmento(nomeDigitado, "—", 80)}");

        linhas.Add(string.Empty);
        linhas.Add($"Para confirmar, responda SIM. Para desistir, responda NÃO. O horário fica reservado por {minutosDeValidade} minutos.");
        linhas.Add($"Ao responder SIM, você confirma o agendamento e concorda com a política de privacidade de {negocio}: {LinkPrivacidade}");
        return string.Join('\n', linhas);
    }

    /// <summary>Confirmação enviada depois do commit. Com <paramref name="linkDeGestao"/> nulo sai o marcador que vai ao banco.</summary>
    public string Confirmacao(DadosDoAgendamentoParaTexto dados, string? linkDeGestao)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var negocio = Fragmento(dados.NomeNegocio, "o estabelecimento");
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return $"Agendamento confirmado em {negocio}: {servico} com {profissional}, {dia} às {hora}. " +
               $"Para cancelar ou remarcar: {linkDeGestao ?? LinkDeGestaoOculto}";
    }

    /// <summary>Aviso fixo de atendimento automatizado (uma vez por conversa a cada N dias).</summary>
    public string AvisoAutomatizado(string nomeNegocio) =>
        $"Olá! Aqui é o assistente virtual (atendimento automatizado) de {Fragmento(nomeNegocio, "nosso estabelecimento")}. " +
        $"As mensagens são processadas por um sistema de inteligência artificial. Política de privacidade: {LinkPrivacidade}";

    /// <summary>Horário perdido na confirmação, com as alternativas que o servidor achou (no fuso do negócio).</summary>
    public string HorarioIndisponivelComAlternativas(IReadOnlyList<DateTimeOffset>? alternativas, string fuso)
    {
        if (alternativas is not { Count: > 0 })
            return HorarioIndisponivelNaConfirmacao;

        var lista = string.Join(", ", alternativas.Take(5).Select(a => FormatacaoBrasil.DataHora(a, fuso)));
        return $"{HorarioIndisponivelNaConfirmacao} Horários livres perto desse: {lista}.";
    }

    /// <summary>Quando nada mais pode ser feito por aqui: leva o cliente ao link público.</summary>
    public string NaoConsegui() =>
        $"Não consegui concluir o agendamento por aqui. Para agendar, acesse: {_linkPublico}";
}
