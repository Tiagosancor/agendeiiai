using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Clientes;

public sealed class GerenciadorClientes : IGerenciadorClientes
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public GerenciadorClientes(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<ClienteResumo>> ListarAsync(CancellationToken cancellationToken = default) =>
        await _dbContext.Clientes
            .OrderBy(c => c.Nome)
            .Select(c => new ClienteResumo(c.Id, c.Nome, c.Telefone.Valor, c.Email, c.Observacoes, c.Excluido))
            .ToListAsync(cancellationToken);

    public async Task<ClienteResumo?> ObterAsync(Guid clienteId, CancellationToken cancellationToken = default)
    {
        var cliente = await _dbContext.Clientes.FindAsync([clienteId], cancellationToken);
        return cliente is null ? null : Mapear(cliente);
    }

    public async Task<Guid> CriarAsync(CriarCliente dados, CancellationToken cancellationToken = default)
    {
        var telefone = TelefoneE164.Criar(dados.Telefone);

        var jaExiste = await _dbContext.Clientes.AnyAsync(c => c.Telefone == telefone, cancellationToken);
        if (jaExiste)
            throw new TelefoneJaCadastradoException(telefone.Valor);

        var cliente = Cliente.Criar(
            _contextoNegocio.NegocioId!.Value, dados.Nome, telefone,
            email: dados.Email, observacoes: dados.Observacoes);

        _dbContext.Clientes.Add(cliente);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            throw new TelefoneJaCadastradoException(telefone.Valor);
        }

        return cliente.Id;
    }

    public async Task<bool> AtualizarAsync(Guid clienteId, AtualizarCliente dados, CancellationToken cancellationToken = default)
    {
        var cliente = await _dbContext.Clientes.FindAsync([clienteId], cancellationToken);
        if (cliente is null)
            return false;

        cliente.AtualizarDados(dados.Nome, dados.Email, dados.Observacoes);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<ExportacaoCliente?> ExportarAsync(Guid clienteId, CancellationToken cancellationToken = default)
    {
        var cliente = await _dbContext.Clientes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == clienteId, cancellationToken);
        if (cliente is null)
            return null;

        var agendamentos = await _dbContext.Agendamentos.AsNoTracking()
            .Include(a => a.Servicos)
            .Where(a => a.ClienteId == clienteId)
            .OrderByDescending(a => a.Inicio)
            .ToListAsync(cancellationToken);

        return new ExportacaoCliente(
            cliente.Id, cliente.Nome, cliente.Telefone.Valor, cliente.Email, cliente.Observacoes,
            cliente.Origem.ToString(), cliente.CriadoEm,
            agendamentos.Select(a => new ExportacaoAgendamento(
                a.Inicio, a.Fim, a.Status.ToString(), a.Servicos.Select(s => s.Nome).ToList(), a.Total, a.Observacoes)).ToList());
    }

    public async Task<bool> ExcluirAsync(Guid clienteId, CancellationToken cancellationToken = default)
    {
        var cliente = await _dbContext.Clientes.FindAsync([clienteId], cancellationToken);
        if (cliente is null)
            return false;

        cliente.Anonimizar(DateTimeOffset.UtcNow);

        // Zera também o que o cliente informou de próprio punho no link público (seção 8.1.4)
        // nos agendamentos já feitos — o nome do cadastro já foi anonimizado acima.
        await _dbContext.Agendamentos
            .Where(a => a.ClienteId == clienteId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.NomeInformado, (string?)null), cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static ClienteResumo Mapear(Cliente cliente) =>
        new(cliente.Id, cliente.Nome, cliente.Telefone.Valor, cliente.Email, cliente.Observacoes, cliente.Excluido);
}
