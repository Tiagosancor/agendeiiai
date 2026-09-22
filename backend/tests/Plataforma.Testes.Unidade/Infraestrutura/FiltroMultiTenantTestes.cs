using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Infraestrutura.MultiTenant;
using Plataforma.Infraestrutura.Persistencia;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>
/// Prova o *global query filter* multi-tenant do <c>PlataformaDbContext</c> (seção 8.3.1):
/// um negócio nunca enxerga dados de outro, e sem negócio resolvido nenhuma linha aparece
/// (comportamento seguro por padrão — ver docs/decisoes.md).
///
/// Importante: as três instâncias de <see cref="DbContextDeTeste"/> abaixo compartilham as
/// MESMAS <c>DbContextOptions</c> (e portanto o mesmo modelo do EF Core, cacheado). Isso é
/// proposital — é exatamente o cenário em que o bug corrigido em <c>PlataformaDbContext</c>
/// (capturar o tenant como <c>Expression.Constant</c> em vez de via método de instância)
/// travaria todo mundo no tenant da primeira requisição.
/// </summary>
public sealed class FiltroMultiTenantTestes
{
    [Fact]
    public async Task Cada_negocio_ve_apenas_os_proprios_itens()
    {
        var negocioA = Guid.NewGuid();
        var negocioB = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nameof(Cada_negocio_ve_apenas_os_proprios_itens))
            .Options;

        await using (var dbContextParaSemear = CriarContexto(options, negocioA))
        {
            dbContextParaSemear.Itens.Add(ItemDeTeste.Criar(negocioA, "Item do negócio A"));
            dbContextParaSemear.Itens.Add(ItemDeTeste.Criar(negocioB, "Item do negócio B"));
            await dbContextParaSemear.SaveChangesAsync();
        }

        await using var dbContextA = CriarContexto(options, negocioA);
        (await dbContextA.Itens.ToListAsync()).Should().ContainSingle(i => i.Nome == "Item do negócio A");

        await using var dbContextB = CriarContexto(options, negocioB);
        (await dbContextB.Itens.ToListAsync()).Should().ContainSingle(i => i.Nome == "Item do negócio B");
    }

    [Fact]
    public async Task Sem_negocio_resolvido_nenhuma_linha_e_visivel()
    {
        var negocioA = Guid.NewGuid();

        var options = new DbContextOptionsBuilder<PlataformaDbContext>()
            .UseInMemoryDatabase(nameof(Sem_negocio_resolvido_nenhuma_linha_e_visivel))
            .Options;

        await using (var dbContextParaSemear = CriarContexto(options, negocioA))
        {
            dbContextParaSemear.Itens.Add(ItemDeTeste.Criar(negocioA, "Item do negócio A"));
            await dbContextParaSemear.SaveChangesAsync();
        }

        var contextoSemNegocio = new ContextoNegocio();
        await using var dbContextSemTenant = new DbContextDeTeste(options, contextoSemNegocio);

        (await dbContextSemTenant.Itens.ToListAsync()).Should().BeEmpty();
    }

    private static DbContextDeTeste CriarContexto(
        DbContextOptions<PlataformaDbContext> options, Guid negocioId)
    {
        var contexto = new ContextoNegocio();
        contexto.Definir(negocioId);
        return new DbContextDeTeste(options, contexto);
    }
}
