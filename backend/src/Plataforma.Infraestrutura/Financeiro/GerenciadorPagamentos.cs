using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Financeiro;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Financeiro;

public sealed class GerenciadorPagamentos : IGerenciadorPagamentos
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorPagamentos(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<Guid> RegistrarAsync(RegistrarPagamento dados, CancellationToken cancellationToken = default)
    {
        var agendamentoExiste = await _dbContext.Agendamentos.AnyAsync(a => a.Id == dados.AgendamentoId, cancellationToken);
        if (!agendamentoExiste)
            throw new InvalidOperationException("Agendamento não encontrado.");

        var jaTemPagamento = await _dbContext.Pagamentos.AnyAsync(p => p.AgendamentoId == dados.AgendamentoId, cancellationToken);
        if (jaTemPagamento)
            throw new InvalidOperationException("Esse agendamento já tem um pagamento registrado.");

        var pagamento = Pagamento.Criar(
            _contextoNegocio.NegocioId!.Value, dados.AgendamentoId, dados.Valor, Enum.Parse<FormaPagamento>(dados.Forma));

        _dbContext.Pagamentos.Add(pagamento);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return pagamento.Id;
    }

    public async Task<PagamentoResumo?> ObterPorAgendamentoAsync(Guid agendamentoId, CancellationToken cancellationToken = default)
    {
        var pagamento = await _dbContext.Pagamentos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.AgendamentoId == agendamentoId, cancellationToken);

        return pagamento is null
            ? null
            : new PagamentoResumo(pagamento.Id, pagamento.AgendamentoId, pagamento.Valor, pagamento.Forma.ToString(), pagamento.CriadoEm);
    }
}
