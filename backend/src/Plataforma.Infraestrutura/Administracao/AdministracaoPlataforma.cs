using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Administracao;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Administracao;

/// <summary>
/// Única parte que cruza negócios: todo acesso desliga o filtro de tenant EXPLICITAMENTE
/// (<c>IgnoreQueryFilters</c>) e só lê negócio, assinatura, histórico, cobranças e a
/// contagem de profissionais ativos — nada de clientes finais.
/// </summary>
public sealed class AdministracaoPlataforma : IAdministracaoPlataforma
{
    private const int MinutosSessao = 60;
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly PlataformaDbContext _dbContext;
    private readonly ISenhaHasher _senhaHasher;
    private readonly OpcoesJwt _opcoesJwt;

    public AdministracaoPlataforma(PlataformaDbContext dbContext, ISenhaHasher senhaHasher, IOptions<OpcoesJwt> opcoesJwt)
    {
        _dbContext = dbContext;
        _senhaHasher = senhaHasher;
        _opcoesJwt = opcoesJwt.Value;
    }

    public async Task<SessaoPlataforma?> EntrarAsync(string email, string senha, CancellationToken cancellationToken = default)
    {
        var normalizado = (email ?? string.Empty).Trim().ToLowerInvariant();
        var administrador = await _dbContext.AdministradoresPlataforma.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Email == normalizado && a.Ativo, cancellationToken);

        if (administrador is null || !_senhaHasher.Verificar(senha ?? string.Empty, administrador.SenhaHash))
            return null;

