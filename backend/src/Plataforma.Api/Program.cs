using System.Text;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Api.Autenticacao;
using Plataforma.Api.Middlewares;
using Plataforma.Dominio.Usuarios;
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

builder.Services.AddControllers(opcoes => opcoes.Filters.Add<Plataforma.Api.Assinaturas.FiltroAssinaturaSuspensa>())
    // Enums como texto no JSON ("Administrador", não "1") — mais legível pro frontend e
    // pro Swagger. Continua aceitando número na entrada (comportamento padrão do
    // conversor), então não quebra nada que já mandava o valor numérico.
    .AddJsonOptions(opcoes => opcoes.JsonSerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AdicionarInfraestrutura(builder.Configuration);

// JWT do painel (seção 4). Os parâmetros de validação só são lidos quando o primeiro
// token chega (via IOptions<OpcoesJwt>, resolvido em tempo de execução) — não aqui em
// Program.cs, para não travar a configuração de antes de qualquer override de teste
// (mesma armadilha documentada em docs/decisoes.md para a connection string).
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer()
    // Administração da plataforma: mesma chave, audiência própria — o token do painel de um
    // negócio nunca passa aqui, e o da plataforma nunca passa no painel (seção 8.6.7).
    .AddJwtBearer(ClaimsPlataforma.EsquemaPlataforma);

builder.Services
    .AddOptions<JwtBearerOptions>(ClaimsPlataforma.EsquemaPlataforma)
    .Configure<Microsoft.Extensions.Options.IOptions<OpcoesJwt>>((jwtBearerOpcoes, opcoesJwt) =>
    {
        var opcoes = opcoesJwt.Value;

        jwtBearerOpcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = opcoes.Emissor,
            ValidAudience = opcoes.AudienciaPlataforma,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.ChaveSecreta)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<Microsoft.Extensions.Options.IOptions<OpcoesJwt>>((jwtBearerOpcoes, opcoesJwt) =>
    {
        var opcoes = opcoesJwt.Value;

        jwtBearerOpcoes.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = opcoes.Emissor,
            ValidAudience = opcoes.Audiencia,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opcoes.ChaveSecreta)),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

// Autorização por permissão, nunca só por perfil (seção 4) — uma policy por valor do
// catálogo fixo de Permissao, checando a claim que o GeradorTokenAcesso emite.
builder.Services.AddAuthorization(opcoes =>
{
    foreach (var permissao in Enum.GetValues<Permissao>())
    {
        var nomeDaPolicy = permissao.ToString();
        opcoes.AddPolicy(nomeDaPolicy, politica => politica.RequireClaim(ClaimsPlataforma.Permissao, nomeDaPolicy));
    }

    // Tela de assinatura: só o perfil Administrador do negócio (seção 7), não uma permissão avulsa.
    opcoes.AddPolicy(ClaimsPlataforma.PoliticaSomenteAdministradorNegocio, politica =>
        politica.RequireClaim(ClaimsPlataforma.Perfil, nameof(Perfil.Administrador)));

    opcoes.AddPolicy(ClaimsPlataforma.PoliticaAdministradorPlataforma, politica => politica
        .AddAuthenticationSchemes(ClaimsPlataforma.EsquemaPlataforma)
        .RequireClaim(ClaimsPlataforma.Perfil, ClaimsPlataforma.PerfilAdministradorPlataforma));
});

builder.Services.AddTransient<IClaimsTransformation, ContextoNegocioClaimsTransformation>();

// Camada extra de rate limit por IP (seção 8.1.5) — o limite por telefone/negócio, que é o
// que importa de verdade contra abuso do código de confirmação, já é reforçado dentro do
// ServicoVerificacao (banco); isto aqui é só a primeira barreira, barata, contra um único
// IP martelando o endpoint.
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    opcoes.AddPolicy("CodigoVerificacaoPorIp", contexto => RateLimitPartition.GetFixedWindowLimiter(
        contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip",
        _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromMinutes(1),
            PermitLimit = 10,
            QueueLimit = 0,
        }));

    // Cadastro de negócio (seção 8.6.1): envio de código, validação e criação da conta; e a
    // checagem de slug, mais frouxa porque roda enquanto a pessoa digita. Limites lidos na
    // hora (não antes do Build) para os testes poderem sobrescrever.
    opcoes.AddPolicy(Plataforma.Api.Controllers.Cadastro.CadastroController.PoliticaPorIp, contexto =>
        LimitePorIpPorMinuto(contexto, "Cadastro:LimitePorIpPorMinuto", padrao: 10));
    opcoes.AddPolicy(Plataforma.Api.Controllers.Cadastro.CadastroController.PoliticaSlugPorIp, contexto =>
        LimitePorIpPorMinuto(contexto, "Cadastro:LimiteSlugPorIpPorMinuto", padrao: 60));
    opcoes.AddPolicy(Plataforma.Api.Controllers.Administracao.AutenticacaoPlataformaController.PoliticaLoginPorIp, contexto =>
        LimitePorIpPorMinuto(contexto, "Plataforma:LimiteLoginPorIpPorMinuto", padrao: 5));

    static RateLimitPartition<string> LimitePorIpPorMinuto(HttpContext contexto, string chaveConfiguracao, int padrao)
    {
        var limite = contexto.RequestServices.GetRequiredService<IConfiguration>().GetValue(chaveConfiguracao, padrao);
        return RateLimitPartition.GetFixedWindowLimiter(
            $"{chaveConfiguracao}:{contexto.Connection.RemoteIpAddress?.ToString() ?? "sem-ip"}",
            _ => new FixedWindowRateLimiterOptions { Window = TimeSpan.FromMinutes(1), PermitLimit = limite, QueueLimit = 0 });
    }
});

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

