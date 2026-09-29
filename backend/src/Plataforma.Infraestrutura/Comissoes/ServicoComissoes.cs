using System.Globalization;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Profissionais;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Comissoes;

/// <summary>
/// Comissões (seção 7). Só entram linhas de atendimento <c>Concluido</c> com comissão gravada
/// — os concluídos antes deste recurso não têm e não ganham retroativamente. Comissão é dado de
/// remuneração: nada daqui vai para log de aplicação, e-mail ou página pública.
/// </summary>
public sealed class ServicoComissoes : IServicoComissoes
{
    public const int TamanhoMaximoPagina = 100;

    /// <summary>Vendas listadas em "Minhas comissões" (as mais recentes do período); os totais somam todas.</summary>
    public const int MaximoVendasListadas = 100;

    private readonly PlataformaDbContext _dbContext;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly IRegistroAuditoria _auditoria;
    private readonly TravaQuinzenas _travaQuinzenas;

    public ServicoComissoes(
        PlataformaDbContext dbContext, IUsuarioAtual usuarioAtual, IRegistroAuditoria auditoria, TravaQuinzenas travaQuinzenas)
    {
        _dbContext = dbContext;
        _usuarioAtual = usuarioAtual;
        _auditoria = auditoria;
        _travaQuinzenas = travaQuinzenas;
    }

