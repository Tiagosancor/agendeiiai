using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>Semeia negócio + usuário e devolve um <see cref="HttpClient"/> já autenticado, para os testes de autorização e CRUD do painel não repetirem esse arranjo.</summary>
public static class SemeadorDeUsuarios
{
    public const string SenhaPadrao = "SenhaForte!123";

    public static async Task<(HttpClient Cliente, Guid NegocioId, Guid UsuarioId, string Email)> CriarUsuarioELogarAsync(
        this PlataformaWebApplicationFactory fabrica, Perfil perfil, params Permissao[] permissoesExtras)
    {
        using var escopo = fabrica.Services.CreateScope();
        var dbContext = escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>();
        var senhaHasher = escopo.ServiceProvider.GetRequiredService<ISenhaHasher>();

        var negocio = Negocio.Criar(
            Slug.Criar("neg-" + Guid.NewGuid().ToString("N")[..16]), "Negócio de Teste", TipoNegocio.Barbearia);
        dbContext.Negocios.Add(negocio);

        var email = $"usuario-{Guid.NewGuid():N}@teste.com";
        var usuario = Usuario.Criar(negocio.Id, "Usuário Teste", email, perfil, senhaHasher.Hash(SenhaPadrao));

        foreach (var permissao in permissoesExtras)
            usuario.ConcederPermissao(permissao);

        dbContext.Usuarios.Add(usuario);
        await dbContext.SaveChangesAsync();

        using var clienteAnonimo = fabrica.CreateClient();
        var respostaLogin = await clienteAnonimo.PostAsJsonAsync(
            "/painel/auth/login", new RequisicaoLogin(email, SenhaPadrao));
        respostaLogin.EnsureSuccessStatusCode();

        var corpo = await respostaLogin.Content.ReadFromJsonAsync<RespostaLogin>();

        var clienteAutenticado = fabrica.CreateClient();
        clienteAutenticado.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", corpo!.AccessToken);

        return (clienteAutenticado, negocio.Id, usuario.Id, email);
    }
}
