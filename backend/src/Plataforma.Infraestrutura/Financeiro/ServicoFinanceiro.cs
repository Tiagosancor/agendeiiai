using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Infraestrutura.Agendamentos;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Financeiro;

/// <summary>
/// Faturamento por período (seção 7) — o total geral soma o valor de fato pago
/// (<c>Pagamento.Valor</c>, já refletindo cupom); as quebras por profissional e por
/// serviço somam o preço de cada <c>AgendamentoServico</c> dos agendamentos pagos, uma
/// atribuição direta que não tenta ratear desconto de cupom entre serviços — suficiente
/// para o "painel simples do mês" da seção 7, não uma contabilidade formal.
/// </summary>
public sealed class ServicoFinanceiro : IServicoFinanceiro
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ServicoFinanceiro(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<ResumoFinanceiro> ObterResumoAsync(FiltroFinanceiro filtro, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == negocioId, cancellationToken);
        var fuso = TimeZoneInfo.FindSystemTimeZoneById(negocio.Fuso);

        var inicioUtc = ConversorFusoHorario.ParaUtc(filtro.Inicio, TimeOnly.MinValue, fuso);
        var fimUtc = ConversorFusoHorario.ParaUtc(filtro.Fim.AddDays(1), TimeOnly.MinValue, fuso);

        var consultaAgendamentos = _dbContext.Agendamentos.AsNoTracking()
            .Where(a => a.Inicio >= inicioUtc && a.Inicio < fimUtc);

        if (filtro.ProfissionalId is not null)
            consultaAgendamentos = consultaAgendamentos.Where(a => a.ProfissionalId == filtro.ProfissionalId);

        if (filtro.ServicoId is not null)
            consultaAgendamentos = consultaAgendamentos.Where(a => a.Servicos.Any(s => s.ServicoId == filtro.ServicoId));

        var agendamentos = await consultaAgendamentos.Include(a => a.Servicos).ToListAsync(cancellationToken);
        var agendamentoIds = agendamentos.Select(a => a.Id).ToHashSet();

        var pagamentos = await _dbContext.Pagamentos.AsNoTracking()
            .Where(p => agendamentoIds.Contains(p.AgendamentoId))
            .ToListAsync(cancellationToken);

        var mapaAgendamentos = agendamentos.ToDictionary(a => a.Id);

        var total = pagamentos.Sum(p => p.Valor);
        var quantidade = pagamentos.Count;

        var profissionalIds = pagamentos.Select(p => mapaAgendamentos[p.AgendamentoId].ProfissionalId).Distinct().ToList();
        var profissionais = await _dbContext.Profissionais.AsNoTracking()
            .Where(p => profissionalIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var porProfissional = pagamentos
            .GroupBy(p => mapaAgendamentos[p.AgendamentoId].ProfissionalId)
            .Select(g => new FaturamentoPorProfissional(
                g.Key, profissionais.TryGetValue(g.Key, out var prof) ? prof.Nome : "—", g.Sum(p => p.Valor), g.Count()))
            .OrderByDescending(f => f.Total)
            .ToList();

        var porServico = pagamentos
            // Valor cobrado de cada serviço: já com o ajuste feito durante o atendimento (seção 7).
            .SelectMany(p => mapaAgendamentos[p.AgendamentoId].Servicos.Select(s => new { s.ServicoId, s.Nome, s.ValorCobrado }))
            .GroupBy(x => new { x.ServicoId, x.Nome })
            .Select(g => new FaturamentoPorServico(g.Key.ServicoId, g.Key.Nome, g.Sum(x => x.ValorCobrado), g.Count()))
            .OrderByDescending(f => f.Total)
            .ToList();

        // Vendas de produto entram na data da venda, separadas dos serviços (seção 7).
        var totalProdutos = 0m;
        var quantidadeVendas = 0;
        List<FaturamentoPorProduto> porProduto = [];
        if (filtro.ServicoId is null)
        {
            var vendas = _dbContext.VendasProduto.AsNoTracking().Where(v => v.Data >= inicioUtc && v.Data < fimUtc);
            if (filtro.ProfissionalId is not null)
                vendas = vendas.Where(v => v.VendedorProfissionalId == filtro.ProfissionalId);

            var itens = await vendas.SelectMany(v => v.Itens)
                .Select(i => new { i.VendaId, i.ProdutoId, i.NomeProduto, i.Total, i.Quantidade })
                .ToListAsync(cancellationToken);

            totalProdutos = itens.Sum(i => i.Total);
            quantidadeVendas = itens.Select(i => i.VendaId).Distinct().Count();
            porProduto = itens
                .GroupBy(i => i.ProdutoId)
                .Select(g => new FaturamentoPorProduto(g.Key, g.First().NomeProduto, g.Sum(i => i.Total), g.Sum(i => i.Quantidade)))
                .OrderByDescending(f => f.Total)
                .ToList();
        }

        return new ResumoFinanceiro(
            total + totalProdutos, quantidade, porProfissional, porServico, total, totalProdutos, quantidadeVendas, porProduto);
    }
}
