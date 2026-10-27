using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Testes.Integracao.Infraestrutura;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Modelo de reserva do Gemini (<c>Ai__Gemini__ModeloReserva</c>), com o provedor roteirizado (nunca o Gemini real): quando a
/// reserva entra, quando NÃO entra, a assinatura de pensamento que não atravessa de um modelo a outro, o registro de uso,
/// o disjuntor e o orçamento de tempo.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class ModeloReservaTestes : IAsyncLifetime
{
    private const string Reserva = "modelo-reserva";
    private const string Principal = "modelo-principal";

    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public ModeloReservaTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public Task InitializeAsync() => IniciarAsync(Reserva);

    private async Task IniciarAsync(string? reserva, string tempoMaximo = "40")
    {
        if (_ambiente is not null)
            await _ambiente.DisposeAsync();

        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?>
        {
            ["Ai:Gemini:Model"] = Principal,
            ["Ai:Gemini:ModeloReserva"] = reserva,
            ["Ai:TempoMaximoProcessamentoSegundos"] = tempoMaximo,
            ["Ai:LimiteChamadasPorNegocioPorDia"] = "250",
        });
    }

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private static AiResponse Responde(AiRequest r, string texto) => new(texto, [], 100, 20, r.Modelo ?? Principal, "STOP");

    private static AiResponse Chama(AiRequest r, string nome, string assinatura) =>
        new(null, [new AiChamadaFerramenta(nome, JsonDocument.Parse("{}").RootElement.Clone(), assinatura)], 100, 20, r.Modelo ?? Principal, "STOP");

    [Theory]
    [InlineData(503)]
    [InlineData(429)]
    [InlineData(404)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(504)]
    public async Task Falha_de_capacidade_no_principal_responde_pela_reserva_e_registra_o_modelo_que_respondeu(int status)
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (r, _) => r.Modelo is null ? throw new FalhaProvedorIaException("http", status) : Responde(r, "Resposta da reserva.");

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.UltimaEntrega().Should().Be("Resposta da reserva.");
        _ambiente.Provedor.Requisicoes.Select(r => r.Modelo).Should().Equal(null, Reserva);
        var usos = await _ambiente.UsosAsync();
        usos.Select(u => (u.Modelo, u.Sucesso)).Should().Equal((Principal, false), (Reserva, true));
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("rede")]
    public async Task Timeout_e_erro_de_rede_tambem_acionam_a_reserva(string tipo)
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (r, _) => r.Modelo is null ? throw new FalhaProvedorIaException(tipo) : Responde(r, "ok reserva");

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.UltimaEntrega().Should().Be("ok reserva");
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    public async Task Erro_nosso_ou_de_chave_nao_aciona_a_reserva(int status)
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => throw new FalhaProvedorIaException("http", status);

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        _ambiente.UltimaEntrega().Should().Contain("assistente virtual");
    }

    [Fact]
    public async Task Principal_e_reserva_falham_cai_no_fallback_com_o_link_e_as_duas_tentativas_ficam_registradas()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => throw new FalhaProvedorIaException("http", 503);

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(2);
        _ambiente.UltimaEntrega().Should().Contain("assistente virtual").And.Contain(c.Slug);
        (await _ambiente.UsosAsync()).Select(u => (u.Modelo, u.Sucesso)).Should().Equal((Principal, false), (Reserva, false));
    }

    [Fact]
    public async Task Falha_na_segunda_volta_refaz_a_rodada_do_zero_na_reserva_sem_a_assinatura_nem_os_turnos_do_principal()
    {
        var c = await _ambiente.SemearAsync();
        const string assinatura = "ASSINATURA-DO-PRINCIPAL==";
        _ambiente.Provedor.Roteiro = (r, n) => n switch
        {
            1 => Chama(r, "listar_profissionais", assinatura),
            2 => throw new FalhaProvedorIaException("http", 503),
            _ => Responde(r, "Atendem a Ana e o Beto."),
        };

        await _ambiente.EnviarAsync(c, "quem atende?");

        _ambiente.UltimaEntrega().Should().Be("Atendem a Ana e o Beto.");
        var reserva = _ambiente.Provedor.Requisicoes[2];
        reserva.Modelo.Should().Be(Reserva);
        reserva.Mensagens.Should().ContainSingle().Which.Papel.Should().Be(PapelIa.Usuario);
        reserva.Mensagens.SelectMany(m => m.Chamadas ?? []).Should().BeEmpty();
        ProvedorIaRoteirizado.Serializar(reserva).Should().NotContain(assinatura);
        _ambiente.Provedor.Requisicoes[1].Mensagens.SelectMany(m => m.Chamadas ?? []).Single().AssinaturaPensamento.Should().Be(assinatura);
    }

    [Fact]
    public async Task Reserva_que_pede_ferramenta_leva_so_a_propria_assinatura_e_conclui()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (r, _) =>
        {
            if (r.Modelo is null)
                throw new FalhaProvedorIaException("http", 503);
            var chamadas = r.Mensagens.SelectMany(m => m.Chamadas ?? []).ToList();
            if (chamadas.Count == 0)
                return Chama(r, "listar_servicos", "ASSINATURA-DA-RESERVA");
            chamadas.Single().AssinaturaPensamento.Should().Be("ASSINATURA-DA-RESERVA");
            return Responde(r, "Temos corte e barba.");
        };

        await _ambiente.EnviarAsync(c, "quais serviços?");

        _ambiente.UltimaEntrega().Should().Be("Temos corte e barba.");
        (await _ambiente.UsosAsync()).Select(u => u.Modelo).Should().Equal(Principal, Reserva, Reserva);
    }

    [Fact]
    public async Task Sem_reserva_configurada_nada_muda_uma_falha_de_capacidade_vai_direto_ao_fallback()
    {
        await IniciarAsync(reserva: "");
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (_, _) => throw new FalhaProvedorIaException("http", 503);

        await _ambiente.EnviarAsync(c, "oi");

        _ambiente.Provedor.Chamadas.Should().Be(1);
        _ambiente.Provedor.Requisicoes.Single().Modelo.Should().BeNull();
        _ambiente.UltimaEntrega().Should().Contain("assistente virtual");
    }

    [Fact]
    public async Task Disjuntor_depois_da_falha_de_capacidade_as_proximas_mensagens_vao_direto_a_reserva()
    {
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (r, _) => r.Modelo is null ? throw new FalhaProvedorIaException("http", 503) : Responde(r, "da reserva");

        await _ambiente.EnviarAsync(c, "primeira");
        await _ambiente.EnviarAsync(c, "segunda");

        // 1ª mensagem: principal (falha) + reserva; 2ª: só a reserva.
        _ambiente.Provedor.Requisicoes.Select(r => r.Modelo).Should().Equal(null, Reserva, Reserva);
    }

    [Fact]
    public async Task Disjuntor_aberto_para_um_negocio_nao_muda_o_modelo_de_outro_negocio()
    {
        var a = await _ambiente.SemearAsync("Negócio A");
        var b = await _ambiente.SemearAsync("Negócio B");
        var principalFalha = true;
        _ambiente.Provedor.Roteiro = (r, _) => r.Modelo is null && principalFalha ? throw new FalhaProvedorIaException("http", 503) : Responde(r, "ok");

        await _ambiente.EnviarAsync(a, "oi");
        principalFalha = false;
        await _ambiente.EnviarAsync(b, "oi");
        await _ambiente.EnviarAsync(a, "de novo");

        // A: principal (falha) + reserva; B: principal (disjuntor de A não vale para B); A de novo: direto à reserva.
        _ambiente.Provedor.Requisicoes.Select(r => r.Modelo).Should().Equal(null, Reserva, null, Reserva);
    }

    private sealed class ExecutorQueTravaAteOPrazo : Plataforma.Aplicacao.WhatsAppAgente.IExecutorFerramentasAgente
    {
        public IReadOnlyList<Plataforma.Aplicacao.WhatsAppAgente.IFerramentaAgente> Ferramentas => [];

        public IReadOnlyList<Plataforma.Aplicacao.WhatsAppAgente.IFerramentaAgente> FerramentasPara(Guid negocioId) => [];

        public async Task<Plataforma.Aplicacao.WhatsAppAgente.ResultadoFerramentaAgente> ExecutarComSinalAsync(
            Plataforma.Aplicacao.WhatsAppAgente.ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default!;
        }

        public async Task<JsonElement> ExecutarAsync(
            Plataforma.Aplicacao.WhatsAppAgente.ContextoConversaAgente contexto, string nome, JsonElement argumentos, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return default;
        }
    }

    [Fact]
    public async Task Ferramenta_cancelada_pelo_prazo_do_principal_ainda_chama_a_reserva_e_nao_registra_nova_falha()
    {
        await _ambiente.DisposeAsync();
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?>
        {
            ["Ai:Gemini:Model"] = Principal,
            ["Ai:Gemini:ModeloReserva"] = Reserva,
            ["Ai:TempoMaximoProcessamentoSegundos"] = "5",
            ["Ai:LimiteChamadasPorNegocioPorDia"] = "250",
        }, ajustarServicos: s => s.AddScoped<Plataforma.Aplicacao.WhatsAppAgente.IExecutorFerramentasAgente, ExecutorQueTravaAteOPrazo>());
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.Roteiro = (r, _) => r.Modelo is null ? Chama(r, "listar_servicos", "ASSINATURA") : Responde(r, "Resposta da reserva.");

        await _ambiente.EnviarAsync(c, "quais serviços?");

        _ambiente.UltimaEntrega().Should().Be("Resposta da reserva.");
        _ambiente.Provedor.Requisicoes.Select(r => r.Modelo).Should().Equal(null, Reserva);
        // O principal já foi contabilizado como sucesso (a chamada ao modelo respondeu); o prazo da ferramenta não gera outra linha.
        (await _ambiente.UsosAsync()).Select(u => (u.Modelo, u.Sucesso)).Should().Equal((Principal, true), (Reserva, true));
    }

    [Fact]
    public async Task Orcamento_de_tempo_principal_travado_e_cortado_para_sobrar_tempo_a_reserva()
    {
        await IniciarAsync(Reserva, tempoMaximo: "10");
        var c = await _ambiente.SemearAsync();
        _ambiente.Provedor.AtrasoPor = r => r.Modelo is null ? TimeSpan.FromSeconds(120) : TimeSpan.Zero;
        _ambiente.Provedor.Roteiro = (r, _) => Responde(r, "reserva a tempo");

        var relogio = Stopwatch.StartNew();
        await _ambiente.EnviarAsync(c, "oi");
        relogio.Stop();

        _ambiente.UltimaEntrega().Should().Be("reserva a tempo");
        relogio.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "principal (60% = 6 s) + reserva cabem no orçamento de 10 s");
        (await _ambiente.UsosAsync()).Select(u => (u.Modelo, u.Sucesso)).Should().Equal((Principal, false), (Reserva, true));
    }
}