    public Task<ConfiguracaoComissao?> ObterConfiguracaoAsync(Guid profissionalId, CancellationToken cancellationToken = default) =>
        _dbContext.Profissionais
            .Where(p => p.Id == profissionalId && !p.Excluido)
            .Select(p => new ConfiguracaoComissao(p.PercentualComissao, p.AcertoPorQuinzena, p.PercentualComissaoProdutoVenda))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> DefinirConfiguracaoAsync(
        Guid profissionalId, ConfiguracaoComissao configuracao, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.FirstOrDefaultAsync(p => p.Id == profissionalId && !p.Excluido, cancellationToken);
        if (profissional is null)
            return false;

        var percentualAnterior = profissional.PercentualComissao;
        var acertoAnterior = profissional.AcertoPorQuinzena;
        var produtoAnterior = profissional.PercentualComissaoProdutoVenda;
        profissional.DefinirPercentualComissao(configuracao.Percentual);
        profissional.DefinirAcertoPorQuinzena(configuracao.AcertoPorQuinzena);
        if (configuracao.PercentualProdutoVenda is { } percentualProduto)
            profissional.DefinirPercentualComissaoProdutoVenda(percentualProduto);

        var mudancas = new List<string>();
        if (percentualAnterior != configuracao.Percentual)
            mudancas.Add($"Comissão: {Percentual(percentualAnterior)} → {Percentual(configuracao.Percentual)}");
        if (acertoAnterior != configuracao.AcertoPorQuinzena)
            mudancas.Add($"Acerto por quinzena: {SimNao(acertoAnterior)} → {SimNao(configuracao.AcertoPorQuinzena)}");
        if (produtoAnterior != profissional.PercentualComissaoProdutoVenda)
            mudancas.Add($"Comissão de produto: {Percentual(produtoAnterior)} → {Percentual(profissional.PercentualComissaoProdutoVenda)}");

        if (mudancas.Count > 0)
        {
            _auditoria.Registrar(AcoesAuditoria.AlterarComissao, nameof(Profissional), profissional.Id, string.Join("; ", mudancas));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    public async Task<ComissoesDoProfissional> ListarMinhasAsync(FiltroComissoes filtro, CancellationToken cancellationToken = default)
    {
        var usuarioId = _usuarioAtual.UsuarioId ?? throw new InvalidOperationException("Sem usuário logado.");
        var usuario = await _dbContext.Usuarios
            .Where(u => u.Id == usuarioId)
            .Select(u => new { u.ProfissionalId, u.PercentualComissaoProdutoVenda })
            .FirstOrDefaultAsync(cancellationToken);

        var resultado = usuario?.ProfissionalId is { } id ? await ListarDoProfissionalAsync(id, filtro, cancellationToken) : null;
        if (resultado is not null)
            return resultado;

        // Sem cadastro de profissional (ex.: Recepcionista que vende): só a comissão de produto, dela mesma.
        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(filtro.De, filtro.Ate, cancellationToken);
        var produtos = await ComissoesProdutoAsync(
            v => v.VendedorUsuarioId == usuarioId, usuario?.PercentualComissaoProdutoVenda, inicioUtc, fimUtc, cancellationToken);
        return new ComissoesDoProfissional(
            null, null, null, TotaisComissao.Zero, [], filtro.Pagina, filtro.TamanhoPagina, 0, produtos, produtos.Totais.TotalComissao);
    }

    /// <summary>Vendas de um vendedor no período: totais de todas e a lista das mais recentes.</summary>
    private async Task<ComissoesProduto> ComissoesProdutoAsync(
        Expression<Func<VendaProduto, bool>> doVendedor, decimal? percentualAtual, DateTimeOffset inicioUtc, DateTimeOffset fimUtc,
        CancellationToken cancellationToken)
    {
        var vendas = _dbContext.VendasProduto.AsNoTracking().Where(doVendedor).Where(v => v.EstornadaEm == null && v.Data >= inicioUtc && v.Data < fimUtc);

        var totais = await vendas
            .GroupBy(_ => 1)
            .Select(g => new TotaisComissaoProduto(g.Sum(v => v.ValorComissao), g.Sum(v => v.Total), g.Count()))
            .FirstOrDefaultAsync(cancellationToken) ?? TotaisComissaoProduto.Zero;

        var recentes = await vendas
            .Include(v => v.Itens)
            .OrderByDescending(v => v.Data).ThenBy(v => v.Id)
            .Take(MaximoVendasListadas)
            .ToListAsync(cancellationToken);

        var idsClientes = recentes.Where(v => v.ClienteId != null).Select(v => v.ClienteId!.Value).Distinct().ToList();
        var nomes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => idsClientes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        var itens = recentes
            .Select(v => new ItemComissaoProduto(
                v.Id, v.Data,
                string.Join(", ", v.Itens.OrderBy(i => i.NomeProduto).Select(i => i.Quantidade == 1 ? i.NomeProduto : $"{i.NomeProduto} × {i.Quantidade}")),
                v.ClienteId is { } c && nomes.TryGetValue(c, out var nome) ? PrimeiroNome(nome) : null,
                v.Total, v.PercentualComissao, v.ValorComissao))
            .ToList();

        return new ComissoesProduto(percentualAtual, totais, itens);
    }

    public async Task<ComissoesDoProfissional?> ListarDoProfissionalAsync(
        Guid profissionalId, FiltroComissoes filtro, CancellationToken cancellationToken = default)
    {
        var profissional = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => p.Id == profissionalId)
            .Select(p => new { p.Id, p.Nome, p.PercentualComissao, p.PercentualComissaoProdutoVenda })
            .FirstOrDefaultAsync(cancellationToken);
        if (profissional is null)
            return null;

        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(filtro.De, filtro.Ate, cancellationToken);
        var linhas = ConsultaLinhasComissao.Concluidas(_dbContext, inicioUtc, fimUtc).Where(l => l.ProfissionalId == profissionalId);

        var totais = await linhas
            .GroupBy(_ => 1)
            .Select(g => new TotaisComissao(g.Sum(l => l.Comissao), g.Sum(l => l.ValorBase), g.Count()))
            .FirstOrDefaultAsync(cancellationToken) ?? TotaisComissao.Zero;

        var pagina = await linhas
            .OrderByDescending(l => l.Inicio).ThenBy(l => l.LinhaId)
            .Skip((filtro.Pagina - 1) * filtro.TamanhoPagina)
            .Take(filtro.TamanhoPagina)
            .ToListAsync(cancellationToken);

        var idsClientes = pagina.Where(l => l.ClienteId != null).Select(l => l.ClienteId!.Value).Distinct().ToList();
        var nomes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => idsClientes.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Nome, cancellationToken);

        var itens = pagina
            .Select(l => new ItemComissao(
                l.AgendamentoId, l.Inicio, l.Servico,
                PrimeiroNome(l.ClienteId is { } c && nomes.TryGetValue(c, out var nome) ? nome : null),
                l.ValorBase, l.Percentual, l.Comissao))
            .ToList();

        var produtos = await ComissoesProdutoAsync(
            v => v.VendedorProfissionalId == profissionalId, profissional.PercentualComissaoProdutoVenda, inicioUtc, fimUtc, cancellationToken);

        return new ComissoesDoProfissional(
            profissional.Id, profissional.Nome, profissional.PercentualComissao, totais,
            itens, filtro.Pagina, filtro.TamanhoPagina, totais.QuantidadeServicos,
            produtos, totais.TotalComissao + produtos.Totais.TotalComissao);
    }

