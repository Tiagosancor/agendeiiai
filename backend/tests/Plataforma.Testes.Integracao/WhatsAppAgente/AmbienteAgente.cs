using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>Fila que processa na hora, dentro da chamada (testes determinísticos). A fila real tem teste próprio.</summary>
public sealed class FilaImediata : IFilaRespostaAgente
{
    private readonly IServiceScopeFactory _escopos;

    public FilaImediata(IServiceScopeFactory escopos) => _escopos = escopos;

    public async Task<bool> EnfileirarAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        using var escopo = _escopos.CreateScope();
        await escopo.ServiceProvider.GetRequiredService<IProcessadorRodadaAgente>().ProcessarAsync(conversaId, CancellationToken.None);
        return true;
    }
}

/// <summary>Fila que perde tudo (simula o reinício do processo): só a varredura de recuperação acha a mensagem.</summary>
public sealed class FilaQuePerde : IFilaRespostaAgente
{
    public Task<bool> EnfileirarAsync(Guid conversaId, CancellationToken cancellationToken = default) => Task.FromResult(false);
}

/// <summary>
/// "Modelo" roteirizado: o teste decide o que ele responde a cada chamada — inclusive se comportar como um modelo
/// COMPROMETIDO (obedecer a injeção, pedir ferramenta de escrita). Registra tudo que o servidor enviou.
/// </summary>
public sealed class ProvedorIaRoteirizado : IAiProvider
{
    private readonly ConcurrentQueue<AiRequest> _requisicoes = new();
    private int _chamadas;

    public Func<AiRequest, int, AiResponse> Roteiro { get; set; } = (_, _) => Texto("ok");

    public bool FalharComHttp500 { get; set; }

    public TimeSpan Atraso { get; set; }

    /// <summary>Atraso decidido por requisição (ex.: só o modelo principal trava); vale no lugar de <see cref="Atraso"/>.</summary>
    public Func<AiRequest, TimeSpan>? AtrasoPor { get; set; }

    public int Chamadas => Volatile.Read(ref _chamadas);

    public IReadOnlyList<AiRequest> Requisicoes => _requisicoes.ToArray();

    public async Task<AiResponse> GenerateAsync(AiRequest requisicao, CancellationToken cancellationToken = default)
    {
        var n = Interlocked.Increment(ref _chamadas);
        _requisicoes.Enqueue(requisicao);

        var atraso = AtrasoPor?.Invoke(requisicao) ?? Atraso;
        if (atraso > TimeSpan.Zero)
            await Task.Delay(atraso, cancellationToken);

        if (FalharComHttp500)
            throw new FalhaProvedorIaException("http", 500);

        return Roteiro(requisicao, n);
    }

    public static AiResponse Texto(string texto) => new(texto, [], 100, 20, "modelo-falso", "STOP");

    public static AiResponse Chama(params (string Nome, string ArgumentosJson)[] chamadas) =>
        new(null, chamadas.Select(c => new AiChamadaFerramenta(c.Nome, JsonDocument.Parse(c.ArgumentosJson).RootElement.Clone())).ToList(),
            100, 20, "modelo-falso", "STOP");

    /// <summary>Pedido de ferramenta com <c>thoughtSignature</c>, como os modelos Gemini 3 respondem.</summary>
    public static AiResponse ChamaComAssinatura(string nome, string argumentosJson, string assinatura) =>
        new(null, [new AiChamadaFerramenta(nome, JsonDocument.Parse(argumentosJson).RootElement.Clone(), assinatura)],
            100, 20, "modelo-falso", "STOP");

    /// <summary>Resultados de ferramenta que o servidor mandou ao modelo nesta requisição.</summary>
    public static List<(string Nome, JsonElement Conteudo)> Resultados(AiRequest requisicao) =>
        requisicao.Mensagens.Where(m => m.Resultado is not null).Select(m => (m.Resultado!.Nome, m.Resultado.Conteudo)).ToList();

    public static string Serializar(AiRequest requisicao)
    {
        var sb = new StringBuilder(requisicao.PromptSistema);
        foreach (var m in requisicao.Mensagens)
        {
            sb.AppendLine().Append(m.Papel).Append(": ").Append(m.Texto);
            foreach (var c in m.Chamadas ?? [])
                sb.Append(' ').Append(c.Nome).Append(c.Argumentos.GetRawText());
            if (m.Resultado is not null)
                sb.Append(' ').Append(m.Resultado.Nome).Append(m.Resultado.Conteudo.GetRawText());
        }

        return sb.ToString();
    }
}

/// <summary>Captura tudo que passa pelo <see cref="ILogger"/> (mensagem formatada + exceção) para provar que não vaza conteúdo.</summary>
public sealed class CapturaDeLogs : ILoggerFactory
{
    private readonly ConcurrentQueue<string> _linhas = new();

