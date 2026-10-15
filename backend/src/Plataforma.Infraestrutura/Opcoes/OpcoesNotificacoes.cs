using System.ComponentModel.DataAnnotations;
using Plataforma.Dominio.Notificacoes;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// Outbox transacional de notificações (B7.4-B). Tudo é configuração técnica global (variáveis <c>Notificacoes__*</c>), sem opção por
/// negócio e sem aparecer para o usuário. <b>Rollout:</b> <see cref="OutboxHabilitada"/> liga o despachante (e a limpeza); cada família
/// só passa a gravar na outbox, em vez de enviar direto, com a sua própria chave ligada — por isso <c>OutboxHabilitada=true</c> sozinha
/// não muda nenhum caminho de criação. Desligar uma família só faz os eventos NOVOS voltarem ao envio direto; o despachante continua
/// escoando o que já está na outbox enquanto a chave geral estiver ligada (nada fica órfão nem sai duas vezes).
/// </summary>
public sealed class OpcoesNotificacoes : IValidatableObject
{
    public const string Secao = "Notificacoes";

    /// <summary>Despachante e limpeza ligados. Desligada, nenhuma mensagem da outbox é enviada (pausa operacional).</summary>
    public bool OutboxHabilitada { get; set; }

    /// <summary>Lembrete de 60 minutos pela outbox (exige <see cref="OutboxHabilitada"/>).</summary>
    public bool OutboxLembrete { get; set; }

    /// <summary>Confirmação, cancelamento, avisos ao profissional (remarcação, transferência) pela outbox (exige <see cref="OutboxHabilitada"/>).</summary>
    public bool OutboxOperacionais { get; set; }

    /// <summary>Lembrete de retorno pela outbox (exige <see cref="OutboxHabilitada"/>).</summary>
    public bool OutboxRetorno { get; set; }

    /// <summary>Duração da reivindicação de um item. Vencida, outra instância o recupera.</summary>
    [Range(30, 3600)]
    public int LeaseSegundos { get; set; } = 120;

    /// <summary>Itens reivindicados por execução do despachante (a trava só dura o que a execução levar para processá-los).</summary>
    [Range(1, 500)]
    public int TamanhoLote { get; set; } = 20;

    /// <summary>Teto de tempo de uma execução do despachante antes de parar de reivindicar mais lotes.</summary>
    [Range(5, 600)]
    public int DuracaoMaximaExecucaoSegundos { get; set; } = 50;

    /// <summary>
    /// Lembrete de 60 min: esperas entre as tentativas, em minutos (padrão <c>1,5,15</c> = despacho imediato + até 3 novas tentativas).
    /// Lista separada por vírgula (um array no binder de configuração somaria ao valor padrão).
    /// </summary>
    [RegularExpression(@"^\d{1,4}(,\d{1,4}){0,2}$", ErrorMessage = "Use de 1 a 3 valores inteiros separados por vírgula.")]
    public string LembreteBackoffMinutos { get; set; } = "1,5,15";

    /// <summary>Lembrete de 60 min: se restarem menos que isto para o atendimento, a mensagem é cancelada em vez de enviada.</summary>
    [Range(0, 600)]
    public int LembreteNaoEnviarFaltandoMinutos { get; set; } = 30;

    /// <summary>Operacionais: esperas entre as tentativas (padrão <c>1,5,60,360</c> = no máximo 5 tentativas no total, todas dentro do prazo).</summary>
    [RegularExpression(@"^\d{1,5}(,\d{1,5}){0,3}$", ErrorMessage = "Use de 1 a 4 valores inteiros separados por vírgula.")]
    public string OperacionaisBackoffMinutos { get; set; } = "1,5,60,360";

    /// <summary>Operacionais: depois deste prazo, contado da criação, nenhuma nova tentativa — falha definitiva.</summary>
    [Range(1, 24)]
    public int OperacionaisPrazoHoras { get; set; } = 24;

