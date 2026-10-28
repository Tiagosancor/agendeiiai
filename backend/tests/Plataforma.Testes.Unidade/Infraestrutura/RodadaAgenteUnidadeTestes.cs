using FluentAssertions;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Fase 4, fatia 1: o diagnóstico persistente guarda só números e códigos, e a configuração tem faixas e padrões fixos.</summary>
public sealed class RodadaAgenteUnidadeTestes
{
    private static readonly ValidadorOpcoesIa Validador = new();

    [Theory]
    [InlineData("STOP", "STOP")]
    [InlineData("http:503", "http:503")]
    [InlineData("Olá, João! 71 98888-7777", "Ol" + "Joo" + "71" + "98888-7777")]
    [InlineData("joao@exemplo.com", "joaoexemplo.com")]
    [InlineData("  ", null)]
    [InlineData("!!! ???", null)]
    [InlineData(null, null)]
    public void CodigoSeguro_remove_espaco_acento_e_pontuacao_de_texto_livre(string? entrada, string? esperado) =>
        RodadaAgente.CodigoSeguro(entrada, 40).Should().Be(esperado);

    [Fact]
    public void CodigoSeguro_limita_o_tamanho() =>
        RodadaAgente.CodigoSeguro(new string('a', 100), RodadaAgente.TamanhoMaximoCodigo)!.Length.Should().Be(40);

    [Fact]
    public void Rodada_guarda_so_codigos_e_limita_as_listas()
    {
        var ferramentas = Enumerable.Range(0, 50).Select(i => "consultar_horarios").ToList();
        var rodada = new RodadaAgente(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, -5, DesfechoRodadaAgente.Respondida, null, -1, -2, -3,
            "gemini 3.1 flash", "STOP com texto", ferramentas, ["horario indisponivel", "  "], "http:503", "v2");

        rodada.DuracaoMs.Should().Be(0);
        (rodada.Chamadas, rodada.TokensEntrada, rodada.TokensSaida).Should().Be((0, 0, 0));
        rodada.Modelo.Should().Be("gemini3.1flash");
        rodada.MotivoFim.Should().Be("STOPcomtexto");
        rodada.Ferramentas!.Length.Should().Be(RodadaAgente.TamanhoMaximoLista);
        rodada.Ferramentas.Should().StartWith("consultar_horarios,consultar_horarios");
        rodada.ErrosFerramenta.Should().Be("horarioindisponivel");
        rodada.FalhaProvedor.Should().Be("http:503");
    }

    [Fact]
    public void Lista_vazia_vira_nulo()
    {
        var rodada = new RodadaAgente(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 1, DesfechoRodadaAgente.Silencio, MotivoSilencioRodada.Humano,
            0, 0, 0, null, null, [], null, null, null);

        (rodada.Ferramentas, rodada.ErrosFerramenta, rodada.MotivoFim, rodada.Modelo).Should().Be((null, null, null, null));
        rodada.MotivoSilencio.Should().Be(MotivoSilencioRodada.Humano);
    }

    [Fact]
    public void Uso_com_campos_extras_normaliza_os_codigos()
    {
        var rodadaId = Guid.NewGuid();
        var uso = new UsoIa(Guid.NewGuid(), Guid.NewGuid(), "m", 10, 5, false, "MAX TOKENS", -3, 2, "http", 503, rodadaId);

        (uso.MotivoFim, uso.LatenciaMs, uso.Iteracao, uso.FalhaTipo, uso.StatusHttp, uso.RodadaId)
            .Should().Be(("MAXTOKENS", 0, 2, "http", 503, rodadaId));

        var antigo = new UsoIa(Guid.NewGuid(), Guid.NewGuid(), "m", 1, 1, true);
        (antigo.MotivoFim, antigo.LatenciaMs, antigo.RodadaId).Should().Be((null, null, null));
    }

    [Fact]
    public void Padroes_e_faixas_do_registro_de_rodadas()
    {
        var o = new OpcoesIa();
        (o.RegistrarRodadas, o.RetencaoRodadasDias).Should().Be((true, 90));
        Validador.Validate(null, o).Succeeded.Should().BeTrue();

        foreach (var dias in new[] { 7, 365 })
            Validador.Validate(null, new OpcoesIa { RetencaoRodadasDias = dias }).Succeeded.Should().BeTrue();
        foreach (var dias in new[] { 0, 6, 366 })
            Validador.Validate(null, new OpcoesIa { RetencaoRodadasDias = dias }).Failed.Should().BeTrue();
    }
}
