using Microsoft.EntityFrameworkCore.Storage;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Administracao;
using Plataforma.Dominio.Auditoria;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Cadastro;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.Contato;
using Plataforma.Dominio.Cupons;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Fidelidade;
using Plataforma.Dominio.Financeiro;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Notificacoes;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Servicos;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Arquivos;
using Plataforma.Dominio.Verificacao;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Contexto EF Core único do monólito modular. Aplica automaticamente um
/// *global query filter* por <c>NegocioId</c> em toda entidade que implemente
/// <see cref="IEntidadeDoNegocio"/> (seção 8.3.1) — sem negócio resolvido no
/// <see cref="IContextoNegocio"/>, nenhuma linha dessas tabelas é visível
/// (comportamento seguro por padrão; use <c>IgnoreQueryFilters()</c> apenas em
/// rotinas administrativas explícitas e revisadas).
/// </summary>
public class PlataformaDbContext : DbContext
{
    private static readonly MethodInfo MetodoAplicarFiltroDoNegocio = typeof(PlataformaDbContext)
        .GetMethod(nameof(AplicarFiltroDoNegocio), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly IContextoNegocio _contextoNegocio;

    public PlataformaDbContext(DbContextOptions<PlataformaDbContext> options, IContextoNegocio contextoNegocio)
        : base(options)
    {
        _contextoNegocio = contextoNegocio;
    }

    public DbSet<Negocio> Negocios => Set<Negocio>();

    public DbSet<Usuario> Usuarios => Set<Usuario>();

    public DbSet<TokenAtualizacao> TokensAtualizacao => Set<TokenAtualizacao>();

    public DbSet<RedefinicaoSenha> RedefinicoesSenha => Set<RedefinicaoSenha>();

    public DbSet<AjusteValorAtendimento> AjustesValorAtendimento => Set<AjusteValorAtendimento>();

    public DbSet<Plataforma.Dominio.Comissoes.PeriodoComissao> PeriodosComissao => Set<Plataforma.Dominio.Comissoes.PeriodoComissao>();

    public DbSet<Plataforma.Dominio.Comissoes.FechamentoComissao> FechamentosComissao => Set<Plataforma.Dominio.Comissoes.FechamentoComissao>();

    public DbSet<Profissional> Profissionais => Set<Profissional>();

    public DbSet<Categoria> Categorias => Set<Categoria>();

    public DbSet<Servico> Servicos => Set<Servico>();

    public DbSet<Cliente> Clientes => Set<Cliente>();

    public DbSet<LembreteRetornoEnvio> LembretesRetornoEnvios => Set<LembreteRetornoEnvio>();

    public DbSet<HorarioTrabalho> HorariosTrabalho => Set<HorarioTrabalho>();

    public DbSet<BloqueioAgenda> BloqueiosAgenda => Set<BloqueioAgenda>();

    public DbSet<ProfissionalServico> ProfissionalServicos => Set<ProfissionalServico>();

    public DbSet<Agendamento> Agendamentos => Set<Agendamento>();

    public DbSet<Cupom> Cupons => Set<Cupom>();

    public DbSet<CodigoVerificacao> CodigosVerificacao => Set<CodigoVerificacao>();

    public DbSet<MensagemContato> MensagensContato => Set<MensagemContato>();

    public DbSet<Pagamento> Pagamentos => Set<Pagamento>();

    public DbSet<Produto> Produtos => Set<Produto>();

    public DbSet<MovimentoEstoque> MovimentosEstoque => Set<MovimentoEstoque>();

    public DbSet<VendaProduto> VendasProduto => Set<VendaProduto>();

    public DbSet<Plataforma.Dominio.Comissoes.LancamentoSaldoDevedor> LancamentosSaldoDevedor => Set<Plataforma.Dominio.Comissoes.LancamentoSaldoDevedor>();

    public DbSet<Plataforma.Dominio.Comissoes.QuitacaoSaldo> QuitacoesSaldo => Set<Plataforma.Dominio.Comissoes.QuitacaoSaldo>();

    public DbSet<ProgramaFidelidade> ProgramasFidelidade => Set<ProgramaFidelidade>();

    public DbSet<SeloCliente> SelosCliente => Set<SeloCliente>();

    public DbSet<Plano> Planos => Set<Plano>();

    public DbSet<Assinatura> Assinaturas => Set<Assinatura>();

    public DbSet<HistoricoAssinatura> HistoricoAssinaturas => Set<HistoricoAssinatura>();

    public DbSet<CobrancaAssinatura> CobrancasAssinatura => Set<CobrancaAssinatura>();

    public DbSet<EventoWebhookPagamento> EventosWebhookPagamento => Set<EventoWebhookPagamento>();

    public DbSet<LogAuditoriaPlataforma> LogsAuditoriaPlataforma => Set<LogAuditoriaPlataforma>();

    public DbSet<LogAuditoriaNegocio> LogsAuditoriaNegocio => Set<LogAuditoriaNegocio>();

    public DbSet<NotificacaoProfissional> NotificacoesProfissional => Set<NotificacaoProfissional>();

    public DbSet<NotificacaoPendente> NotificacoesPendentes => Set<NotificacaoPendente>();

    public DbSet<EventoWebhookWhatsApp> EventosWebhookWhatsApp => Set<EventoWebhookWhatsApp>();

    public DbSet<CodigoCadastro> CodigosCadastro => Set<CodigoCadastro>();

    public DbSet<RegistroTesteGratis> RegistrosTesteGratis => Set<RegistroTesteGratis>();

    public DbSet<ChaveIdempotencia> ChavesIdempotencia => Set<ChaveIdempotencia>();

    public DbSet<Plataforma.Dominio.Idempotencia.RequisicaoIdempotente> RequisicoesIdempotentes => Set<Plataforma.Dominio.Idempotencia.RequisicaoIdempotente>();

    public DbSet<AdministradorPlataforma> AdministradoresPlataforma => Set<AdministradorPlataforma>();

    public DbSet<ArquivoBanco> Arquivos => Set<ArquivoBanco>();

    public DbSet<SlugAnterior> SlugsAnteriores => Set<SlugAnterior>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.InstanciaWhatsApp> InstanciasWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.InstanciaWhatsApp>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.ConversaWhatsApp> ConversasWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.ConversaWhatsApp>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.MensagemWhatsApp> MensagensWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.MensagemWhatsApp>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.PendenciaAgendamentoWhatsApp> PendenciasAgendamentoWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.PendenciaAgendamentoWhatsApp>();
    public DbSet<Plataforma.Dominio.WhatsAppAgente.PendenciaGestaoWhatsApp> PendenciasGestaoWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.PendenciaGestaoWhatsApp>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.UsoIa> UsosIa => Set<Plataforma.Dominio.WhatsAppAgente.UsoIa>();

    public DbSet<Plataforma.Dominio.WhatsAppAgente.AvisoAtendenteWhatsApp> AvisosAtendenteWhatsApp => Set<Plataforma.Dominio.WhatsAppAgente.AvisoAtendenteWhatsApp>();
    public DbSet<Plataforma.Dominio.WhatsAppAgente.RodadaAgente> RodadasAgente => Set<Plataforma.Dominio.WhatsAppAgente.RodadaAgente>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Pré-requisito da exclusion constraint de horários (seção 8.2.1) — confirmado
        // também em tempo de execução pelo health check de btree_gist (seção 8.5.5).
        modelBuilder.HasPostgresExtension("btree_gist");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PlataformaDbContext).Assembly);

        // O modelo do EF Core é montado uma vez e reaproveitado entre instâncias do
        // DbContext (cada requisição ganha uma instância nova, mas o *modelo* é cacheado).
        // Por isso o filtro é escrito como lambda de um MÉTODO DE INSTÂNCIA (não como
        // Expression.Constant fechando sobre um valor fixo): o EF Core reconhece o
        // fechamento sobre "this" e reavalia _contextoNegocio.NegocioId a cada consulta,
        // usando a instância do DbContext (e portanto o negócio) da requisição atual.
        foreach (var tipoEntidade in modelBuilder.Model.GetEntityTypes().ToList())
        {
            if (typeof(EntidadeBase).IsAssignableFrom(tipoEntidade.ClrType))
            {
                // O Id é sempre gerado no construtor da entidade (Guid.NewGuid()), nunca
                // pelo banco. Sem isso, o EF Core, ao descobrir uma entidade nova só por
                // navegação (ex.: Usuario.ConcederPermissao adicionando um UsuarioPermissao
                // a uma coleção já rastreada, sem passar por Add() explícito), vê uma chave
                // "não-padrão" e conclui — errado — que a linha já existe, gerando UPDATE em
                // vez de INSERT (e um DbUpdateConcurrencyException, já que a linha não existe).
                modelBuilder.Entity(tipoEntidade.ClrType)
                    .Property(nameof(EntidadeBase.Id))
                    .ValueGeneratedNever();
            }

            // O Npgsql só aceita gravar DateTimeOffset com Offset=0 em "timestamp with time
            // zone" — um DateTimeOffset com o offset do fuso do negócio (ex.: -03:00, comum
            // ao converter hora local pra UTC — seção 8.2.6) explode em tempo de execução.
            // Normaliza pra UTC automaticamente em toda propriedade DateTimeOffset de todo
            // mundo, pra ninguém precisar lembrar disso entidade por entidade.
            foreach (var propriedade in tipoEntidade.GetProperties())
            {
                if (propriedade.ClrType == typeof(DateTimeOffset))
                {
                    propriedade.SetValueConverter(new ValueConverter<DateTimeOffset, DateTimeOffset>(
                        v => v.ToUniversalTime(), v => v));
                }
                else if (propriedade.ClrType == typeof(DateTimeOffset?))
                {
                    propriedade.SetValueConverter(new ValueConverter<DateTimeOffset?, DateTimeOffset?>(
                        v => v.HasValue ? v.Value.ToUniversalTime() : v, v => v));
                }
            }

            if (!typeof(IEntidadeDoNegocio).IsAssignableFrom(tipoEntidade.ClrType))
                continue;

            MetodoAplicarFiltroDoNegocio
                .MakeGenericMethod(tipoEntidade.ClrType)
                .Invoke(this, [modelBuilder]);
        }

        base.OnModelCreating(modelBuilder);
    }

    private void AplicarFiltroDoNegocio<TEntidade>(ModelBuilder modelBuilder)
        where TEntidade : class, IEntidadeDoNegocio
    {
        modelBuilder.Entity<TEntidade>().HasQueryFilter(e => e.NegocioId == _contextoNegocio.NegocioId);
    }

    public override int SaveChanges()
    {
        AtualizarCarimbosDeAuditoria();
        var conversas = ConversasDeMensagensNovas();
        if (conversas.Count == 0)
            return base.SaveChanges();

        if (Database.CurrentTransaction is not null)
        {
            GuardarConversas(conversas);
            return base.SaveChanges();
        }

        return Database.CreateExecutionStrategy().Execute(() =>
        {
            using var transacao = Database.BeginTransaction();
            GuardarConversas(conversas);
            var linhas = base.SaveChanges();
            transacao.Commit();
            return linhas;
        });
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AtualizarCarimbosDeAuditoria();
        var conversas = ConversasDeMensagensNovas();
        if (conversas.Count == 0)
            return await base.SaveChangesAsync(cancellationToken);

        if (Database.CurrentTransaction is not null)
        {
            await GuardarConversasAsync(conversas, cancellationToken);
            return await base.SaveChangesAsync(cancellationToken);
        }

        return await Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transacao = await Database.BeginTransactionAsync(cancellationToken);
            await GuardarConversasAsync(conversas, cancellationToken);
            var linhas = await base.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return linhas;
        });
    }

    // ---- LGPD das conversas do agente (G11) -------------------------------------------------------------------------------------
    // Protocolo comum a todo escritor de mensagem: antes de gravar, trava a linha da conversa com FOR NO KEY UPDATE (FOR SHARE daria deadlock de upgrade quando o mesmo SaveChanges atualiza a conversa) (na MESMA transação da gravação)
    // e confere que ela não foi anonimizada. A anonimização toma FOR UPDATE na mesma linha (ServicoLgpdConversasWhatsApp), então ou ela
    // espera a gravação terminar e apaga a mensagem junto, ou a gravação espera, vê o telefone "anonimizado:..." e é desfeita.
    // Ordem de locks: a conversa vem DEPOIS de pendência, agendamento e cliente (quem já os tem os mantém; ninguém pede cliente
    // segurando a conversa). Pedido sem transação do chamador ganha uma própria (a estratégia de retry exige o ExecuteAsync).

    private List<(Guid Id, Guid NegocioId)> ConversasDeMensagensNovas() =>
        ChangeTracker.Entries<Plataforma.Dominio.WhatsAppAgente.MensagemWhatsApp>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => (e.Entity.ConversaId, e.Entity.NegocioId))
            .Distinct()
            .ToList();

    private System.Data.Common.DbCommand ComandoDeGuarda(List<(Guid Id, Guid NegocioId)> conversas)
    {
        var comando = Database.GetDbConnection().CreateCommand();
        comando.Transaction = Database.CurrentTransaction!.GetDbTransaction();
        comando.CommandText =
            "SELECT telefone_cliente FROM conversas_whatsapp WHERE id = ANY(@ids) AND negocio_id = ANY(@negocios) FOR NO KEY UPDATE";
        comando.Parameters.Add(new Npgsql.NpgsqlParameter("ids", conversas.Select(c => c.Id).ToArray()));
        comando.Parameters.Add(new Npgsql.NpgsqlParameter("negocios", conversas.Select(c => c.NegocioId).Distinct().ToArray()));
        return comando;
    }

    private void GuardarConversas(List<(Guid Id, Guid NegocioId)> conversas)
    {
        using var comando = ComandoDeGuarda(conversas);
        using var leitor = comando.ExecuteReader();
        while (leitor.Read())
        {
            if (leitor.GetString(0).StartsWith(Plataforma.Dominio.WhatsAppAgente.ConversaWhatsApp.PrefixoTelefoneAnonimizado, StringComparison.Ordinal))
                throw new Plataforma.Infraestrutura.WhatsAppAgente.ConversaAnonimizadaException(conversas[0].Id);
        }
    }

    private async Task GuardarConversasAsync(List<(Guid Id, Guid NegocioId)> conversas, CancellationToken cancellationToken)
    {
        await using var comando = ComandoDeGuarda(conversas);
        await using var leitor = await comando.ExecuteReaderAsync(cancellationToken);
        while (await leitor.ReadAsync(cancellationToken))
        {
            if (leitor.GetString(0).StartsWith(Plataforma.Dominio.WhatsAppAgente.ConversaWhatsApp.PrefixoTelefoneAnonimizado, StringComparison.Ordinal))
                throw new Plataforma.Infraestrutura.WhatsAppAgente.ConversaAnonimizadaException(conversas[0].Id);
        }
    }

    private void AtualizarCarimbosDeAuditoria()
    {
        var agora = DateTimeOffset.UtcNow;

        foreach (var entrada in ChangeTracker.Entries<EntidadeBase>())
        {
            if (entrada.State == EntityState.Modified)
                entrada.Entity.AtualizadoEm = agora;
        }
    }
}
