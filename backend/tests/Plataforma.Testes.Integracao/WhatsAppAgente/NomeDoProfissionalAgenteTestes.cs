using System.Globalization;
using System.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.WhatsAppAgente;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;
using static Plataforma.Testes.Integracao.WhatsAppAgente.HelpersAgendamentoAgente;
using static Plataforma.Testes.Integracao.WhatsAppAgente.ProvedorIaRoteirizado;

namespace Plataforma.Testes.Integracao.WhatsAppAgente;

/// <summary>
/// Cliente cita o profissional só pelo primeiro nome. O casamento é do MODELO (regra de prompt) sobre a lista pública; o servidor não
/// tem busca por nome. O "modelo" roteirizado aqui aplica a regra do prompt (primeiro nome, sem caixa/acento) e os testes provam o que
/// é do servidor: a lista traz o necessário, só IDs chegam à escrita e o resumo mostra o nome COMPLETO. Nunca Gemini real.
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class NomeDoProfissionalAgenteTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private AmbienteAgente _ambiente = null!;

    public NomeDoProfissionalAgenteTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _ambiente = new AmbienteAgente(_postgres);
        await _ambiente.IniciarAsync(new Dictionary<string, string?>
        {
            ["Ai:Agendamento:Modo"] = "Todos",
            ["Ai:LimiteMensagensPorTelefonePorHora"] = "1000",
            ["Ai:LimiteMensagensPorTelefonePorDia"] = "5000",
            ["Ai:MaxChamadasFerramentaPorIteracao"] = "10",
        });
    }

    public async Task DisposeAsync() => await _ambiente.DisposeAsync();

    private async Task<CenarioAgente> CenarioComHomonimosAsync()
    {
        var c = await _ambiente.SemearAsync();
        await _ambiente.NoBancoAsync(async db =>
        {
            foreach (var nome in new[] { "Ana Lima", "Verônica Souza" })
            {
                var p = Profissional.Criar(c.NegocioId, nome);
                db.Profissionais.Add(p);
                db.ProfissionalServicos.Add(ProfissionalServico.Criar(c.NegocioId, p.Id, c.Corte));
                db.HorariosTrabalho.Add(HorarioTrabalho.Criar(c.NegocioId, p.Id, DiaSemana.Segunda, new TimeOnly(9, 0), new TimeOnly(12, 0)));
            }

            await db.SaveChangesAsync();
            return 0;
        });
        return c;
    }

    private static string Normalizar(string texto)
    {
        var d = texto.Normalize(NormalizationForm.FormD).Where(ch => CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark);
        return new string(d.ToArray()).Trim().ToLowerInvariant();
    }

    /// <summary>Aplica a regra do prompt à lista devolvida por listar_profissionais.</summary>
    private static List<(Guid Id, string Nome)> Casar(AiRequestLike r, string citado) =>
        ProvedorIaRoteirizado.Resultados(r.Requisicao).Single(x => x.Nome == "listar_profissionais").Conteudo.EnumerateArray()
            .Select(p => (Id: p.GetProperty("id").GetGuid(), Nome: p.GetProperty("nome").GetString()!))
            .Where(p => Normalizar(p.Nome.Split(' ')[0]) == Normalizar(citado)).ToList();

    private sealed record AiRequestLike(Plataforma.Aplicacao.WhatsAppAgente.Ia.AiRequest Requisicao);

    [Fact]
    public async Task Prompt_v1_e_v2_trazem_a_regra_do_primeiro_nome()
    {
        var antes = ConstrutorPromptAgente.Construir("X", DateTimeOffset.UtcNow, "https://x.com");
        var depois = ConstrutorPromptAgente.ConstruirComAgendamento("X", DateTimeOffset.UtcNow, "https://x.com");
        foreach (var prompt in new[] { antes, depois })
            prompt.Should().Contain("primeiro nome").And.Contain("MAIS DE UM profissional").And.Contain("Veronica = Verônica").And.Contain("NUNCA invente ids");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Primeiro_nome_de_dois_profissionais_termina_em_pergunta_sem_preparar_agendamento()
    {
        var c = await CenarioComHomonimosAsync();
        _ambiente.Provedor.Roteiro = (req, n) =>
        {
            if (n == 1)
                return Chama(("listar_profissionais", "{}"));

            var achados = Casar(new(req), "Ana");
            return achados.Count > 1
                ? Texto("Qual delas? " + string.Join(" ou ", achados.Select(a => a.Nome).Order()))
                : Chama(("preparar_agendamento", Prepara(c, achados.Single().Id, "10:00", "Maria Souza")));
        };

        await _ambiente.EnviarAsync(c, "Ana");

        _ambiente.Provedor.Chamadas.Should().Be(2);
        _ambiente.UltimaEntrega().Should().Contain("Qual delas?").And.Contain("Ana Lima");
        (await _ambiente.MensagensAsync()).Where(m => m.Papel == PapelMensagemWhatsApp.Ferramenta).Select(m => m.NomeFerramenta)
            .Should().NotContain("preparar_agendamento");
        (await _ambiente.PendenciasAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Veronica")]
    [InlineData("verônica")]
    public async Task Primeiro_nome_unico_com_ou_sem_acento_resolve_o_id_e_o_resumo_do_servidor_traz_o_nome_completo(string citado)
    {
        var c = await CenarioComHomonimosAsync();
        _ambiente.Provedor.Roteiro = (req, n) =>
        {
            if (n == 1)
                return Chama(("listar_profissionais", "{}"));

            var achados = Casar(new(req), citado);
            return achados.Count == 1
                ? Chama(("preparar_agendamento", Prepara(c, achados[0].Id, "10:00", "Maria Souza")))
                : Texto("Qual delas?");
        };

        await _ambiente.EnviarAsync(c, citado);

        (await _ambiente.PendenciasAsync()).Should().ContainSingle();
        var agendamento = (await _ambiente.AgendamentosAsync()).Single();
        agendamento.Inicio.Should().Be(H(10, 0));
        _ambiente.UltimaEntrega().Should().Contain("Profissional: Verônica Souza");
    }

    [Fact]
    public async Task Nome_sem_correspondencia_lista_os_profissionais_sem_ferramenta_de_escrita()
    {
        var c = await CenarioComHomonimosAsync();
        _ambiente.Provedor.Roteiro = (req, n) =>
        {
            if (n == 1)
                return Chama(("listar_profissionais", "{}"));

            var todos = Resultados(req).Single().Conteudo.EnumerateArray().Select(p => p.GetProperty("nome").GetString()!).Order();
            return Casar(new(req), "Zeca").Count == 0
                ? Texto("Não encontrei o Zeca. Profissionais: " + string.Join(", ", todos))
                : Texto("?");
        };

        await _ambiente.EnviarAsync(c, "Zeca");

        _ambiente.Provedor.Chamadas.Should().Be(2);
        _ambiente.UltimaEntrega().Should().Contain("Não encontrei o Zeca").And.Contain("Ana Lima").And.Contain("Verônica Souza");
        (await _ambiente.MensagensAsync()).Where(m => m.Papel == PapelMensagemWhatsApp.Ferramenta).Select(m => m.NomeFerramenta)
            .Should().OnlyContain(f => f == "listar_profissionais");
        (await _ambiente.AgendamentosAsync()).Should().BeEmpty();
    }
}
