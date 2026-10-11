using FluentAssertions;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Negocios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class LembreteRetornoTestes
{
    private static readonly TimeOnly Dez = new(10, 0);

    [Fact]
    public void Configuracao_padrao_e_valida()
    {
        LembreteRetorno.Validar(LembreteRetorno.DiasPadrao, LembreteRetorno.HorarioPadrao, LembreteRetorno.MensagemPadrao)
            .Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void Dias_fora_do_intervalo_sao_recusados(int dias)
    {
        LembreteRetorno.Validar(dias, Dez, "Olá").Should().ContainKey("dias").And.HaveCount(1);
    }

    [Fact]
    public void Dias_e_horario_ausentes_sao_recusados()
    {
        var erros = LembreteRetorno.Validar(null, null, "Olá");

        erros.Keys.Should().BeEquivalentTo("dias", "horario");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Mensagem_vazia_e_recusada(string? mensagem)
    {
        LembreteRetorno.Validar(30, Dez, mensagem).Should().ContainKey("mensagem");
    }

    [Fact]
    public void Mensagem_longa_demais_e_recusada()
    {
        LembreteRetorno.Validar(30, Dez, new string('a', LembreteRetorno.TamanhoMaximoMensagem + 1)).Should().ContainKey("mensagem");
    }

    [Theory]
    [InlineData("Oi {nome}")]
    [InlineData("Oi {cliente}, volte!")]
    [InlineData("Oi {}")]
    [InlineData("Faz { dias } dias")]
    [InlineData("Faz {DIAS} dias")]
    [InlineData("{nome_cliente} {telefone}")]
    public void Variavel_desconhecida_e_recusada(string mensagem)
    {
        var erros = LembreteRetorno.Validar(30, Dez, mensagem);

        erros.Should().ContainKey("mensagem");
        erros["mensagem"][0].Should().Contain("Variável desconhecida");
    }

    [Fact]
    public void Todas_as_variaveis_conhecidas_e_chave_solta_sao_aceitas()
    {
        LembreteRetorno.Validar(30, Dez, "{nome_cliente} {nome_empresa} {dias} {link_agendamento} até logo :-}").Should().BeEmpty();
    }

    [Fact]
    public void Montar_substitui_as_variaveis()
    {
        var texto = LembreteRetorno.Montar(LembreteRetorno.MensagemPadrao, "Ana", "Barbearia X", 30, "https://x.exemplo.com/");

        texto.Should().Be("Olá, Ana! Já faz 30 dias desde seu último atendimento na Barbearia X. Que tal agendar o próximo? https://x.exemplo.com/");
    }

    [Fact]
    public void Negocio_novo_comeca_com_o_lembrete_desligado_e_os_padroes()
    {
        var negocio = NovoNegocio();

        negocio.LembreteRetornoAtivo.Should().BeFalse();
        negocio.LembreteRetornoDias.Should().Be(30);
        negocio.LembreteRetornoHorario.Should().Be(Dez);
        negocio.LembreteRetornoMensagem.Should().Be(LembreteRetorno.MensagemPadrao);
        negocio.LembreteRetornoAtivadoEm.Should().BeNull();
    }

    [Fact]
    public void Ligar_marca_quando_e_editar_ligado_nao_mexe()
    {
        var negocio = NovoNegocio();
        var ontem = DateTimeOffset.UtcNow.AddDays(-1);

        negocio.ConfigurarLembreteRetorno(true, 30, Dez, "Oi {nome_cliente}", ontem);
        negocio.ConfigurarLembreteRetorno(true, 45, new TimeOnly(9, 30), "Oi de novo", DateTimeOffset.UtcNow);

        negocio.LembreteRetornoAtivadoEm.Should().Be(ontem);
        negocio.LembreteRetornoDias.Should().Be(45);
    }

    [Fact]
    public void Desligar_guarda_a_configuracao_e_religar_marca_o_novo_momento()
    {
        var negocio = NovoNegocio();
        negocio.ConfigurarLembreteRetorno(true, 20, new TimeOnly(15, 0), "Volte, {nome_cliente}!", DateTimeOffset.UtcNow.AddDays(-10));

        negocio.ConfigurarLembreteRetorno(false, 20, new TimeOnly(15, 0), "Volte, {nome_cliente}!", DateTimeOffset.UtcNow.AddDays(-5));

        negocio.LembreteRetornoAtivo.Should().BeFalse();
        (negocio.LembreteRetornoDias, negocio.LembreteRetornoHorario, negocio.LembreteRetornoMensagem)
            .Should().Be((20, new TimeOnly(15, 0), "Volte, {nome_cliente}!"));

        var agora = DateTimeOffset.UtcNow;
        negocio.ConfigurarLembreteRetorno(true, 20, new TimeOnly(15, 0), "Volte, {nome_cliente}!", agora);

        negocio.LembreteRetornoAtivadoEm.Should().Be(agora);
    }

    [Fact]
    public void Configuracao_invalida_no_dominio_lanca()
    {
        var negocio = NovoNegocio();

        var acao = () => negocio.ConfigurarLembreteRetorno(true, 0, Dez, "Oi", DateTimeOffset.UtcNow);

        acao.Should().Throw<ArgumentException>();
        negocio.LembreteRetornoAtivo.Should().BeFalse();
    }

    [Fact]
    public void Cliente_aceita_por_padrao_e_anonimizado_deixa_de_aceitar()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "Ana", TelefoneE164.Criar("+5571999990000"));
        cliente.AceitaLembreteRetorno.Should().BeTrue();

        cliente.Anonimizar(DateTimeOffset.UtcNow);

        cliente.AceitaLembreteRetorno.Should().BeFalse();
    }

    [Fact]
    public void Falha_tenta_de_novo_uma_vez_e_so_no_mesmo_dia()
    {
        var hoje = new DateOnly(2026, 10, 11);
        var envio = LembreteRetornoEnvio.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), hoje, DateTimeOffset.UtcNow);
        envio.MarcarFalha("fora do ar", DateTimeOffset.UtcNow);

        envio.PodeTentarDeNovo(hoje.AddDays(1)).Should().BeFalse();
        envio.PodeTentarDeNovo(hoje).Should().BeTrue();

        envio.NovaTentativa(DateTimeOffset.UtcNow);
        envio.MarcarFalha("fora do ar de novo", DateTimeOffset.UtcNow);

        envio.Tentativas.Should().Be(2);
        envio.PodeTentarDeNovo(hoje).Should().BeFalse();
    }

    [Fact]
    public void Enviado_nunca_tenta_de_novo()
    {
        var hoje = new DateOnly(2026, 10, 11);
        var envio = LembreteRetornoEnvio.Iniciar(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), hoje, DateTimeOffset.UtcNow);
        envio.MarcarEnviado("wamid.1", DateTimeOffset.UtcNow);

        envio.PodeTentarDeNovo(hoje).Should().BeFalse();
    }

    private static Negocio NovoNegocio() => Negocio.Criar(Slug.Criar("negocio-teste"), "Negócio", TipoNegocio.Barbearia);
}