    /// <summary>Retorno: esperas entre as tentativas (minutos).</summary>
    [RegularExpression(@"^\d{1,4}(,\d{1,4}){0,2}$", ErrorMessage = "Use de 1 a 3 valores inteiros separados por vírgula.")]
    public string RetornoBackoffMinutos { get; set; } = "1,5,15";

    /// <summary>Retorno: validade da mensagem desde que entrou na outbox (o dia dela já passou depois disso).</summary>
    [Range(15, 1440)]
    public int RetornoValidadeMinutos { get; set; } = 180;

    /// <summary>Itens enviados, cancelados, desconhecidos e falhos são apagados depois deste prazo (nunca os pendentes ou em envio).</summary>
    [Range(1, 3650)]
    public int RetencaoDias { get; set; } = 30;

    public bool UsaOutbox(FamiliaNotificacao familia) => OutboxHabilitada && familia switch
    {
        FamiliaNotificacao.Lembrete60 => OutboxLembrete,
        FamiliaNotificacao.Operacional => OutboxOperacionais,
        FamiliaNotificacao.Retorno => OutboxRetorno,
        _ => false,
    };

    /// <summary>Esperas (após a 1ª, 2ª… tentativa) da família. A quantidade de tentativas é <c>esperas + 1</c>.</summary>
    public IReadOnlyList<TimeSpan> Esperas(FamiliaNotificacao familia) => Converter(familia switch
    {
        FamiliaNotificacao.Lembrete60 => LembreteBackoffMinutos,
        FamiliaNotificacao.Operacional => OperacionaisBackoffMinutos,
        _ => RetornoBackoffMinutos,
    });

    public int MaximoDeTentativas(FamiliaNotificacao familia) => Esperas(familia).Count + 1;

    /// <summary>Prazo final de um item criado em <paramref name="agora"/> (lembrete: calculado pelo chamador a partir do atendimento).</summary>
    public DateTimeOffset PrazoDeCriacao(FamiliaNotificacao familia, DateTimeOffset agora) => familia switch
    {
        FamiliaNotificacao.Operacional => agora.AddHours(OperacionaisPrazoHoras),
        FamiliaNotificacao.Retorno => agora.AddMinutes(RetornoValidadeMinutos),
        _ => throw new ArgumentOutOfRangeException(nameof(familia), "O prazo do lembrete depende do horário do atendimento."),
    };

    private static List<TimeSpan> Converter(string lista) =>
        lista.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => TimeSpan.FromMinutes(int.Parse(v, System.Globalization.CultureInfo.InvariantCulture)))
            .ToList();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var (nome, valor) in new[]
        {
            (nameof(LembreteBackoffMinutos), LembreteBackoffMinutos),
            (nameof(OperacionaisBackoffMinutos), OperacionaisBackoffMinutos),
            (nameof(RetornoBackoffMinutos), RetornoBackoffMinutos),
        })
        {
            // O regex de cada propriedade só roda se o valor existir; aqui, as regras que ele não cobre.
            var esperas = valor?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
            if (esperas.Length == 0 || esperas.Any(e => !int.TryParse(e, out var minutos) || minutos < 1))
                yield return new ValidationResult($"{nome}: cada espera precisa ser um inteiro de pelo menos 1 minuto.", [nome]);
        }

        // Operacionais: no máximo 5 tentativas no total (4 esperas) e a soma das esperas cabe no prazo.
        if (ListaValida(OperacionaisBackoffMinutos) && Esperas(FamiliaNotificacao.Operacional).Sum(e => e.TotalMinutes) >= OperacionaisPrazoHoras * 60)
            yield return new ValidationResult(
                "As esperas das notificações operacionais precisam caber dentro do prazo de OperacionaisPrazoHoras.",
                [nameof(OperacionaisBackoffMinutos), nameof(OperacionaisPrazoHoras)]);
    }

    private static bool ListaValida(string? valor) =>
        !string.IsNullOrWhiteSpace(valor) && valor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(e => int.TryParse(e, out var minutos) && minutos >= 1);
}
