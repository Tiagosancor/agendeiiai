using System.Text.RegularExpressions;

namespace Plataforma.Dominio.Negocios;

/// <summary>
/// Regras da configuração do lembrete de retorno (retenção, não marketing): "faz N dias desde o seu último atendimento",
/// com o link público do negócio. Validação única, usada pela API (erros por campo) e pelo próprio <see cref="Negocio"/>.
/// </summary>
public static partial class LembreteRetorno
{
    public const int DiasPadrao = 30;

    public const int DiasMinimo = 1;

    /// <summary>Teto de sanidade: mais de um ano depois já não é "retorno".</summary>
    public const int DiasMaximo = 365;

    public const int TamanhoMaximoMensagem = 1000;

    public static readonly TimeOnly HorarioPadrao = new(10, 0);

    public const string MensagemPadrao =
        "Olá, {nome_cliente}! Já faz {dias} dias desde seu último atendimento na {nome_empresa}. Que tal agendar o próximo? {link_agendamento}";

    public const string VariavelNomeCliente = "nome_cliente";
    public const string VariavelNomeEmpresa = "nome_empresa";
    public const string VariavelDias = "dias";
    public const string VariavelLinkAgendamento = "link_agendamento";

    public static readonly IReadOnlyList<string> Variaveis =
        [VariavelNomeCliente, VariavelNomeEmpresa, VariavelDias, VariavelLinkAgendamento];

    /// <summary>Erros por campo (<c>dias</c>, <c>horario</c>, <c>mensagem</c>); vazio = válido.</summary>
    public static IReadOnlyDictionary<string, string[]> Validar(int? dias, TimeOnly? horario, string? mensagem)
    {
        var erros = new Dictionary<string, string[]>();

        if (dias is null)
            erros["dias"] = ["Informe a quantidade de dias."];
        else if (dias < DiasMinimo || dias > DiasMaximo)
            erros["dias"] = [$"A quantidade de dias precisa estar entre {DiasMinimo} e {DiasMaximo}."];

        if (horario is null)
            erros["horario"] = ["Informe um horário válido no formato HH:mm."];

        if (string.IsNullOrWhiteSpace(mensagem))
            erros["mensagem"] = ["A mensagem não pode ficar vazia."];
        else if (mensagem.Trim().Length > TamanhoMaximoMensagem)
            erros["mensagem"] = [$"A mensagem pode ter até {TamanhoMaximoMensagem} caracteres."];
        else if (VariaveisDesconhecidas(mensagem) is { Count: > 0 } desconhecidas)
            erros["mensagem"] =
            [
                $"Variável desconhecida: {string.Join(", ", desconhecidas.Select(v => "{" + v + "}"))}. " +
                $"Use apenas {string.Join(", ", Variaveis.Select(v => "{" + v + "}"))}.",
            ];

        return erros;
    }

    /// <summary>Tudo o que aparece entre chaves e não é uma das <see cref="Variaveis"/> (inclusive <c>{}</c> e <c>{ dias }</c>).</summary>
    public static IReadOnlyList<string> VariaveisDesconhecidas(string mensagem) =>
        EntreChaves().Matches(mensagem)
            .Select(m => m.Groups[1].Value)
            .Where(nome => !Variaveis.Contains(nome))
            .Distinct()
            .ToList();

    /// <summary>Substitui as variáveis da mensagem já validada. Texto puro: o WhatsApp não interpreta HTML.</summary>
    public static string Montar(string mensagem, string nomeCliente, string nomeEmpresa, int dias, string linkAgendamento) =>
        EntreChaves().Replace(mensagem, m => m.Groups[1].Value switch
        {
            VariavelNomeCliente => nomeCliente,
            VariavelNomeEmpresa => nomeEmpresa,
            VariavelDias => dias.ToString(System.Globalization.CultureInfo.InvariantCulture),
            VariavelLinkAgendamento => linkAgendamento,
            _ => m.Value,
        });

    [GeneratedRegex(@"\{([^{}]*)\}")]
    private static partial Regex EntreChaves();
}
