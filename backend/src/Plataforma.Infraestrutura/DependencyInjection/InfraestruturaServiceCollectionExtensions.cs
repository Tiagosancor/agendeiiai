using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Aplicacao.Autenticacao;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Contato;
using Plataforma.Aplicacao.Cupons;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Aplicacao.Fidelidade;
using Plataforma.Aplicacao.Ics;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Publico;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Aplicacao.Verificacao;
using Plataforma.Infraestrutura.Administracao;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Assinaturas;
using Plataforma.Infraestrutura.Autenticacao;
using Plataforma.Infraestrutura.Cadastro;
using Plataforma.Infraestrutura.Clientes;
using Plataforma.Infraestrutura.Contato;
using Plataforma.Infraestrutura.Cupons;
using Plataforma.Infraestrutura.Financeiro;
using Plataforma.Infraestrutura.Fidelidade;
using Plataforma.Infraestrutura.Ics;
using Plataforma.Infraestrutura.MultiTenant;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Notificacoes;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Infraestrutura.Profissionais;
using Plataforma.Infraestrutura.Publico;
using Plataforma.Infraestrutura.Seguranca;
using Plataforma.Infraestrutura.Servicos;
using Plataforma.Infraestrutura.Usuarios;
using Plataforma.Infraestrutura.Verificacao;

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

        servicos
            .AddOptions<OpcoesEmail>()
            .Bind(configuracao.GetSection(OpcoesEmail.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesWhatsApp>()
            .Bind(configuracao.GetSection(OpcoesWhatsApp.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesVerificacao>()
            .Bind(configuracao.GetSection(OpcoesVerificacao.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesAgendamentoPublico>()
            .Bind(configuracao.GetSection(OpcoesAgendamentoPublico.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        servicos
            .AddOptions<OpcoesLembretes>()
            .Bind(configuracao.GetSection(OpcoesLembretes.Secao))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Sem ValidateOnStart nem ValidateDataAnnotations de propósito: é só um bool, e
        // precisa ser lido via IOptionsMonitor (não IOptions) pra dar pra ligar/desligar o
        // modo manutenção em produção sem reiniciar a API (ver ModoManutencaoMiddleware).
        servicos.AddOptions<OpcoesManutencao>().Bind(configuracao.GetSection(OpcoesManutencao.Secao));

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

        // Agenda e disponibilidade (Sprint 2 — seção 8.2).
        servicos.AddScoped<IGerenciadorHorariosTrabalho, GerenciadorHorariosTrabalho>();
        servicos.AddScoped<IGerenciadorBloqueios, GerenciadorBloqueios>();
        servicos.AddScoped<IGerenciadorProfissionalServicos, GerenciadorProfissionalServicos>();
        servicos.AddScoped<IConsultaDisponibilidade, ConsultaDisponibilidade>();
        servicos.AddScoped<IServicoAgendamentos, ServicoAgendamentos>();
        servicos.AddScoped<JobExpirarReservas>();

        // Página pública, assistente e código de confirmação (Sprint 3 — seção 8.1/6).
        servicos.AddSingleton<IServicoTokenPublico, ServicoTokenPublico>();
        servicos.AddSingleton<IGeradorIcs, GeradorIcs>();
        servicos.AddScoped<IServicoVerificacao, ServicoVerificacao>();
        servicos.AddScoped<INotificador, Notificador>();
        servicos.AddScoped<IGerenciadorCupons, GerenciadorCupons>();
        servicos.AddScoped<IServicoContato, ServicoContato>();
        servicos.AddScoped<IConsultaCatalogoPublico, ConsultaCatalogoPublico>();

        // Notificações ao profissional, lembretes e financeiro (Sprint 4 — seção 9/7).
        servicos.AddScoped<JobEnviarLembretes>();
        servicos.AddScoped<IGerenciadorPagamentos, GerenciadorPagamentos>();
        servicos.AddScoped<IServicoFinanceiro, ServicoFinanceiro>();
        servicos.AddScoped<IGerenciadorFidelidade, GerenciadorFidelidade>();

        // Planos e assinatura do negócio (seção 7). Um gateway real entra como mais um
        // IGatewayPagamento registrado aqui — o webhook descobre o provedor pela rota.
        servicos.AddOptions<OpcoesCobranca>().Bind(configuracao.GetSection(OpcoesCobranca.Secao));
        servicos.AddScoped<IGatewayPagamento, GatewayPagamentoManual>();
        servicos.AddScoped<IProcessadorWebhookPagamento, ProcessadorWebhookPagamento>();
        servicos.AddScoped<JobAtualizarAssinaturas>();
        servicos.AddScoped<IConsultaSituacaoAssinatura, ConsultaSituacaoAssinatura>();

        // Cadastro de negócio novo e checklist do primeiro acesso (seção 6.5).
        servicos.AddScoped<IServicoCadastro, ServicoCadastro>();
        servicos.AddScoped<IServicoPrimeirosPassos, ServicoPrimeirosPassos>();

        // Tela de assinatura do negócio e administração da plataforma (seção 7).
        servicos.AddScoped<IServicoAssinaturaNegocio, ServicoAssinaturaNegocio>();
        servicos.AddScoped<IAdministracaoPlataforma, AdministracaoPlataforma>();
        servicos.AddOptions<OpcoesCaptcha>().Bind(configuracao.GetSection(OpcoesCaptcha.Secao));
        servicos.AddHttpClient<IVerificadorCaptcha, VerificadorCaptchaTurnstile>();

        // E-mail e WhatsApp: Fake em dev/testes, provedor real escolhido em runtime pela
        // configuração (seção 4) — nunca hardcoded, senão os testes de integração (que não
        // configuram Resend/Meta) tentariam bater numa API externa de verdade.
        servicos.AddScoped<EmailSenderFake>();
        servicos.AddHttpClient<EmailSenderResend>();
        servicos.AddScoped<IEmailSender>(sp =>
            sp.GetRequiredService<IOptions<OpcoesEmail>>().Value.Provedor == ProvedorEmail.Resend
                ? sp.GetRequiredService<EmailSenderResend>()
                : sp.GetRequiredService<EmailSenderFake>());

        servicos.AddScoped<MensageriaWhatsAppFake>();
        servicos.AddHttpClient<MensageriaWhatsAppOficial>();
        servicos.AddScoped<IMensageriaWhatsApp>(sp =>
            sp.GetRequiredService<IOptions<OpcoesWhatsApp>>().Value.Provedor == ProvedorWhatsApp.Oficial
                ? sp.GetRequiredService<MensageriaWhatsAppOficial>()
                : sp.GetRequiredService<MensageriaWhatsAppFake>());

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
        servicos.AddHostedService(sp => new Plataforma.Infraestrutura.Jobs.RegistroJobsRecorrentes(
            sp, sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Plataforma.Infraestrutura.Jobs.RegistroJobsRecorrentes>>()));

        return servicos;
    }

    private static string ObterConnectionString(IServiceProvider servicos) =>
        servicos.GetRequiredService<IConfiguration>()[ChaveConnectionString]!;
}
