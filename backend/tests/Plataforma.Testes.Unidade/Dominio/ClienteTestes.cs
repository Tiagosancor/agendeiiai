using FluentAssertions;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Xunit;

namespace Plataforma.Testes.Unidade.Dominio;

public sealed class ClienteTestes
{
    [Fact]
    public void Criar_preenche_os_campos_e_usa_origem_painel_por_padrao()
    {
        var telefone = TelefoneE164.Criar("+5571988887777");
        var cliente = Cliente.Criar(Guid.NewGuid(), "João", telefone);

        cliente.Nome.Should().Be("João");
        cliente.Telefone.Should().Be(telefone);
        cliente.Origem.Should().Be(OrigemCliente.Painel);
    }

    [Fact]
    public void PreencherEmailSeVazio_nao_sobrescreve_email_existente()
    {
        var cliente = Cliente.Criar(
            Guid.NewGuid(), "João", TelefoneE164.Criar("+5571988887777"), email: "joao@existente.com");

        cliente.PreencherEmailSeVazio("outro@novo.com");

        cliente.Email.Should().Be("joao@existente.com");
    }

    [Fact]
    public void PreencherEmailSeVazio_preenche_quando_nao_tinha_email()
    {
        var cliente = Cliente.Criar(Guid.NewGuid(), "João", TelefoneE164.Criar("+5571988887777"));

        cliente.PreencherEmailSeVazio("novo@email.com");

        cliente.Email.Should().Be("novo@email.com");
    }

    [Fact]
    public void Criar_sem_nome_lanca_excecao()
    {
        var acao = () => Cliente.Criar(Guid.NewGuid(), "  ", TelefoneE164.Criar("+5571988887777"));
        acao.Should().Throw<ArgumentException>();
    }
}
