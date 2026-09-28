using FluentAssertions;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Usuarios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

/// <summary>Iniciar atendimento e ajuste de valor (seção 7).</summary>
public sealed class AjusteValorTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    private static Agendamento EmAtendimento(decimal preco = 80m)
    {
        var agendamento = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Agora,
            [new ItemServicoAgendamento(Guid.NewGuid(), "Corte", preco, 30)]);
        agendamento.IniciarAtendimento();
        return agendamento;
    }

    [Theory]
    [InlineData(TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 15, 65)]
    [InlineData(TipoAjusteValor.Desconto, ModoAjusteValor.Percentual, 12.5, 70)]
    [InlineData(TipoAjusteValor.Acrescimo, ModoAjusteValor.Reais, 20, 100)]
    [InlineData(TipoAjusteValor.Acrescimo, ModoAjusteValor.Percentual, 10, 88)]
    [InlineData(TipoAjusteValor.Desconto, ModoAjusteValor.Percentual, 100, 0)]
    public void Calcula_o_valor_final_sobre_o_preco_original(TipoAjusteValor tipo, ModoAjusteValor modo, double valor, double esperado)
    {
        var agendamento = EmAtendimento();
        var linha = agendamento.Servicos.Single();

        var ajuste = agendamento.AjustarValor(linha.Id, tipo, modo, (decimal)valor, "motivo", null, Agora);

        ajuste.ValorDepois.Should().Be((decimal)esperado);
        linha.Preco.Should().Be(80m);
        linha.ValorCobrado.Should().Be((decimal)esperado);
        agendamento.Total.Should().Be((decimal)esperado);
    }

    [Fact]
    public void Novo_ajuste_substitui_o_anterior_partindo_do_preco_original()
    {
        var agendamento = EmAtendimento();
        var linha = agendamento.Servicos.Single();
        agendamento.AjustarValor(linha.Id, TipoAjusteValor.Desconto, ModoAjusteValor.Percentual, 50m, "a", null, Agora);

        var segundo = agendamento.AjustarValor(linha.Id, TipoAjusteValor.Desconto, ModoAjusteValor.Percentual, 10m, "b", null, Agora);

        (segundo.ValorAntes, segundo.ValorDepois).Should().Be((40m, 72m));
    }

    [Fact]
    public void Nao_ajusta_fora_do_atendimento_sem_motivo_nem_para_negativo()
    {
        var agendado = Agendamento.CriarConfirmado(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Agora,
            [new ItemServicoAgendamento(Guid.NewGuid(), "Corte", 80m, 30)]);
        var linhaAgendado = agendado.Servicos.Single().Id;
        agendado.Invoking(a => a.AjustarValor(linhaAgendado, TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 5m, "m", null, Agora))
            .Should().Throw<InvalidOperationException>();

        var agendamento = EmAtendimento();
        var linha = agendamento.Servicos.Single().Id;
        agendamento.Invoking(a => a.AjustarValor(linha, TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 5m, " ", null, Agora))
            .Should().Throw<ArgumentException>();
        agendamento.Invoking(a => a.AjustarValor(linha, TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 80.01m, "m", null, Agora))
            .Should().Throw<ArgumentException>();
        agendamento.Invoking(a => a.AjustarValor(linha, TipoAjusteValor.Acrescimo, ModoAjusteValor.Reais, -1m, "m", null, Agora))
            .Should().Throw<ArgumentException>();
        agendamento.Servicos.Single().PrecoAjustado.Should().BeNull();
    }

    [Fact]
    public void Correcao_apos_concluido_recalcula_a_comissao_com_o_percentual_gravado()
    {
        var agendamento = EmAtendimento(100m);
        agendamento.MarcarConcluido(30m, Agora);
        var linha = agendamento.Servicos.Single();

        agendamento.Invoking(a => a.AjustarValor(linha.Id, TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 10m, "m", null, Agora))
            .Should().Throw<InvalidOperationException>(); // sem ser correção, não
        var ajuste = agendamento.AjustarValor(linha.Id, TipoAjusteValor.Desconto, ModoAjusteValor.Reais, 10m, "m", null, Agora, correcaoAposConclusao: true);

        ajuste.AposConclusao.Should().BeTrue();
        (linha.ComissaoValorBase, linha.ComissaoPercentual, linha.ComissaoValor).Should().Be((90m, 30m, 27m));
    }

    [Fact]
    public void Iniciar_so_a_partir_de_agendado_e_em_atendimento_pode_concluir_ou_cancelar_mas_nao_faltar()
    {
        var agendamento = EmAtendimento();

        agendamento.Status.Should().Be(StatusAgendamento.EmAtendimento);
        agendamento.Invoking(a => a.IniciarAtendimento()).Should().Throw<InvalidOperationException>();
        agendamento.Invoking(a => a.MarcarFaltou()).Should().Throw<InvalidOperationException>();
        agendamento.Invoking(a => a.Mover(Agora.AddHours(1), Agora.AddHours(2))).Should().Throw<InvalidOperationException>();

        agendamento.MarcarConcluido(0m, Agora);
        agendamento.Status.Should().Be(StatusAgendamento.Concluido);
    }

    [Theory]
    [InlineData(Perfil.Recepcionista)]
    [InlineData(Perfil.Profissional)]
    public void Permissao_de_ajustar_vem_desligada_fora_do_Administrador(Perfil perfil)
    {
        Usuario.PermissoesPadrao(perfil).Should().NotContain(Permissao.AjustarValorAtendimento);
        Usuario.PermissoesPadrao(Perfil.Administrador).Should().Contain(Permissao.AjustarValorAtendimento);
    }
}