    public async Task<IReadOnlyList<ResumoComissaoProfissional>> ResumirPorProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(de, ate, cancellationToken);

        var porProfissional = await ConsultaLinhasComissao.Concluidas(_dbContext, inicioUtc, fimUtc)
            .GroupBy(l => l.ProfissionalId)
            .Select(g => new { ProfissionalId = g.Key, Comissao = g.Sum(l => l.Comissao), Base = g.Sum(l => l.ValorBase), Quantidade = g.Count() })
            .ToDictionaryAsync(g => g.ProfissionalId, cancellationToken);

        var vendasPorProfissional = await _dbContext.VendasProduto.AsNoTracking()
            .Where(v => v.VendedorProfissionalId != null && v.EstornadaEm == null && v.Data >= inicioUtc && v.Data < fimUtc)
            .GroupBy(v => v.VendedorProfissionalId!.Value)
            .Select(g => new { ProfissionalId = g.Key, Comissao = g.Sum(v => v.ValorComissao), Total = g.Sum(v => v.Total), Quantidade = g.Count() })
            .ToDictionaryAsync(g => g.ProfissionalId, g => new TotaisComissaoProduto(g.Comissao, g.Total, g.Quantidade), cancellationToken);

        // Todo mundo em atividade aparece (com zero, se for o caso); excluído só se atendeu ou vendeu no período.
        var ids = porProfissional.Keys.Concat(vendasPorProfissional.Keys).Distinct().ToList();
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => !p.Excluido || ids.Contains(p.Id))
            .OrderBy(p => p.Nome)
            .Select(p => new { p.Id, p.Nome, p.Ativo, p.PercentualComissao, p.PercentualComissaoProdutoVenda })
            .ToListAsync(cancellationToken);

        return profissionais
            .Select(p => new ResumoComissaoProfissional(
                p.Id, p.Nome, p.Ativo, p.PercentualComissao,
                porProfissional.TryGetValue(p.Id, out var t) ? new TotaisComissao(t.Comissao, t.Base, t.Quantidade) : TotaisComissao.Zero,
                p.PercentualComissaoProdutoVenda,
                vendasPorProfissional.GetValueOrDefault(p.Id, TotaisComissaoProduto.Zero)))
            .ToList();
    }

    public async Task<IReadOnlyList<ResumoComissaoVendedor>> ResumirVendedoresSemProfissionalAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken = default)
    {
        var (inicioUtc, fimUtc) = await IntervaloUtcAsync(de, ate, cancellationToken);

        var porUsuario = await _dbContext.VendasProduto.AsNoTracking()
            .Where(v => v.VendedorUsuarioId != null && v.EstornadaEm == null && v.Data >= inicioUtc && v.Data < fimUtc)
            .GroupBy(v => v.VendedorUsuarioId!.Value)
            .Select(g => new { UsuarioId = g.Key, Comissao = g.Sum(v => v.ValorComissao), Total = g.Sum(v => v.Total), Quantidade = g.Count() })
            .ToDictionaryAsync(g => g.UsuarioId, g => new TotaisComissaoProduto(g.Comissao, g.Total, g.Quantidade), cancellationToken);

        // Quem vendeu no período, e quem pode vender sem cadastro de profissional (com zero).
        var ids = porUsuario.Keys.ToList();
        var usuarios = await _dbContext.Usuarios.AsNoTracking()
            .Where(u => ids.Contains(u.Id)
                || (!u.Excluido && u.ProfissionalId == null
                    && (u.PercentualComissaoProdutoVenda > 0 || u.Permissoes.Any(p => p.Permissao == Permissao.VenderProdutos))))
            .OrderBy(u => u.Nome)
            .Select(u => new { u.Id, u.Nome, u.Ativo, u.PercentualComissaoProdutoVenda })
            .ToListAsync(cancellationToken);

        return usuarios
            .Select(u => new ResumoComissaoVendedor(
                u.Id, u.Nome, u.Ativo, u.PercentualComissaoProdutoVenda, porUsuario.GetValueOrDefault(u.Id, TotaisComissaoProduto.Zero)))
            .ToList();
    }

    public Task<PercentualComissaoProdutoUsuario?> ObterPercentualProdutoDoUsuarioAsync(
        Guid usuarioId, CancellationToken cancellationToken = default) =>
        _dbContext.Usuarios.AsNoTracking()
            .Where(u => u.Id == usuarioId && !u.Excluido)
            .Select(u => new PercentualComissaoProdutoUsuario(u.PercentualComissaoProdutoVenda, u.ProfissionalId, u.AcertoPorQuinzena))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<bool> DefinirPercentualProdutoDoUsuarioAsync(
        Guid usuarioId, DefinirPercentualProduto dados, CancellationToken cancellationToken = default)
    {
        var usuario = await _dbContext.Usuarios.FirstOrDefaultAsync(u => u.Id == usuarioId && !u.Excluido, cancellationToken);
        if (usuario is null)
            return false;
        if (usuario.ProfissionalId is not null)
            throw new ArgumentException("Este usuário é um profissional: a comissão de produto fica na ficha do profissional.");

        var anterior = usuario.PercentualComissaoProdutoVenda;
        var acertoAnterior = usuario.AcertoPorQuinzena;
        usuario.DefinirPercentualComissaoProdutoVenda(dados.Percentual);
        if (dados.AcertoPorQuinzena is { } acerto)
            usuario.DefinirAcertoPorQuinzena(acerto);

        var mudancas = new List<string>();
        if (anterior != dados.Percentual)
            mudancas.Add($"Comissão de produto: {Percentual(anterior)} → {Percentual(dados.Percentual)}");
        if (acertoAnterior != usuario.AcertoPorQuinzena)
            mudancas.Add($"Acerto por quinzena: {SimNao(acertoAnterior)} → {SimNao(usuario.AcertoPorQuinzena)}");
        if (mudancas.Count > 0)
        {
            _auditoria.Registrar(AcoesAuditoria.AlterarComissaoProduto, nameof(Usuario), usuario.Id, string.Join("; ", mudancas));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private async Task<(DateTimeOffset InicioUtc, DateTimeOffset FimUtc)> IntervaloUtcAsync(
        DateOnly de, DateOnly ate, CancellationToken cancellationToken) =>
        ConsultaLinhasComissao.IntervaloUtc(de, ate, await _travaQuinzenas.FusoAsync(cancellationToken));

    private static string PrimeiroNome(string? nome)
    {
        var primeiro = nome?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrEmpty(primeiro) ? "Cliente" : primeiro;
    }

    private static string SimNao(bool valor) => valor ? "sim" : "não";

    private static string Percentual(decimal valor) =>
        valor.ToString("0.##", CultureInfo.InvariantCulture).Replace('.', ',') + "%";
}
