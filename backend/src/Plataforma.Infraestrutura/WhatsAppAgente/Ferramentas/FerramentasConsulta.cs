using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Publico;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente.Ferramentas;

// Ferramentas de CONSULTA do agente (Fase 2). Só leitura de dado público do próprio negócio; o negócio vem do filtro global do
// EF (IContextoNegocio), nunca de argumento. Os DTOs de saída são records com lista FECHADA de campos: campo novo aqui é
// decisão consciente (há teste de snapshot das chaves). Nada de foto, custo, e-mail, telefone ou quem ocupa um horário.

public sealed record ServicoDaFerramenta(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("categoria")] string Categoria,
    [property: JsonPropertyName("preco")] string Preco,
    [property: JsonPropertyName("duracao_minutos")] int DuracaoMinutos);

public sealed record ProfissionalDaFerramenta(
    [property: JsonPropertyName("id")] Guid Id,
    [property: JsonPropertyName("nome")] string Nome,
    [property: JsonPropertyName("funcao")] string? Funcao,
    [property: JsonPropertyName("preco"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Preco,
    [property: JsonPropertyName("duracao_minutos"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? DuracaoMinutos);

public sealed record HorarioDaFerramenta(
    [property: JsonPropertyName("hora")] string Hora,
    [property: JsonPropertyName("profissional_id")] Guid ProfissionalId,
    [property: JsonPropertyName("profissional")] string Profissional);

public sealed record HorariosDaFerramenta(
    [property: JsonPropertyName("data")] string Data,
    [property: JsonPropertyName("dia_semana")] string DiaSemana,
    [property: JsonPropertyName("horarios")] IReadOnlyList<HorarioDaFerramenta> Horarios,
    [property: JsonPropertyName("truncado")] bool Truncado);

public sealed class FerramentaListarServicos : IFerramentaAgente
{
    private const int Maximo = 100;
    private readonly IConsultaCatalogoPublico _catalogo;

    public FerramentaListarServicos(IConsultaCatalogoPublico catalogo) => _catalogo = catalogo;

    public string Nome => "listar_servicos";

    public string Descricao => "Lista os serviços do estabelecimento com preço e duração. Não recebe parâmetros.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema("""{"type":"object","properties":{},"additionalProperties":false}""");

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        if (!new LeitorArgumentos(argumentos).Valido)
            return RespostaFerramenta.Erro(ErrosFerramenta.ArgumentosInvalidos);

        var categorias = await _catalogo.ListarServicosAsync(cancellationToken);
        var servicos = categorias
            .SelectMany(c => c.Servicos.Select(s => new ServicoDaFerramenta(
                s.Id, RespostaFerramenta.Limpar(s.Nome), RespostaFerramenta.Limpar(c.Nome), FormatacaoBrasil.Reais(s.Preco), s.DuracaoMinutos)))
            .Take(Maximo)
            .ToList();

        return RespostaFerramenta.De(servicos);
    }
}

public sealed class FerramentaListarProfissionais : IFerramentaAgente
{
    private readonly IConsultaCatalogoPublico _catalogo;

    public FerramentaListarProfissionais(IConsultaCatalogoPublico catalogo) => _catalogo = catalogo;

    public string Nome => "listar_profissionais";

    public string Descricao =>
        "Lista os profissionais do estabelecimento. Com servico_id, só quem realiza aquele serviço, com o preço e a duração de cada um.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """{"type":"object","properties":{"servico_id":{"type":"string","description":"UUID do serviço, obtido em listar_servicos"}},"additionalProperties":false}""");

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        var leitor = new LeitorArgumentos(argumentos, "servico_id");
        var servicoId = leitor.Identificador("servico_id");
        if (!leitor.Valido)
            return RespostaFerramenta.Erro(ErrosFerramenta.ArgumentosInvalidos);

        var profissionais = await _catalogo.ListarProfissionaisAsync(cancellationToken);

        if (servicoId is null)
        {
            return RespostaFerramenta.De(profissionais
                .Select(p => new ProfissionalDaFerramenta(p.Id, RespostaFerramenta.Limpar(p.Nome), Funcao(p), null, null))
                .ToList());
        }

        var servicos = await _catalogo.ListarServicosAsync(cancellationToken);
        if (!servicos.Any(c => c.Servicos.Any(s => s.Id == servicoId)))
            return RespostaFerramenta.Erro(ErrosFerramenta.ServicoDesconhecido);

        var lista = new List<ProfissionalDaFerramenta>();
        foreach (var profissional in profissionais)
        {
            var dele = profissional.Servicos?.FirstOrDefault(s => s.ServicoId == servicoId);
            if (dele is null)
                continue;

            lista.Add(new ProfissionalDaFerramenta(
                profissional.Id, RespostaFerramenta.Limpar(profissional.Nome), Funcao(profissional),
                FormatacaoBrasil.Reais(dele.Preco), dele.DuracaoMinutos));
        }

        return RespostaFerramenta.De(lista);
    }

    private static string? Funcao(ProfissionalPublico p) =>
        string.IsNullOrWhiteSpace(p.Funcao) ? null : RespostaFerramenta.Limpar(p.Funcao);
}

public sealed class FerramentaConsultarHorarios : IFerramentaAgente
{
    private const int MaximoHorarios = 30;
    private static readonly string[] DiasDaSemana = ["domingo", "segunda-feira", "terça-feira", "quarta-feira", "quinta-feira", "sexta-feira", "sábado"];

    private readonly IConsultaCatalogoPublico _catalogo;
    private readonly IConsultaHorariosPublicos _horarios;
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IOptions<OpcoesIa> _opcoes;

    public FerramentaConsultarHorarios(
        IConsultaCatalogoPublico catalogo, IConsultaHorariosPublicos horarios, PlataformaDbContext dbContext,
        IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes)
    {
        _catalogo = catalogo;
        _horarios = horarios;
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes;
    }

    public string Nome => "consultar_horarios";

    public string Descricao =>
        "Consulta os horários livres de um serviço em um dia (um dia por chamada). Sem profissional_id, junta todos que fazem o serviço.";

    public JsonElement Esquema { get; } = RespostaFerramenta.Esquema(
        """
        {"type":"object","properties":{
          "servico_id":{"type":"string","description":"UUID do serviço, obtido em listar_servicos"},
          "profissional_id":{"type":"string","description":"UUID do profissional (opcional), obtido em listar_profissionais"},
          "data":{"type":"string","description":"Dia no formato AAAA-MM-DD"}},
         "required":["servico_id","data"],"additionalProperties":false}
        """);

    public async Task<JsonElement> ExecutarAsync(ContextoConversaAgente contexto, JsonElement argumentos, CancellationToken cancellationToken = default)
    {
        var leitor = new LeitorArgumentos(argumentos, "servico_id", "profissional_id", "data");
        var servicoId = leitor.Identificador("servico_id");
        var profissionalId = leitor.Identificador("profissional_id");
        var data = leitor.Data("data");

        if (!leitor.Valido || servicoId is null || (data is null && !leitor.DataMalFormada))
            return RespostaFerramenta.Erro(ErrosFerramenta.ArgumentosInvalidos);
        if (leitor.DataMalFormada || data is null)
            return RespostaFerramenta.Erro(ErrosFerramenta.DataInvalida);

        var servico = (await _catalogo.ListarServicosAsync(cancellationToken))
            .SelectMany(c => c.Servicos).FirstOrDefault(s => s.Id == servicoId);
        if (servico is null)
            return RespostaFerramenta.Erro(ErrosFerramenta.ServicoDesconhecido);

        var profissionais = await _catalogo.ListarProfissionaisAsync(cancellationToken);
        if (profissionalId is not null)
        {
            var profissional = profissionais.FirstOrDefault(p => p.Id == profissionalId);
            if (profissional is null)
                return RespostaFerramenta.Erro(ErrosFerramenta.ProfissionalDesconhecido);
            if (!profissional.ServicoIds.Contains(servico.Id))
                return RespostaFerramenta.Erro(ErrosFerramenta.ProfissionalNaoFazServico);
        }

        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);
        var hoje = ConversorFusoHorario.ParaLocal(DateTimeOffset.UtcNow, fuso).Dia;
        if (data.Value < hoje || data.Value > hoje.AddDays(_opcoes.Value.DiasMaximosConsulta))
            return RespostaFerramenta.Erro(ErrosFerramenta.DataForaDoPeriodo);

        var livres = await _horarios.ListarAsync(data.Value, servico.DuracaoMinutos, [servico.Id], profissionalId, 500, cancellationToken);

        // Só os horários que caem no dia local pedido (a consulta pública já é por dia; isto protege contra virada de fuso).
        var doDia = livres
            .Select(h => (Horario: h, Local: ConversorFusoHorario.ParaLocal(h.Inicio, fuso)))
            .Where(x => x.Local.Dia == data.Value)
            .ToList();

        var nomes = profissionais.ToDictionary(p => p.Id, p => RespostaFerramenta.Limpar(p.Nome));
        var itens = doDia
            .Take(MaximoHorarios)
            .Select(x => new HorarioDaFerramenta(
                x.Local.Hora.ToString("HH':'mm", CultureInfo.InvariantCulture),
                x.Horario.ProfissionalId, nomes.GetValueOrDefault(x.Horario.ProfissionalId, string.Empty)))
            .ToList();

        return RespostaFerramenta.De(new HorariosDaFerramenta(
            data.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DiasDaSemana[(int)data.Value.DayOfWeek], itens, doDia.Count > MaximoHorarios));
    }
}
