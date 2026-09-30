using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Comum;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Negocios;

/// <summary>
/// Troca do link público (seção 5). Tudo numa transação: trava o slug disputado (<see cref="DisponibilidadeSlugs.TravarAsync"/>)
/// e a linha do negócio (duas trocas simultâneas não furam o limite de 30 dias), confere a disponibilidade, grava o
/// <see cref="SlugAnterior"/> (301 por 90 dias) e a auditoria. Os links enviados daqui em diante (e-mails, primeiros passos)
/// já saem com o novo, porque são montados na hora a partir do slug atual.
/// </summary>
public sealed class GerenciadorLinkNegocio : IGerenciadorLinkNegocio
{
    public const string AcaoAuditoria = "TrocarLink";

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IRegistroAuditoria _auditoria;
    private readonly OpcoesMarca _opcoesMarca;

    public GerenciadorLinkNegocio(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IRegistroAuditoria auditoria, IOptions<OpcoesMarca> opcoesMarca)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _auditoria = auditoria;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task<LinkNegocio?> ObterAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstOrDefaultAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        return negocio is null ? null : Mapear(negocio, DateTimeOffset.UtcNow);
    }

    public async Task<DisponibilidadeSlug> VerificarAsync(string slug, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);

        if (DisponibilidadeSlugs.ValidarFormato(slug, out var valido) is null && valido!.Equals(negocio.Slug))
            return new DisponibilidadeSlug(false, "Este já é o link atual.");

        return await DisponibilidadeSlugs.VerificarAsync(_dbContext, slug, negocio.Id, DateTimeOffset.UtcNow, cancellationToken);
    }

    public async Task<ResultadoTrocaLink> TrocarAsync(string slug, CancellationToken cancellationToken = default)
    {
        var problema = DisponibilidadeSlugs.ValidarFormato(slug, out var novo);
        if (problema is not null)
            return ResultadoTrocaLink.Falha(ErroTrocaLink.Invalido, problema);

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();

        return await estrategia.ExecuteAsync(async () =>
        {
            _dbContext.ChangeTracker.Clear();
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            await DisponibilidadeSlugs.TravarAsync(_dbContext, novo!, cancellationToken);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM negocios WHERE id = {negocioId} FOR UPDATE", cancellationToken);
            var negocio = await _dbContext.Negocios.FirstAsync(n => n.Id == negocioId, cancellationToken);
            var agora = DateTimeOffset.UtcNow;

            if (novo!.Equals(negocio.Slug))
                return ResultadoTrocaLink.Falha(ErroTrocaLink.Invalido, "Este já é o link atual.");

            if (negocio.ProximaTrocaDeSlugEm is { } proxima && agora < proxima)
                return TrocaRecente(proxima, negocio.Fuso);

            if (await DisponibilidadeSlugs.EmUsoAsync(_dbContext, novo, negocioId, agora, cancellationToken))
                return ResultadoTrocaLink.Falha(ErroTrocaLink.EmUso, DisponibilidadeSlugs.MensagemEmUso);

            var antigo = negocio.Slug;
            var anterior = negocio.TrocarSlug(novo, agora);
            _dbContext.SlugsAnteriores.Add(anterior);

            // Voltar para um link antigo do próprio negócio: ele deixa de ser "antigo" (senão redirecionaria para si mesmo).
            var reaproveitados = await _dbContext.SlugsAnteriores.Where(s => s.NegocioId == negocioId && s.Slug == novo).ToListAsync(cancellationToken);
            _dbContext.SlugsAnteriores.RemoveRange(reaproveitados);

            _auditoria.Registrar(AcaoAuditoria, nameof(Negocio), negocioId,
                $"Link: de '{antigo.Valor}' para '{novo.Valor}'; o antigo redireciona até {FormatacaoBrasil.Data(anterior.RedirecionaAte, negocio.Fuso)}");

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
            {
                // Um cadastro novo ficou com o slug entre a checagem e a gravação: o índice único decide.
                return ResultadoTrocaLink.Falha(ErroTrocaLink.EmUso, DisponibilidadeSlugs.MensagemEmUso);
            }

            await transacao.CommitAsync(cancellationToken);
            return ResultadoTrocaLink.Ok(Mapear(negocio, agora));
        });
    }

    private static ResultadoTrocaLink TrocaRecente(DateTimeOffset proxima, string fuso) =>
        ResultadoTrocaLink.Falha(ErroTrocaLink.TrocaRecente,
            $"O link só pode ser trocado uma vez a cada {Negocio.DiasEntreTrocasDeSlug} dias. Você poderá trocar de novo em {FormatacaoBrasil.Data(proxima, fuso)}.",
            proxima);

    private LinkNegocio Mapear(Negocio negocio, DateTimeOffset agora) => new(
        negocio.Slug.Valor,
        ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, "/"),
        negocio.SlugAlteradoEm,
        negocio.ProximaTrocaDeSlugEm is { } proxima && proxima > agora ? proxima : null);
}
