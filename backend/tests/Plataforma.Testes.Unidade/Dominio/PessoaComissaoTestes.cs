using FluentAssertions;
using Plataforma.Dominio.Comissoes;

namespace Plataforma.Testes.Unidade.Dominio;

public class PessoaComissaoTestes
{
    [Fact]
    public void Exige_exatamente_um_de_profissional_e_usuario_e_compara_por_valor()
    {
        var id = Guid.NewGuid();

        PessoaComissao.Criar(id, null).Should().Be(PessoaComissao.Profissional(id));
        PessoaComissao.Criar(null, id).Should().Be(PessoaComissao.Usuario(id));
        PessoaComissao.Profissional(id).Should().NotBe(PessoaComissao.Usuario(id));

        var nenhum = () => PessoaComissao.Criar(null, null);
        var dois = () => PessoaComissao.Criar(Guid.NewGuid(), Guid.NewGuid());
        nenhum.Should().Throw<ArgumentException>();
        dois.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Vale_de_usuario_fica_sem_profissional()
    {
        var usuario = Guid.NewGuid();

        var vale = LancamentoSaldoDevedor.CriarVale(Guid.NewGuid(), PessoaComissao.Usuario(usuario), 10m, new DateOnly(2026, 9, 1), null, null);
        var fechamento = FechamentoComissao.Criar(Guid.NewGuid(), Guid.NewGuid(), PessoaComissao.Usuario(usuario), 0m, 0m, 0, null, DateTimeOffset.UtcNow);

        (vale.ProfissionalId, vale.UsuarioId).Should().Be(((Guid?)null, (Guid?)usuario));
        fechamento.Pessoa.Should().Be(PessoaComissao.Usuario(usuario));
    }
}