    public IReadOnlyList<string> Linhas => _linhas.ToArray();

    public void AddProvider(ILoggerProvider provider)
    {
    }

    public ILogger CreateLogger(string categoryName) => new Registrador(categoryName, _linhas);

    public void Dispose()
    {
    }

    private sealed class Registrador(string categoria, ConcurrentQueue<string> linhas) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            linhas.Enqueue($"{categoria} [{logLevel}] {formatter(state, exception)} {exception}");
    }
}

public sealed record CenarioAgente(
    Guid NegocioId, string Slug, string NomeInstancia, Guid Ana, Guid Beto, Guid Inativo, Guid Corte, Guid Barba, Guid Tratamento,
    Guid ServicoDoInativo, string Nome);

/// <summary>
/// Sobe a API com <c>Ai__Provider=Gemini</c> (chave FALSA), o modelo roteirizado, a Evolution espiã e a fila imediata.
/// Nunca fala com o Gemini de verdade.
/// </summary>
public sealed class AmbienteAgente : IAsyncDisposable
{
    public const string Segredo = "segredo-do-agente-de-teste-0123456789";
    public const string ChaveGemini = "chave-gemini-falsa-NAO-PODE-VAZAR-123456";
    public const string TelefoneCliente = "5571988887777";
    public const string TelefoneDaInstancia = "5571999990000";

    private readonly PostgresContainerFixture _postgres;
    private int _contadorMensagens;

    public PlataformaWebApplicationFactory Fabrica { get; private set; } = null!;

    public EspiaClienteEvolution Evolution { get; } = new();

    public ProvedorIaRoteirizado Provedor { get; } = new();

    public CapturaDeLogs? Logs { get; private set; }

