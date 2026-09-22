using FluentAssertions;
using Microsoft.Extensions.Options;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Seguranca;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

public sealed class CriptografiaCpfTestes
{
    private static CriptografiaCpf CriarServico(string chaveId = "v1", byte[]? chave = null)
    {
        var opcoes = new OpcoesCriptografiaCpf
        {
            ChaveId = chaveId,
            ChaveBase64 = Convert.ToBase64String(chave ?? new byte[32]),
        };

        return new CriptografiaCpf(Options.Create(opcoes));
    }

    [Fact]
    public void Proteger_nunca_guarda_o_cpf_em_texto_puro()
    {
        var servico = CriarServico();
        var cpf = Cpf.Criar("111.444.777-35");

        var protegido = servico.Proteger(cpf);

        protegido.Mascarado.Should().Be("***.444.777-**");
        Convert.ToBase64String(protegido.TextoCifrado).Should().NotContain(cpf.Digitos);
    }

    [Fact]
    public void Proteger_e_Revelar_fazem_ida_e_volta_corretamente()
    {
        var servico = CriarServico();
        var cpf = Cpf.Criar("111.444.777-35");

        var protegido = servico.Proteger(cpf);
        var revelado = servico.Revelar(protegido);

        revelado.Should().Be(cpf.Digitos);
    }

    [Fact]
    public void Cada_chamada_usa_um_nonce_diferente()
    {
        var servico = CriarServico();
        var cpf = Cpf.Criar("111.444.777-35");

        var protegido1 = servico.Proteger(cpf);
        var protegido2 = servico.Proteger(cpf);

        protegido1.Nonce.Should().NotBeEquivalentTo(protegido2.Nonce);
        protegido1.TextoCifrado.Should().NotBeEquivalentTo(protegido2.TextoCifrado);
    }

    [Fact]
    public void Revelar_com_chave_diferente_da_atual_lanca_excecao()
    {
        var servicoQueProtegeu = CriarServico(chaveId: "v1", chave: new byte[32]);
        var protegido = servicoQueProtegeu.Proteger(Cpf.Criar("111.444.777-35"));

        var servicoComOutraChave = CriarServico(chaveId: "v2", chave: new byte[32]);

        var acao = () => servicoComOutraChave.Revelar(protegido);

        acao.Should().Throw<InvalidOperationException>();
    }
}
