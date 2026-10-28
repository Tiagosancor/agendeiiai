using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

/// <summary>Erro de ferramenta que volta ao modelo como resultado (<c>{ erro, mensagem }</c>) — nunca exceção, nunca detalhe interno.</summary>
public static class ErrosFerramenta
{
    public const string ArgumentosInvalidos = "argumentos_invalidos";
    public const string ServicoDesconhecido = "servico_desconhecido";
    public const string ProfissionalDesconhecido = "profissional_desconhecido";
    public const string ProfissionalNaoFazServico = "profissional_nao_faz_servico";
    public const string DataInvalida = "data_invalida";
    public const string DataForaDoPeriodo = "data_fora_do_periodo";
    public const string LimiteChamadas = "limite_chamadas";
    public const string FerramentaDesconhecida = "ferramenta_desconhecida";
    public const string ErroInterno = "erro_interno";

    // Fase 3 (preparar_agendamento).
    public const string HoraInvalida = "hora_invalida";
    public const string HorarioIndisponivel = "horario_indisponivel";
    public const string NomeNecessario = "nome_necessario";
    public const string NomeInvalido = "nome_invalido";
    public const string LimiteAgendamentos = "limite_agendamentos";
    public const string AgendamentoIndisponivel = "agendamento_indisponivel";
    public const string ResumoJaEnviado = "resumo_ja_enviado";

    // Fase 4 (chamar_atendente).
    public const string AtendenteIndisponivel = "atendente_indisponivel";

    public static string Mensagem(string codigo) => codigo switch
    {
        ArgumentosInvalidos => "Argumentos inválidos para esta ferramenta.",
        ServicoDesconhecido => "Serviço não encontrado neste estabelecimento.",
        ProfissionalDesconhecido => "Profissional não encontrado neste estabelecimento.",
        ProfissionalNaoFazServico => "Este profissional não realiza este serviço.",
        DataInvalida => "Data inválida. Use o formato AAAA-MM-DD.",
        DataForaDoPeriodo => "Data fora do período que pode ser consultada.",
        LimiteChamadas => "Limite de chamadas de ferramenta atingido nesta resposta.",
        FerramentaDesconhecida => "Ferramenta inexistente.",
        HoraInvalida => "Hora inválida. Use o formato HH:mm (24 horas).",
        HorarioIndisponivel => "Esse horário não está livre. Ofereça ao cliente um dos horários alternativos, se houver.",
        NomeNecessario => "Peça ao cliente o nome completo e chame a ferramenta de novo com nome_cliente.",
        NomeInvalido => "Esse nome não pode ser usado. Peça ao cliente o nome completo, só com letras.",
        LimiteAgendamentos => "Limite de agendamentos pelo WhatsApp atingido. Envie ao cliente o link para agendar.",
        AgendamentoIndisponivel => "Não é possível agendar por aqui agora. Envie ao cliente o link para agendar.",
        ResumoJaEnviado => "O resumo desse agendamento já foi enviado. Peça ao cliente que responda SIM para confirmar.",
        AtendenteIndisponivel => "Não foi possível chamar uma pessoa agora. Envie ao cliente o link para agendar.",
        _ => "Não foi possível consultar agora.",
    };
}

public static class RespostaFerramenta
{
    private static readonly JsonSerializerOptions Opcoes = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = null,
    };

    public static JsonElement De<T>(T valor) => JsonSerializer.SerializeToElement(valor, Opcoes);

    public static JsonElement Erro(string codigo) => De(new { erro = codigo, mensagem = ErrosFerramenta.Mensagem(codigo) });

    /// <summary>Erro com a lista de horários alternativos (<c>HH:mm</c>, até 5, mesmo dia e profissional).</summary>
    public static JsonElement ErroComAlternativas(string codigo, IReadOnlyList<string> alternativas) =>
        De(new { erro = codigo, mensagem = ErrosFerramenta.Mensagem(codigo), alternativas });

    public static JsonElement Esquema(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return documento.RootElement.Clone();
    }

    /// <summary>Texto de cadastro do tenant como DADO: sem caracteres de controle e com tamanho limitado.</summary>
    public static string Limpar(string? texto, int maximo = 100)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        var limpo = new string(texto.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return limpo.Length > maximo ? limpo[..maximo] : limpo;
    }
}

/// <summary>Leitura estrita dos argumentos do modelo: só campos conhecidos, tipos exatos, sem desserializar para entidade.</summary>
public sealed class LeitorArgumentos
{
    private readonly JsonElement _argumentos;
    private readonly HashSet<string> _permitidos;

    public bool Valido { get; private set; } = true;

    public LeitorArgumentos(JsonElement argumentos, params string[] permitidos)
    {
        _argumentos = argumentos;
        _permitidos = [.. permitidos];

        if (argumentos.ValueKind == JsonValueKind.Undefined || argumentos.ValueKind == JsonValueKind.Null)
        {
            _argumentos = default;
            return;
        }

        if (argumentos.ValueKind != JsonValueKind.Object)
        {
            Valido = false;
            return;
        }

        foreach (var propriedade in argumentos.EnumerateObject())
        {
            if (!_permitidos.Contains(propriedade.Name))
                Valido = false;
        }
    }

    /// <summary>Texto de até 100 caracteres; ausente/nulo = <c>null</c> (obrigatoriedade é de quem chama). Tipo errado invalida.</summary>
    public string? Texto(string nome)
    {
        if (_argumentos.ValueKind != JsonValueKind.Object || !_argumentos.TryGetProperty(nome, out var valor) || valor.ValueKind == JsonValueKind.Null)
            return null;

        if (valor.ValueKind != JsonValueKind.String)
        {
            Valido = false;
            return null;
        }

        var texto = valor.GetString()!;
        if (texto.Length > 100)
        {
            Valido = false;
            return null;
        }

        return texto.Trim();
    }

    public System.Guid? Identificador(string nome)
    {
        var texto = Texto(nome);
        if (texto is null)
            return null;

        if (texto.Length != 36 || !System.Guid.TryParseExact(texto, "D", out var id))
        {
            Valido = false;
            return null;
        }

        return id;
    }

    /// <summary>Data exata <c>AAAA-MM-DD</c>. Formato inválido devolve <c>null</c> e marca <see cref="DataMalFormada"/>.</summary>
    public DateOnly? Data(string nome)
    {
        var texto = Texto(nome);
        if (texto is null)
            return null;

        if (!DateOnly.TryParseExact(texto, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var data))
        {
            DataMalFormada = true;
            return null;
        }

        return data;
    }

    public bool DataMalFormada { get; private set; }
}