// Comandos de linha (ex.: `dotnet Plataforma.Api.dll criar-admin-plataforma --email ...`):
// rodam e saem, sem subir o servidor nem registrar jobs.
if (await Plataforma.Api.Comandos.ComandosDeLinha.ExecutarSeHouverAsync(app.Services, args))
    return;

// Jobs recorrentes (expirar-reservas, enviar-lembretes, atualizar-assinaturas): registrados
// em segundo plano por RegistroJobsRecorrentes (AdicionarInfraestrutura), nunca aqui — um
// lock do Hangfire preso por um container anterior derrubava a subida da API inteira.

if (app.Environment.IsDevelopment())
{
    // Em produção as migrations são um passo explícito de deploy (seção 8.5.5).
    // Em dev, aplicar automaticamente é o que faz "docker compose up" subir tudo pronto.
    using var escopoInicializacao = app.Services.CreateScope();
    var dbContext = escopoInicializacao.ServiceProvider
        .GetRequiredService<Plataforma.Infraestrutura.Persistencia.PlataformaDbContext>();
    var senhaHasher = escopoInicializacao.ServiceProvider.GetRequiredService<ISenhaHasher>();
    await dbContext.Database.MigrateAsync();
    await Plataforma.Infraestrutura.Persistencia.SemeadorDesenvolvimento.SemearAsync(dbContext, senhaHasher);
}

// Atrás de um reverse proxy (Caddy/VPS, Render — seção "Caminho 2" de docs/deploy.md), a
// API só é alcançada pelo proxy, nunca direto pela internet — por isso confiar em
// X-Forwarded-For aqui é seguro (não dá pra um cliente externo forjar o cabeçalho e
// se passar por outro IP, já que o proxy sempre sobrescreve/adiciona o dele por cima).
// Sem isso, o IP de consentimento do cliente (seção 8.4) seria sempre o IP do proxy, não
// o do cliente de verdade.
var opcoesCabecalhosEncaminhados = new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
// "KnownIPNetworks = { }"/"KnownProxies = { }" no inicializador de objeto é um
// collection initializer — adiciona zero itens à lista padrão já existente (que traz
// loopback), não a esvazia. Sem o .Clear() explícito, o X-Forwarded-For do proxy real
// (gateway do Docker, nunca loopback) era silenciosamente ignorado e o IP de
// consentimento (seção 8.4) gravava o IP do container em vez do IP do cliente.
opcoesCabecalhosEncaminhados.KnownIPNetworks.Clear();
opcoesCabecalhosEncaminhados.KnownProxies.Clear();
app.UseForwardedHeaders(opcoesCabecalhosEncaminhados);

app.UseSerilogRequestLogging();

app.UseMiddleware<TratamentoGlobalErrosMiddleware>();

// Modo manutenção (seção 8.5.8) — antes de qualquer outra coisa (CORS, resolução de
// tenant, autenticação), pra bloquear TUDO de uma vez só enquanto ativo. /health é a
// única exceção (o middleware já trata isso internamente).
app.UseMiddleware<ModoManutencaoMiddleware>();

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

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

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
