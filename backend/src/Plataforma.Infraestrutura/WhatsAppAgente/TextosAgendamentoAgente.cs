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

    // Gestão (Fase 4, Fatia 7): textos do servidor para o cancelamento. Nunca nome, e-mail ou telefone do cliente.
    public const string CancelamentoNaoEfetivado = "Tudo bem, não cancelei nada. O seu agendamento continua marcado.";

    public const string CancelamentoPrazoPassou =
        "O prazo para confirmar o cancelamento terminou e nada foi cancelado. Se ainda quiser cancelar, me diga de novo.";

    // Remarcação (Fatia 8).
    public const string RemarcacaoNaoEfetivada = "Tudo bem, não remarquei nada. O seu agendamento continua no horário de antes.";

    public const string RemarcacaoPrazoPassou =
        "O prazo para confirmar a remarcação terminou e nada foi alterado. Se ainda quiser remarcar, me diga de novo.";

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

    /// <summary>
    /// Texto do link de gestão (Fase 4, modo Link): o servidor envia, o texto do modelo é descartado. Com <paramref name="linkDeGestao"/> nulo
    /// sai o marcador que vai ao banco (o token nunca é persistido nem vai à IA).
    /// </summary>
    public string LinkGestao(DadosDoAgendamentoParaTexto dados, string? linkDeGestao)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return $"Aqui está o link para ver, cancelar ou remarcar o seu agendamento de {servico} com {profissional}, {dia} às {hora}: " +
               $"{linkDeGestao ?? LinkDeGestaoOculto}";
    }

    /// <summary>O agendamento deixou de poder receber o link entre o pedido do modelo e o envio (cancelado, concluído, telefone trocado...).</summary>
    public string LinkIndisponivel() =>
        $"Não consegui encontrar esse agendamento por aqui. Para agendar, acesse: {_linkPublico}";

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

    /// <summary>Resumo do cancelamento que pede o "SIM" (o servidor o envia; o texto do modelo é descartado).</summary>
    public string ResumoCancelamento(DadosDoAgendamentoParaTexto dados, int minutosDeValidade)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var negocio = Fragmento(dados.NomeNegocio, "o estabelecimento");
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return string.Join('\n',
            $"Confira o cancelamento em {negocio}:",
            $"- Serviço: {servico}",
            $"- Profissional: {profissional}",
            $"- Quando: {dia} às {hora}",
            string.Empty,
            $"Para CANCELAR este agendamento, responda SIM. Para manter, responda NÃO. Este pedido vale por {minutosDeValidade} minutos.");
    }

    /// <summary>O cancelamento foi gravado (o servidor anuncia; o modelo nunca).</summary>
    public string CancelamentoEfetivado(DadosDoAgendamentoParaTexto dados)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return $"Pronto, cancelei o seu agendamento de {servico} com {profissional}, {dia} às {hora}. Para marcar outro horário, é só me dizer ou acessar: {_linkPublico}";
    }

    /// <summary>
    /// O servidor recusou o cancelamento na hora de efetivar. Texto neutro: não revela pagamento, quinzena nem regra interna, só que não
    /// é possível por aqui e que o estabelecimento resolve.
    /// </summary>
    public string CancelamentoRecusado(Plataforma.Aplicacao.Agendamentos.MotivoRecusaGestaoAgente motivo) =>
        motivo == Plataforma.Aplicacao.Agendamentos.MotivoRecusaGestaoAgente.AntecedenciaMinima
            ? "Não consegui cancelar por aqui: o prazo mínimo para cancelar esse horário já passou. Fale com o estabelecimento."
            : "Não consegui cancelar esse agendamento por aqui. Fale com o estabelecimento.";

    /// <summary>Resumo da remarcação que pede o "SIM" (o servidor o envia; o texto do modelo é descartado). Avisa que o horário novo só é garantido no "SIM".</summary>
    public string ResumoRemarcacao(DadosDoAgendamentoParaTexto dados, DateTimeOffset novoInicio, int minutosDeValidade)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var diaAtual, out var horaAtual);
        DiaEHora(novoInicio, dados.Fuso, out var diaNovo, out var horaNova);
        var negocio = Fragmento(dados.NomeNegocio, "o estabelecimento");
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return string.Join('\n',
            $"Confira a remarcação em {negocio}:",
            $"- Serviço: {servico}",
            $"- Profissional: {profissional}",
            $"- De: {diaAtual} às {horaAtual}",
            $"- Para: {diaNovo} às {horaNova}",
            string.Empty,
            $"Para REMARCAR, responda SIM. Para manter o horário de antes, responda NÃO. Este pedido vale por {minutosDeValidade} minutos; o novo horário só fica garantido depois do SIM.");
    }

    /// <summary>A remarcação foi gravada (o servidor anuncia; o modelo nunca). O link de gestão que o cliente já tem continua valendo.</summary>
    public string RemarcacaoEfetivada(DadosDoAgendamentoParaTexto dados)
    {
        DiaEHora(dados.Inicio, dados.Fuso, out var dia, out var hora);
        var servico = Fragmento(dados.NomeServico, "serviço");
        var profissional = Fragmento(dados.NomeProfissional, "profissional");

        return $"Pronto, remarquei o seu agendamento de {servico} com {profissional} para {dia} às {hora}.";
    }

    /// <summary>O servidor recusou a remarcação na hora de efetivar. Texto neutro: não revela regra interna.</summary>
    public string RemarcacaoRecusada(Plataforma.Aplicacao.Agendamentos.MotivoRecusaGestaoAgente motivo) =>
        motivo switch
        {
            Plataforma.Aplicacao.Agendamentos.MotivoRecusaGestaoAgente.AntecedenciaMinima =>
                "Não consegui remarcar por aqui: o prazo mínimo para remarcar esse horário já passou. Fale com o estabelecimento.",
            Plataforma.Aplicacao.Agendamentos.MotivoRecusaGestaoAgente.AgendamentoAlterado =>
                "Esse agendamento foi alterado depois do resumo, então não remarquei nada. Se ainda quiser remarcar, me diga de novo.",
            _ => "Não consegui remarcar esse agendamento por aqui. Fale com o estabelecimento.",
        };

    /// <summary>O novo horário foi tomado (ou saiu da oferta) entre o resumo e o "SIM": nada foi alterado; traz até 5 horários livres do dia.</summary>
    public string RemarcacaoHorarioIndisponivel(IReadOnlyList<DateTimeOffset>? alternativas, string fuso)
    {
        const string basico = "Não consegui remarcar: esse horário não está mais disponível e o seu agendamento continua como estava.";
        if (alternativas is not { Count: > 0 })
            return basico + " Me diga outro dia ou horário que eu verifico.";

        var lista = string.Join(", ", alternativas.Take(5).Select(a => FormatacaoBrasil.DataHora(a, fuso)));
        return $"{basico} Horários livres perto desse: {lista}. Me diga qual prefere.";
    }
}
