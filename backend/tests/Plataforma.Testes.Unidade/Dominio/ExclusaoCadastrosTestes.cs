using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Seguranca;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Testes.Unidade.Dominio;

/// <summary>Exclusão lógica de cadastros (seção 7): fica só o nome, e excluído não volta.</summary>
public class ExclusaoCadastrosTestes
{
    private static readonly CpfProtegido CpfQualquer = new("***.444.777-**", [1, 2, 3], [4, 5, 6], "chave-1");
    private static readonly Endereco EnderecoQualquer = new("Barra", "Salvador", "Rua A", "1", "40000-000");

    [Fact]
    public void Usuario_excluido_fica_so_com_o_nome_sem_permissoes_e_inativo()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Ana", "ana@teste.com", Perfil.Administrador, "hash",
            "+5571999990000", EnderecoQualquer, CpfQualquer);
        usuario.DefinirFoto("https://exemplo.dev/f.jpg");
        usuario.VincularProfissional(Guid.NewGuid());

        usuario.Excluir(DateTimeOffset.UtcNow);

        usuario.Nome.Should().Be("Ana");
        usuario.Excluido.Should().BeTrue();
        usuario.ExcluidoEm.Should().NotBeNull();
        usuario.Ativo.Should().BeFalse();
        usuario.Email.Should().BeEmpty();
        usuario.Telefone.Should().BeNull();
        usuario.Endereco.Should().Be(Endereco.Vazio);
        usuario.Cpf.Should().BeNull();
        usuario.FotoUrl.Should().BeNull();
        usuario.ProfissionalId.Should().BeNull();
        usuario.Permissoes.Should().BeEmpty();

        var reativar = () => usuario.Ativar();
        reativar.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Profissional_excluido_perde_os_dados_pessoais_e_nao_volta()
    {
        var profissional = Profissional.Criar(Guid.NewGuid(), "Bruno", "+5571988887777", "bruno@teste.com",
            EnderecoQualquer, CpfQualquer, "Barbeiro");
        profissional.DefinirFoto("https://exemplo.dev/b.jpg");

        profissional.Excluir(DateTimeOffset.UtcNow);

        profissional.Nome.Should().Be("Bruno");
        profissional.Funcao.Should().Be("Barbeiro");
        profissional.Ativo.Should().BeFalse();
        profissional.Telefone.Should().BeNull();
        profissional.Email.Should().BeNull();
        profissional.Endereco.Should().Be(Endereco.Vazio);
        profissional.Cpf.Should().BeNull();
        profissional.FotoUrl.Should().BeNull();

        var reativar = () => profissional.Ativar();
        reativar.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Servico_excluido_sai_da_oferta_e_nao_volta()
    {
        var servico = Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", 45m, 30);

        servico.Excluir(DateTimeOffset.UtcNow);

        servico.Ativo.Should().BeFalse();
        servico.Excluido.Should().BeTrue();
        var reativar = () => servico.Ativar();
        reativar.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Desativar_continua_sendo_reversivel()
    {
        var servico = Servico.Criar(Guid.NewGuid(), Guid.NewGuid(), "Corte", 45m, 30);

        servico.Desativar();
        servico.Ativar();

        servico.Ativo.Should().BeTrue();
    }
}
