using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Estoque;

/// <summary>
/// Alerta diário de reposição (seção 7): para cada negócio com produto ativo em estoque baixo ou esgotado,
/// um único e-mail resumo aos Administradores. Sem nada nas duas listas, não manda nada.
/// </summary>
public sealed class JobAlertaEstoque
{
    private readonly PlataformaDbContext _dbContext;
    private readonly INotificador _notificador;
    private readonly OpcoesMarca _opcoesMarca;

    public JobAlertaEstoque(PlataformaDbContext dbContext, INotificador notificador, IOptions<OpcoesMarca> opcoesMarca)
    {
        _dbContext = dbContext;
        _notificador = notificador;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        // Job sem tenant: varre todos os negócios explicitamente.
        var emAlerta = await _dbContext.Produtos.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Ativo && p.QuantidadeEstoque <= p.QuantidadeMinima)
            .OrderBy(p => p.Nome)
            .Select(p => new { p.NegocioId, p.Nome, p.QuantidadeEstoque, p.QuantidadeMinima })
            .ToListAsync(cancellationToken);
        if (emAlerta.Count == 0)
            return;

        var negocioIds = emAlerta.Select(p => p.NegocioId).Distinct().ToList();
        var negocios = await _dbContext.Negocios.AsNoTracking()
            .Where(n => negocioIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.NomeExibido, cancellationToken);
        var administradores = await _dbContext.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => negocioIds.Contains(u.NegocioId) && u.Perfil == Perfil.Administrador && u.Ativo && !u.Excluido)
            .Select(u => new { u.NegocioId, u.Email })
            .ToListAsync(cancellationToken);
        var link = ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, "/painel/estoque");

        foreach (var negocioId in negocioIds)
        {
            var emails = administradores.Where(a => a.NegocioId == negocioId).Select(a => a.Email).ToList();
            if (emails.Count == 0 || !negocios.TryGetValue(negocioId, out var nome))
                continue;

            var produtos = emAlerta.Where(p => p.NegocioId == negocioId).ToList();
            await _notificador.EnviarAlertaEstoqueAsync(new DadosAlertaEstoque(
                emails, nome,
                produtos.Where(p => p.QuantidadeEstoque > 0).Select(p => (p.Nome, p.QuantidadeEstoque, p.QuantidadeMinima)).ToList(),
                produtos.Where(p => p.QuantidadeEstoque == 0).Select(p => p.Nome).ToList(),
                link), cancellationToken);
        }
    }
}
