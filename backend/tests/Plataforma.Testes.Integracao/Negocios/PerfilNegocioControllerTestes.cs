using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using Xunit;

namespace Plataforma.Testes.Integracao.Negocios;

[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class PerfilNegocioControllerTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public PerfilNegocioControllerTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Obter_perfil_do_negocio_recem_criado()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var perfil = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");

        perfil!.NomeExibido.Should().Be("Negócio de Teste");
        perfil.Tipo.Should().Be("Barbearia");
    }

    [Fact]
    public async Task Atualizar_perfil_do_negocio_persiste_marca_endereco_e_horario()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);

        var horario = Enumerable.Range(0, 7)
            .Select(dia => new HorarioFuncionamentoDiaDto(
                dia, dia == 0 ? null : new TimeOnly(9, 0), dia == 0 ? null : new TimeOnly(18, 0), Fechado: dia == 0))
            .ToList();

        var atualizacao = new AtualizarPerfilNegocio(
            NomeExibido: "Acme Barbearia Renovada",
            LogoUrl: "https://exemplo.com/logo.png",
            CorPrimaria: "#000000",
            CorSecundaria: "#FFFFFF",
            TituloPagina: "Bem-vindo",
            SubtituloPagina: "O melhor corte da cidade",
            TextoSobre: "Uma barbearia tradicional.",
            Bairro: "Centro",
            Cidade: "Salvador",
            Rua: "Rua das Flores",
            Numero: "123",
            Cep: "40000-000",
            Telefone: "+557133334444",
            EmailContato: "contato@acme.dev",
            Instagram: "@acme",
            Facebook: null,
            WhatsApp: "+5571988887777",
            WhatsAppAtivoParaConfirmacoes: true,
            HorarioFuncionamento: horario);

        (await cliente.PutAsJsonAsync("/painel/negocio", atualizacao)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var perfilAtualizado = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");

        perfilAtualizado!.NomeExibido.Should().Be("Acme Barbearia Renovada");
        perfilAtualizado.Bairro.Should().Be("Centro");
        perfilAtualizado.Instagram.Should().Be("@acme");
        perfilAtualizado.HorarioFuncionamento.Should().HaveCount(7);
        perfilAtualizado.HorarioFuncionamento.Single(h => h.DiaSemana == 0).Fechado.Should().BeTrue();
        perfilAtualizado.HorarioFuncionamento.Single(h => h.DiaSemana == 1).Abertura.Should().Be(new TimeOnly(9, 0));
    }
}
