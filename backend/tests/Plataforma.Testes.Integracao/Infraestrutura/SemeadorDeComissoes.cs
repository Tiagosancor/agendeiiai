using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Api.Controllers.Painel;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>Arranjo dos testes de comissões e quinzenas: agendamentos direto no banco (horários passados e de madrugada), profissionais e usuários logados.</summary>
public static class SemeadorDeComissoes
{
    public static readonly TimeZoneInfo Fuso = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    /// <summary>Hoje no fuso do negócio, deslocado.</summary>
    public static DateOnly Dia(int deslocamento) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Fuso).DateTime).AddDays(deslocamento);

    /// <summary>Hora local do negócio convertida para UTC.</summary>
    public static DateTimeOffset Local(DateOnly dia, int hora, int minuto)
    {
        var local = dia.ToDateTime(new TimeOnly(hora, minuto), DateTimeKind.Unspecified);
        return new DateTimeOffset(local, Fuso.GetUtcOffset(local)).ToUniversalTime();
    }

    public static Task<HttpResponseMessage> DefinirComissaoAsync(
        this HttpClient cliente, Guid profissionalId, decimal percentual, bool acertoPorQuinzena = false) =>
        cliente.PutAsJsonAsync($"/painel/profissionais/{profissionalId}/comissao", new ConfiguracaoComissao(percentual, acertoPorQuinzena));

    public static Task<Guid> CriarProfissionalAsync(this PlataformaWebApplicationFactory fabrica, Guid negocioId, string nome) =>
        fabrica.NoBancoAsync(async db =>
        {
            var profissional = Profissional.Criar(negocioId, nome);
            db.Profissionais.Add(profissional);
            await db.SaveChangesAsync();
            return profissional.Id;
        });

    /// <summary>Agendamento confirmado direto no banco (sem expediente). Serviços de R$ 50,00 por padrão.</summary>
    public static Task<Guid> SemearAgendamentoAsync(
        this PlataformaWebApplicationFactory fabrica, Guid negocioId, Guid profissionalId, DateTimeOffset inicio,
        decimal desconto = 0m, decimal[]? precos = null, Guid? servicoId = null) =>
        fabrica.NoBancoAsync(async db =>
        {
            var cliente = Cliente.Criar(negocioId, "Cliente Sobrenome", TelefoneE164.Criar($"+55719{Random.Shared.Next(10000000, 99999999)}"));
            db.Clientes.Add(cliente);

            var itens = (precos ?? [50m]).Select((preco, i) => new ItemServicoAgendamento(servicoId ?? Guid.NewGuid(), $"Serviço {i + 1}", preco, 20)).ToList();
            var agendamento = Agendamento.CriarConfirmado(negocioId, profissionalId, cliente.Id, inicio, itens);
            if (desconto > 0)
                typeof(Agendamento).GetProperty(nameof(Agendamento.DescontoAplicado))!.SetValue(agendamento, desconto);

            db.Agendamentos.Add(agendamento);
            await db.SaveChangesAsync();
            return agendamento.Id;
        });

    /// <summary>Semeia e conclui pela API (grava a comissão como no uso real).</summary>
    public static async Task<Guid> SemearEConcluirAsync(
        this PlataformaWebApplicationFactory fabrica, HttpClient admin, Guid negocioId, Guid profissionalId, DateTimeOffset inicio)
    {
        var id = await fabrica.SemearAgendamentoAsync(negocioId, profissionalId, inicio);
        (await admin.PostAsync($"/painel/agendamentos/{id}/concluir", null)).EnsureSuccessStatusCode();
        return id;
    }

    /// <summary>Usuário novo no negócio, opcionalmente vinculado a um profissional, já logado.</summary>
    public static async Task<HttpClient> LogarNovoUsuarioAsync(
        this PlataformaWebApplicationFactory fabrica, Guid negocioId, Perfil perfil, Guid? profissionalId = null,
        params Permissao[] permissoesExtras)
    {
        var email = $"u-{Guid.NewGuid():N}@teste.com";
        await fabrica.NoBancoAsync(async db =>
        {
            var hasher = fabrica.Services.GetRequiredService<ISenhaHasher>();
            var usuario = Usuario.Criar(negocioId, "Usuário", email, perfil, hasher.Hash(SemeadorDeUsuarios.SenhaPadrao));
            if (profissionalId is { } id)
                usuario.VincularProfissional(id);
            foreach (var permissao in permissoesExtras)
                usuario.ConcederPermissao(permissao);
            db.Usuarios.Add(usuario);
            await db.SaveChangesAsync();
            return 0;
        });

        using var anonimo = fabrica.CreateClient();
        var login = await anonimo.PostAsJsonAsync("/painel/auth/login", new RequisicaoLogin(email, SemeadorDeUsuarios.SenhaPadrao));
        var token = (await login.Content.ReadFromJsonAsync<RespostaLogin>())!.AccessToken;

        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return cliente;
    }

    public static async Task<T> NoBancoAsync<T>(this PlataformaWebApplicationFactory fabrica, Func<PlataformaDbContext, Task<T>> acao)
    {
        using var escopo = fabrica.Services.CreateScope();
        return await acao(escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>());
    }
}
