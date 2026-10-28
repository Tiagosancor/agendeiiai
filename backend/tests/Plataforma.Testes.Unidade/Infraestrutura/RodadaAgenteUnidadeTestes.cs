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
        rodada.MotivoFim.Should().Be("DESCONHECIDO", "fora da lista fechada");
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
            .Should().Be(("DESCONHECIDO", 0, 2, "http", 503, rodadaId));

        var antigo = new UsoIa(Guid.NewGuid(), Guid.NewGuid(), "m", 1, 1, true);
        (antigo.MotivoFim, antigo.LatenciaMs, antigo.RodadaId).Should().Be((null, null, null));
    }

    [Theory]
    [InlineData("stop", "STOP")]
    [InlineData("MAX_TOKENS", "MAX_TOKENS")]
    [InlineData("LACO_FERRAMENTAS", "LACO_FERRAMENTAS")]
    [InlineData("joao.silva@exemplo.com", "DESCONHECIDO")]
    [InlineData("5571988887777", "DESCONHECIDO")]
    [InlineData("Maria Souza", "DESCONHECIDO")]
    [InlineData(null, null)]
    public void MotivoFim_so_aceita_valores_da_lista_fechada(string? entrada, string? esperado) =>
        RodadaAgente.MotivoFimFechado(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("http", "http")]
    [InlineData("TIMEOUT", "timeout")]
    [InlineData("joao@exemplo.com", "DESCONHECIDO")]
    [InlineData("71 98888-7777", "DESCONHECIDO")]
    [InlineData(null, null)]
    public void TipoDeFalha_so_aceita_valores_da_lista_fechada(string? entrada, string? esperado) =>
        RodadaAgente.TipoFalhaFechado(entrada).Should().Be(esperado);

    [Theory]
    [InlineData("http:503", "http:503")]
    [InlineData("http:503,timeout", "http:503,timeout")]
    [InlineData("http:joao", "http")]
    [InlineData("5571988887777:500", "DESCONHECIDO:500")]
    [InlineData("a@b.com,rede", "DESCONHECIDO,rede")]
    public void FalhaDoProvedor_revalida_cada_item(string entrada, string esperado) =>
        RodadaAgente.FalhaProvedorFechada(entrada).Should().Be(esperado);

    [Fact]
    public void Rodada_e_uso_nunca_guardam_dado_pessoal_nos_campos_de_telemetria()
    {
        const string pessoal = "joao.silva@exemplo.com 71988887777";
        var rodada = new RodadaAgente(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow, 1, DesfechoRodadaAgente.Fallback, null, 1, 1, 1, "m",
            pessoal, null, null, pessoal, "v2");
        var uso = new UsoIa(Guid.NewGuid(), Guid.NewGuid(), "m", 1, 1, false, pessoal, 1, 1, pessoal, 500, null);

        new[] { rodada.MotivoFim, rodada.FalhaProvedor, uso.MotivoFim, uso.FalhaTipo }.Should().OnlyContain(v => v == "DESCONHECIDO");
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
