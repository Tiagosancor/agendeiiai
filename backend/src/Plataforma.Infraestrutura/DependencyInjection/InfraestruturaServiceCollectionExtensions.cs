using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Infraestrutura.Autenticacao;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Infraestrutura.MultiTenant;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Profissionais;
using Plataforma.Infraestrutura.Seguranca;
using Plataforma.Infraestrutura.Servicos;
using Plataforma.Infraestrutura.Usuarios;

namespace Plataforma.Infraestrutura.DependencyInjection;

public static class InfraestruturaServiceCollectionExtensions
{
    private const string ChaveConnectionString = "ConnectionStrings:Padrao";

    /// <summary>
    /// Registra toda a infraestrutura: persistência (EF Core + Postgres), contexto de
    /// multi-tenant, opções de marca (validadas na inicialização — seção 8.5.2) e jobs
    /// (Hangfire). Falha rápido se faltar configuração obrigatória.
    /// </summary>
    public static IServiceCollection AdicionarInfraestrutura(
        this IServiceCollection servicos, IConfiguration configuracao)
    {
        // Só valida que a connection string existe — não guarda o valor aqui. Guardar
        // guardaria a configuração de ANTES de qualquer override adicionado depois deste
        // ponto (é exatamente isso que o WebApplicationFactory dos testes de integração
        // faz), então cada registro abaixo relê a connection string na hora de usar, a
        // partir do IConfiguration resolvido do container (já com todos os overrides).
        if (string.IsNullOrWhiteSpace(configuracao[ChaveConnectionString]))
        {
            throw new InvalidOperationException(
                "A connection string 'ConnectionStrings:Padrao' (variável ConnectionStrings__Padrao) é obrigatória.");
        }

        servicos
            .AddOptions<OpcoesMarca>()
            .Bind(configuracao.GetSection(OpcoesMarca.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesJwt>()
            .Bind(configuracao.GetSection(OpcoesJwt.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesCriptografiaCpf>()
            .Bind(configuracao.GetSection(OpcoesCriptografiaCpf.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos.AddDbContext<PlataformaDbContext>((sp, opcoes) => opcoes
            .UseNpgsql(
                ObterConnectionString(sp),
                npgsql =>
                {
                    npgsql.MigrationsAssembly(typeof(PlataformaDbContext).Assembly.FullName);

                    // Tolerância à hibernação dos planos gratuitos (seção 8.5.6): reconecta com
                    // backoff em vez de derrubar a requisição na primeira falha transitória.
                    npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null);
                })
            // Converte PascalCase (C#) para snake_case (Postgres) automaticamente — é a
            // convenção de nomes de tabela e coluna do projeto (seção 0, comentários em pt-BR).
            .UseSnakeCaseNamingConvention());

        servicos.AddScoped<IContextoNegocio, ContextoNegocio>();
        servicos.AddScoped<IConsultaNegocioPublico, ConsultaNegocioPublico>();

        // Segurança (seção 8.4): hash de senha, criptografia de CPF, emissão de JWT.
        servicos.AddSingleton<ISenhaHasher, SenhaHasher>();
        servicos.AddSingleton<ICriptografiaCpf, CriptografiaCpf>();
        servicos.AddSingleton<IGeradorTokenAcesso, GeradorTokenAcesso>();

        servicos.AddScoped<IServicoAutenticacao, ServicoAutenticacao>();

        // CRUDs da Sprint 1 (seção 7).
        servicos.AddScoped<IGerenciadorUsuarios, GerenciadorUsuarios>();
        servicos.AddScoped<IGerenciadorProfissionais, GerenciadorProfissionais>();
        servicos.AddScoped<IGerenciadorCategorias, GerenciadorCategorias>();
        servicos.AddScoped<IGerenciadorServicos, GerenciadorServicos>();
        servicos.AddScoped<IGerenciadorClientes, GerenciadorClientes>();
        servicos.AddScoped<IGerenciadorPerfilNegocio, GerenciadorPerfilNegocio>();

        servicos
            .AddHealthChecks()
            .AddNpgSql(sp => ObterConnectionString(sp), name: "postgres")
            .AddCheck<VerificacaoExtensaoBtreeGistHealthCheck>("btree_gist");

        servicos.AddHangfire((sp, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UsePostgreSqlStorage(opcoes => opcoes.UseNpgsqlConnection(ObterConnectionString(sp))));

        servicos.AddHangfireServer();

        return servicos;
    }

    private static string ObterConnectionString(IServiceProvider servicos) =>
        servicos.GetRequiredService<IConfiguration>()[ChaveConnectionString]!;
}
