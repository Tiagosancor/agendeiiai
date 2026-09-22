using Hangfire;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Plataforma.Api.Middlewares;
using Plataforma.Infraestrutura.DependencyInjection;
using Plataforma.Infraestrutura.Opcoes;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog é o provedor de log da aplicação inteira. Nunca logamos dado pessoal
// (telefone mascarado, sem código de verificação em texto puro — seção 8.1.6).
builder.Host.UseSerilog((contexto, configuracaoLog) => configuracaoLog
    .ReadFrom.Configuration(contexto.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Aplicacao", "Plataforma.Api"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AdicionarInfraestrutura(builder.Configuration);

var dominioBase = builder.Configuration[$"{OpcoesMarca.Secao}:Dominio"];
builder.Services.AddCors(opcoes => opcoes.AddPolicy("PadraoPlataforma", politica =>
{
    if (string.IsNullOrWhiteSpace(dominioBase))
    {
        // Sem domínio configurado (ex.: alguns testes), não libera nenhuma origem.
        return;
    }

    // Origens permitidas: o domínio base, o painel (app.{dominio}) e qualquer
    // subdomínio de negócio ({slug}.{dominio}) — seção 8.3.5.
    politica
        .SetIsOriginAllowed(origem =>
        {
            if (!Uri.TryCreate(origem, UriKind.Absolute, out var uri))
                return false;

            var host = uri.Host.ToLowerInvariant();
            var dominio = dominioBase.ToLowerInvariant();

            return host == dominio || host.EndsWith("." + dominio, StringComparison.Ordinal);
        })
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials();
}));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    // Em produção as migrations são um passo explícito de deploy (seção 8.5.5).
    // Em dev, aplicar automaticamente é o que faz "docker compose up" subir tudo pronto.
    using var escopoInicializacao = app.Services.CreateScope();
    var dbContext = escopoInicializacao.ServiceProvider
        .GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>();
    await dbContext.Database.MigrateAsync();
    await Plataforma.Infraestrutura.Persistencia.SemeadorDesenvolvimento.SemearAsync(dbContext);
}

app.UseSerilogRequestLogging();

app.UseMiddleware<TratamentoGlobalErrosMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseCors("PadraoPlataforma");

app.UseMiddleware<ResolucaoNegocioMiddleware>();

app.UseAuthorization();

app.MapControllers();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = Plataforma.Api.Saude.EscritorRespostaSaude.EscreverAsync,
});

if (app.Environment.IsDevelopment())
{
    app.UseHangfireDashboard("/jobs");
}

app.Run();

// Necessário para o WebApplicationFactory<Program> dos testes de integração.
public partial class Program;
