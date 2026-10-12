using FluentAssertions;
using Plataforma.Dominio.Profissionais;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class FotoProfissionalTestes
{
    [Fact]
    public void Trocar_a_foto_enviada_devolve_a_chave_anterior_para_apagar()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");

        profissional.DefinirFotoArquivo("chave-1").Should().BeNull();
        profissional.DefinirFotoArquivo("chave-2").Should().Be("chave-1");

        profissional.FotoArquivo.Should().Be("chave-2");
    }

    [Fact]
    public void Remover_fotos_tira_a_enviada_e_a_url_externa_e_devolve_so_a_chave()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirFoto("https://exemplo.com/a.jpg");
        profissional.DefinirFotoArquivo("chave-1");

        profissional.RemoverFotos().Should().Be("chave-1");

        (profissional.FotoUrl, profissional.FotoArquivo).Should().Be((null, null));
    }

    [Fact]
    public void Remover_quando_so_ha_url_externa_nao_devolve_nada_para_apagar()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirFoto("https://exemplo.com/a.jpg");

        profissional.RemoverFotos().Should().BeNull();
        profissional.FotoUrl.Should().BeNull();
    }

    [Fact]
    public void Excluir_apaga_as_duas_fotos()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Ana");
        profissional.DefinirFoto("https://exemplo.com/a.jpg");
        profissional.DefinirFotoArquivo("chave-1");

        profissional.Excluir(DateTimeOffset.UtcNow);

        (profissional.FotoUrl, profissional.FotoArquivo).Should().Be((null, null));
    }
}
