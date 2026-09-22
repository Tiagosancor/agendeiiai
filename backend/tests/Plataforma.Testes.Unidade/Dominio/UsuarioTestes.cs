using FluentAssertions;
using Plataforma.Dominio.Usuarios;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class UsuarioTestes
{
    [Fact]
    public void Administrador_recebe_todas_as_permissoes_ao_ser_criado()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Ana", "ana@teste.com", Perfil.Administrador, "hash");

        usuario.Permissoes.Should().HaveCount(Enum.GetValues<Permissao>().Length);
        foreach (var permissao in Enum.GetValues<Permissao>())
            usuario.TemPermissao(permissao).Should().BeTrue();
    }

    [Fact]
    public void Profissional_nao_recebe_nenhuma_permissao_por_padrao()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Bia", "bia@teste.com", Perfil.Profissional, "hash");

        usuario.Permissoes.Should().BeEmpty();
    }

    [Fact]
    public void ConcederPermissao_adiciona_e_e_idempotente()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Bia", "bia@teste.com", Perfil.Profissional, "hash");

        usuario.ConcederPermissao(Permissao.GerenciarClientes);
        usuario.ConcederPermissao(Permissao.GerenciarClientes);

        usuario.TemPermissao(Permissao.GerenciarClientes).Should().BeTrue();
        usuario.Permissoes.Should().ContainSingle(p => p.Permissao == Permissao.GerenciarClientes);
    }

    [Fact]
    public void RevogarPermissao_remove()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Ana", "ana@teste.com", Perfil.Administrador, "hash");

        usuario.RevogarPermissao(Permissao.VerFinanceiro);

        usuario.TemPermissao(Permissao.VerFinanceiro).Should().BeFalse();
    }

    [Fact]
    public void Email_e_normalizado_para_minusculas()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Ana", "  ANA@TESTE.COM  ", Perfil.Administrador, "hash");
        usuario.Email.Should().Be("ana@teste.com");
    }

    [Fact]
    public void Desativar_e_ativar_alternam_o_status()
    {
        var usuario = Usuario.Criar(Guid.NewGuid(), "Ana", "ana@teste.com", Perfil.Administrador, "hash");

        usuario.Desativar();
        usuario.Ativo.Should().BeFalse();

        usuario.Ativar();
        usuario.Ativo.Should().BeTrue();
    }
}
