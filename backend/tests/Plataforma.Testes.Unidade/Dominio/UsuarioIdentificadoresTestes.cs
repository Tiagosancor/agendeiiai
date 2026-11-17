using FluentAssertions;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Auditoria;
using Plataforma.Infraestrutura.MultiTenant;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class UsuarioIdentificadoresTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 16, 12, 0, 0, TimeSpan.Zero);

    private static Usuario ComTudo() => Usuario.Criar(
        Guid.NewGuid(), "Ana", "ana@teste.com", NomeUsuario.Criar("ana.silva"), TelefoneE164.Criar("(71) 8888-7777"),
        Perfil.Recepcionista, "hash");

    [Fact]
    public void Criar_sem_nenhum_identificador_e_recusado()
    {
        var criar = () => Usuario.Criar(Guid.NewGuid(), "Ana", null, null, null, Perfil.Profissional, "hash");
        criar.Should().Throw<IdentificadorObrigatorioException>();
    }

    [Fact]
    public void Criar_so_com_nome_de_usuario_ou_so_com_telefone_funciona()
    {
        var porNome = Usuario.Criar(Guid.NewGuid(), "Ana", "  ", NomeUsuario.Criar("ana.s"), null, Perfil.Profissional, "hash");
        porNome.Email.Should().BeNull();

        var porTelefone = Usuario.Criar(Guid.NewGuid(), "Ana", null, null, TelefoneE164.Criar("71988887777"), Perfil.Profissional, "hash");
        porTelefone.TelefoneLogin!.Valor.Should().Be("+5571988887777");
    }

    [Fact]
    public void Telefone_de_login_e_guardado_na_forma_canonica()
    {
        var usuario = ComTudo();
        usuario.TelefoneLogin!.Valor.Should().Be("+5571988887777");

        usuario.DefinirTelefoneLogin(TelefoneE164.Criar("+557177776666"));
        usuario.TelefoneLogin!.Valor.Should().Be("+5571977776666");
    }

    [Fact]
    public void Email_nasce_nao_verificado_e_confirmar_marca_a_data()
    {
        var usuario = ComTudo();
        usuario.EmailVerificado.Should().BeFalse();

        usuario.ConfirmarEmail("Ana@Teste.com", Agora);

        usuario.EmailVerificado.Should().BeTrue();
        usuario.EmailVerificadoEm.Should().Be(Agora);
        usuario.Email.Should().Be("ana@teste.com");
    }

    [Fact]
    public void Trocar_o_email_zera_o_verificado_mas_repetir_o_mesmo_nao()
    {
        var usuario = ComTudo();
        usuario.ConfirmarEmail("ana@teste.com", Agora);

        usuario.DefinirEmailPeloAdministrador(" ANA@teste.com ");
        usuario.EmailVerificado.Should().BeTrue();

        usuario.DefinirEmailPeloAdministrador("outra@teste.com");
        usuario.Email.Should().Be("outra@teste.com");
        usuario.EmailVerificadoEm.Should().BeNull();
    }

    [Fact]
    public void Confirmar_outro_email_troca_o_endereco_e_verifica()
    {
        var usuario = ComTudo();
        usuario.ConfirmarEmail("nova@teste.com", Agora);
        usuario.Email.Should().Be("nova@teste.com");
        usuario.EmailVerificado.Should().BeTrue();
    }

    [Theory]
    [InlineData("sem-arroba")]
    [InlineData("a@")]
    [InlineData("@b.com")]
    public void Email_invalido_e_recusado(string email)
    {
        var usuario = ComTudo();
        var definir = () => usuario.DefinirEmailPeloAdministrador(email);
        definir.Should().Throw<ArgumentException>();
        var confirmar = () => usuario.ConfirmarEmail(email, Agora);
        confirmar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Remover_o_ultimo_identificador_e_recusado_em_cada_caminho()
    {
        var soEmail = Usuario.Criar(Guid.NewGuid(), "Ana", "ana@teste.com", null, null, Perfil.Profissional, "hash");
        var removerEmail = () => soEmail.DefinirEmailPeloAdministrador(null);
        removerEmail.Should().Throw<IdentificadorObrigatorioException>();

        var soNome = Usuario.Criar(Guid.NewGuid(), "Ana", null, NomeUsuario.Criar("ana.s"), null, Perfil.Profissional, "hash");
        var removerNome = () => soNome.DefinirNomeUsuario(null);
        removerNome.Should().Throw<IdentificadorObrigatorioException>();

        var soTelefone = Usuario.Criar(Guid.NewGuid(), "Ana", null, null, TelefoneE164.Criar("71988887777"), Perfil.Profissional, "hash");
        var removerTelefone = () => soTelefone.DefinirTelefoneLogin(null);
        removerTelefone.Should().Throw<IdentificadorObrigatorioException>();
    }

    [Fact]
    public void Pode_remover_um_se_sobrar_outro()
    {
        var usuario = ComTudo();
        usuario.DefinirEmailPeloAdministrador(null);
        usuario.DefinirNomeUsuario(null);
        usuario.Email.Should().BeNull();
        usuario.NomeUsuario.Should().BeNull();
        usuario.TelefoneLogin.Should().NotBeNull();
    }

    [Fact]
    public void AlterarSenha_registra_se_foi_definida_por_terceiro()
    {
        var usuario = ComTudo();
        usuario.AlterarSenha("novo", definidaPorTerceiro: true);
        usuario.SenhaDefinidaPorTerceiro.Should().BeTrue();

        usuario.AlterarSenha("proprio");
        usuario.SenhaDefinidaPorTerceiro.Should().BeFalse();
    }

    [Fact]
    public void Excluir_solta_os_tres_identificadores_com_nulo()
    {
        var usuario = ComTudo();
        usuario.ConfirmarEmail("ana@teste.com", Agora);

        usuario.Excluir(Agora);

        usuario.Email.Should().BeNull();
        usuario.EmailVerificadoEm.Should().BeNull();
        usuario.NomeUsuario.Should().BeNull();
        usuario.TelefoneLogin.Should().BeNull();
    }

    [Fact]
    public void Rotulo_prefere_email_depois_usuario_depois_nome_e_nunca_usa_telefone()
    {
        var id = Guid.NewGuid();
        UsuarioAtual.MontarRotulo(id, "ana@teste.com", "ana.s", "Ana").Should().Be("ana@teste.com");
        UsuarioAtual.MontarRotulo(id, null, "ana.s", "Ana").Should().Be("@ana.s");
        UsuarioAtual.MontarRotulo(id, " ", null, "Ana").Should().Be("Ana");
        UsuarioAtual.MontarRotulo(id, null, null, null).Should().Be($"usuario:{id}");
    }

    [Fact]
    public void UsuarioAtual_sempre_tem_rotulo_depois_de_definido_e_o_email_so_vem_do_token()
    {
        var atual = new UsuarioAtual();
        atual.Rotulo.Should().BeNull();

        atual.Definir(Guid.NewGuid(), null, nome: "Ana");

        atual.Email.Should().BeNull();
        atual.Rotulo.Should().Be("Ana");
    }
}