    public AmbienteAgente(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task IniciarAsync(
        IReadOnlyDictionary<string, string?>? configuracao = null, bool capturarLogs = false, Type? fila = null, string provedorIa = "Gemini",
        Action<IServiceCollection>? ajustarServicos = null)
    {
        var config = new Dictionary<string, string?>
        {
            ["Whatsapp:WebhookSecret"] = Segredo,
            ["Whatsapp:UrlPublicaApi"] = "http://api.invalid",
            ["Ai:Provider"] = provedorIa,
            ["Ai:Gemini:ApiKey"] = ChaveGemini,
        };
        foreach (var (chave, valor) in configuracao ?? new Dictionary<string, string?>())
            config[chave] = valor;

        Logs = capturarLogs ? new CapturaDeLogs() : null;
        var filaUsada = fila ?? typeof(FilaImediata);

        Fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString, config, servicos =>
        {
            servicos.AddSingleton<IClienteEvolution>(Evolution);
            servicos.AddSingleton<IAiProvider>(Provedor);
            if (filaUsada != typeof(void))
                servicos.AddSingleton(typeof(IFilaRespostaAgente), filaUsada);
            if (Logs is not null)
                servicos.AddSingleton<ILoggerFactory>(Logs);
            ajustarServicos?.Invoke(servicos);
        });
        using var aquecimento = Fabrica.CreateClient();
        await Fabrica.ResetarBancoAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Fabrica is not null)
            await Fabrica.DisposeAsync();
    }

    // ---------------------------------------------------------------- cenário

    /// <summary>
    /// Negócio com Ana (seg–sex, 9–12 e 13–18; corte 70/60 min próprio e um serviço fora da vitrine), Beto (seg–sex 9–12;
    /// corte e barba) e Caio (inativo, com um serviço só dele), mais a instância conectada e com o agente ligado.
    /// </summary>
    public async Task<CenarioAgente> SemearAsync(string nome = "Barbearia do Teste", string nomeCorte = "Corte", bool agenteAtivo = true)
    {
        using var escopo = Fabrica.Services.CreateScope();
        var db = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();

        var slug = "ag-" + Guid.NewGuid().ToString("N")[..12];
        var negocio = Negocio.Criar(Slug.Criar(slug), nome, TipoNegocio.Barbearia);
        db.Negocios.Add(negocio);

        var instancia = new InstanciaWhatsApp(negocio.Id, $"neg-{negocio.Id:N}");
        instancia.DefinirAgente(agenteAtivo);
        instancia.AtualizarStatus(StatusInstanciaWhatsApp.Conectada, TelefoneDaInstancia);
        db.InstanciasWhatsApp.Add(instancia);

        var categoria = Categoria.Criar(negocio.Id, "Cabelo");
        db.Categorias.Add(categoria);

        var ana = Profissional.Criar(negocio.Id, "Ana");
        var beto = Profissional.Criar(negocio.Id, "Beto");
        var caio = Profissional.Criar(negocio.Id, "Caio");
        caio.Desativar();
        db.Profissionais.AddRange(ana, beto, caio);

        foreach (var dia in new[] { DiaSemana.Segunda, DiaSemana.Terca, DiaSemana.Quarta, DiaSemana.Quinta, DiaSemana.Sexta })
        {
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocio.Id, ana.Id, dia, new TimeOnly(9, 0), new TimeOnly(12, 0)));
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocio.Id, ana.Id, dia, new TimeOnly(13, 0), new TimeOnly(18, 0)));
            db.HorariosTrabalho.Add(HorarioTrabalho.Criar(negocio.Id, beto.Id, dia, new TimeOnly(9, 0), new TimeOnly(12, 0)));
        }

        var corte = Servico.Criar(negocio.Id, categoria.Id, nomeCorte, 50m, 30);
        var barba = Servico.Criar(negocio.Id, categoria.Id, "Barba", 30m, 20);
        var tratamento = Servico.Criar(negocio.Id, categoria.Id, "Tratamento", 90m, 45, exibirNaPaginaInicial: false);
        var doInativo = Servico.Criar(negocio.Id, categoria.Id, "Só do inativo", 10m, 15);
        db.Servicos.AddRange(corte, barba, tratamento, doInativo);

        db.ProfissionalServicos.AddRange(
            ProfissionalServico.Criar(negocio.Id, ana.Id, corte.Id, 70m, 60),
            ProfissionalServico.Criar(negocio.Id, ana.Id, tratamento.Id),
            ProfissionalServico.Criar(negocio.Id, beto.Id, corte.Id),
            ProfissionalServico.Criar(negocio.Id, beto.Id, barba.Id),
            ProfissionalServico.Criar(negocio.Id, caio.Id, doInativo.Id));

        await db.SaveChangesAsync();
        return new CenarioAgente(negocio.Id, slug, instancia.NomeInstancia, ana.Id, beto.Id, caio.Id, corte.Id, barba.Id, tratamento.Id, doInativo.Id, nome);
    }

    // ---------------------------------------------------------------- webhook

    public async Task<HttpResponseMessage> EnviarAsync(CenarioAgente cenario, string texto, string telefone = TelefoneCliente, string? id = null)
    {
        id ??= "MSG-" + Interlocked.Increment(ref _contadorMensagens) + "-" + Guid.NewGuid().ToString("N")[..6];
        var corpo = JsonSerializer.Serialize(new
        {
            @event = "MESSAGES_UPSERT",
            instance = cenario.NomeInstancia,
            data = new
            {
                key = new { remoteJid = telefone + "@s.whatsapp.net", fromMe = false, id },
                pushName = "Cliente",
                message = new { conversation = texto },
                messageType = "conversation",
            },
        });

        using var cliente = Fabrica.CreateClient();
        return await cliente.PostAsync($"/webhooks/whatsapp/{Segredo}", new StringContent(corpo, Encoding.UTF8, "application/json"));
    }

    // ---------------------------------------------------------------- consultas ao banco

    public async Task<T> NoBancoAsync<T>(Func<PlataformaDbContext, Task<T>> consulta)
    {
        using var escopo = Fabrica.Services.CreateScope();
        return await consulta(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }

    public Task<List<MensagemWhatsApp>> MensagensAsync() =>
        NoBancoAsync(db => db.MensagensWhatsApp.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.CriadoEm).ToListAsync());

    public Task<List<UsoIa>> UsosAsync() =>
        NoBancoAsync(db => db.UsosIa.IgnoreQueryFilters().AsNoTracking().OrderBy(u => u.CriadoEm).ToListAsync());

    public Task<List<ConversaWhatsApp>> ConversasAsync() =>
        NoBancoAsync(db => db.ConversasWhatsApp.IgnoreQueryFilters().AsNoTracking().ToListAsync());

    /// <summary>Última resposta do assistente entregue ao cliente.</summary>
    public string? UltimaEntrega() => Evolution.Envios.Count == 0 ? null : Evolution.Envios[^1].Texto;

    // ---------------------------------------------------------------- datas

    private static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static DateOnly HojeNoNegocio() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime);

    /// <summary>Segunda-feira futura (a partir de depois de amanhã), dentro dos 60 dias consultáveis.</summary>
    public static DateOnly ProximaSegunda()
    {
        var dia = HojeNoNegocio().AddDays(2);
        while (dia.DayOfWeek != DayOfWeek.Monday)
            dia = dia.AddDays(1);
        return dia;
    }

    public static string HoraLocal(DateTimeOffset instante) =>
        TimeZoneInfo.ConvertTime(instante, Fuso).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);

    public HttpClient ClientePublico(string slug) => Fabrica.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri($"http://{slug}.{DominioDeTeste.Valor}/"),
    });
}