        var agora = DateTime.UtcNow;
        var expiraEm = agora.AddMinutes(MinutosSessao);
        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _opcoesJwt.Emissor,
            Audience = _opcoesJwt.AudienciaPlataforma,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expiraEm,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opcoesJwt.ChaveSecreta)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = administrador.Id.ToString(),
                ["email"] = administrador.Email,
                [ClaimsPlataforma.Perfil] = ClaimsPlataforma.PerfilAdministradorPlataforma,
            },
        });

        return new SessaoPlataforma(token, expiraEm, administrador.Nome);
    }

    public async Task<bool> CriarOuRedefinirAdministradorAsync(string email, string nome, string senha, CancellationToken cancellationToken = default)
    {
        var normalizado = email.Trim().ToLowerInvariant();
        var existente = await _dbContext.AdministradoresPlataforma.FirstOrDefaultAsync(a => a.Email == normalizado, cancellationToken);

        if (existente is not null)
        {
            existente.AlterarSenha(_senhaHasher.Hash(senha));
            _dbContext.LogsAuditoriaPlataforma.Add(new LogAuditoriaPlataforma("linha-de-comando", "RedefinirSenhaAdministrador", null, normalizado));
            await _dbContext.SaveChangesAsync(cancellationToken);
            return false;
        }

        _dbContext.AdministradoresPlataforma.Add(AdministradorPlataforma.Criar(normalizado, nome, _senhaHasher.Hash(senha)));
        _dbContext.LogsAuditoriaPlataforma.Add(new LogAuditoriaPlataforma("linha-de-comando", "CriarAdministrador", null, normalizado));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<NegocioNaPlataforma>> ListarNegociosAsync(EstadoAssinatura? estado, CancellationToken cancellationToken = default)
    {
        var linhas = await (
            from n in _dbContext.Negocios.AsNoTracking()
            join a in _dbContext.Assinaturas.IgnoreQueryFilters().AsNoTracking() on n.Id equals a.NegocioId into assinaturas
            from a in assinaturas.DefaultIfEmpty()
            join p in _dbContext.Planos.AsNoTracking() on a.PlanoId equals p.Id into planos
            from p in planos.DefaultIfEmpty()
            where estado == null || (a != null && a.Estado == estado)
            orderby n.CriadoEm descending
            select new { Negocio = n, Assinatura = a, NomePlano = p != null ? p.Nome : null })
            .ToListAsync(cancellationToken);

        return linhas.Select(l => Mapear(l.Negocio, l.Assinatura, l.NomePlano)).ToList();
    }

    public async Task<DetalheNegocioNaPlataforma?> ObterNegocioAsync(Guid negocioId, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstOrDefaultAsync(n => n.Id == negocioId, cancellationToken);
        if (negocio is null)
            return null;

        var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters().AsNoTracking()
            .Include(a => a.Historico)
            .FirstOrDefaultAsync(a => a.NegocioId == negocioId, cancellationToken);
        var nomePlano = assinatura is null
            ? null
            : await _dbContext.Planos.Where(p => p.Id == assinatura.PlanoId).Select(p => p.Nome).FirstOrDefaultAsync(cancellationToken);
        var cobrancas = await _dbContext.CobrancasAssinatura.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.NegocioId == negocioId)
            .OrderByDescending(c => c.PagoEm)
            .Select(c => new CobrancaAssinaturaDto(c.PagoEm, c.Valor, c.Forma, c.PeriodoInicio, c.PeriodoFim, c.Origem))
            .ToListAsync(cancellationToken);
        var profissionaisAtivos = await _dbContext.Profissionais.IgnoreQueryFilters()
            .CountAsync(p => p.NegocioId == negocioId && p.Ativo, cancellationToken);

        return new DetalheNegocioNaPlataforma(
            Mapear(negocio, assinatura, nomePlano), assinatura?.PlanoId, assinatura?.Periodicidade, assinatura?.PrecoMensalTravado,
            assinatura?.ValorDoPeriodo, assinatura?.CarenciaAte, profissionaisAtivos,
            assinatura?.Historico.OrderByDescending(h => h.CriadoEm)
                .Select(h => new HistoricoAssinaturaDto(h.CriadoEm, h.EstadoAnterior, h.EstadoNovo, h.Autor, h.Motivo)).ToList() ?? [],
            cobrancas);
    }

    public Task<bool> RegistrarPagamentoAsync(string autor, Guid negocioId, PagamentoManual dados, CancellationToken cancellationToken = default) =>
        AlterarAssinaturaAsync(autor, negocioId, "RegistrarPagamentoManual", dados, (assinatura, agora) =>
        {
            _dbContext.CobrancasAssinatura.Add(ServicoAssinatura.RegistrarPagamento(
                assinatura, dados.Valor, dados.Forma, dados.PagoEm, dados.PeriodoInicio, dados.PeriodoFim,
                OrigemCobranca.Manual, null, autor, agora));
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<bool> EstenderTesteAsync(string autor, Guid negocioId, int dias, CancellationToken cancellationToken = default) =>
        AlterarAssinaturaAsync(autor, negocioId, "EstenderTeste", new { dias }, (assinatura, agora) =>
        {
            ServicoAssinatura.EstenderTeste(assinatura, dias, autor, agora);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<bool> TrocarPlanoAsync(string autor, Guid negocioId, Guid planoId, Periodicidade periodicidade, CancellationToken cancellationToken = default) =>
        AlterarAssinaturaAsync(autor, negocioId, "TrocarPlano", new { planoId, periodicidade }, async (assinatura, agora) =>
        {
            var plano = await _dbContext.Planos.FirstOrDefaultAsync(p => p.Id == planoId, cancellationToken)
                ?? throw new RegraAssinaturaException("Plano não encontrado.");
            var ativos = await _dbContext.Profissionais.IgnoreQueryFilters().CountAsync(p => p.NegocioId == negocioId && p.Ativo, cancellationToken);
            ServicoAssinatura.TrocarPlano(assinatura, plano, periodicidade, ativos, autor, agora);
        }, cancellationToken);

    public Task<bool> SuspenderAsync(string autor, Guid negocioId, string motivo, CancellationToken cancellationToken = default) =>
        AlterarAssinaturaAsync(autor, negocioId, "Suspender", new { motivo }, (assinatura, agora) =>
        {
            ServicoAssinatura.Suspender(assinatura, string.IsNullOrWhiteSpace(motivo) ? "Suspensa pela administração" : motivo.Trim(), autor, agora);
            return Task.CompletedTask;
        }, cancellationToken);

    public Task<bool> ReativarAsync(string autor, Guid negocioId, CancellationToken cancellationToken = default) =>
        AlterarAssinaturaAsync(autor, negocioId, "Reativar", null, (assinatura, agora) =>
        {
            ServicoAssinatura.Reativar(assinatura, autor, agora);
            return Task.CompletedTask;
        }, cancellationToken);

    /// <summary>Carrega a assinatura (com histórico, que o Reativar consulta), aplica a ação e grava a auditoria junto.</summary>
    private async Task<bool> AlterarAssinaturaAsync(
        string autor, Guid negocioId, string acao, object? detalhes, Func<Assinatura, DateTimeOffset, Task> aplicar,
        CancellationToken cancellationToken)
    {
        var assinatura = await _dbContext.Assinaturas.IgnoreQueryFilters()
            .Include(a => a.Historico)
            .FirstOrDefaultAsync(a => a.NegocioId == negocioId, cancellationToken);

        if (assinatura is null)
            return false;

        await aplicar(assinatura, DateTimeOffset.UtcNow);
        _dbContext.LogsAuditoriaPlataforma.Add(new LogAuditoriaPlataforma(
            autor, acao, negocioId, detalhes is null ? null : JsonSerializer.Serialize(detalhes)));
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static NegocioNaPlataforma Mapear(Plataforma.Dominio.Negocios.Negocio negocio, Assinatura? assinatura, string? nomePlano) => new(
        negocio.Id, negocio.NomeExibido, negocio.Slug.Valor, negocio.Tipo.ToString(), nomePlano, assinatura?.Estado,
        assinatura?.FimTeste, assinatura?.ProximoVencimento, negocio.CriadoEm);
}
