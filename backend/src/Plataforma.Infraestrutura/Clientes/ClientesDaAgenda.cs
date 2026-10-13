using System.Net.Mail;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Clientes;

public sealed class ClientesDaAgenda : IClientesDaAgenda
{
    private const int TamanhoMinimoTermo = 2;
    private const int DigitosMinimosTelefone = 3;
    private const int TamanhoMaximoNome = 150;
    private const int TamanhoMaximoEmail = 254;

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ClientesDaAgenda(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<IReadOnlyList<ClienteBusca>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken = default)
    {
        termo = termo?.Trim() ?? string.Empty;
        if (termo.Length < TamanhoMinimoTermo || _contextoNegocio.NegocioId is not { } negocioId)
            return [];

        limite = Math.Clamp(limite, 1, IClientesDaAgenda.LimiteMaximoBusca);

        var escapado = EscaparLike(termo);
        var padraoContem = $"%{escapado}%";
        var padraoComeca = $"{escapado}%";

        // Telefone só entra se o termo tiver dígitos suficientes: "ab" ou "1" não varrem a coluna.
        var digitos = new string(termo.Where(char.IsAsciiDigit).ToArray());
        var buscaTelefone = digitos.Length >= DigitosMinimosTelefone;
        var padraoTelefoneContem = buscaTelefone ? $"%{digitos}%" : "%";
        var padraoTelefoneTermina = buscaTelefone ? $"%{digitos}" : "%";

        // SQL direto: o EF não traduz unaccent/relevância, e a coluna do telefone tem conversor. O filtro do negócio é explícito
        // aqui porque SqlQuery não passa pelo filtro global; os testes de isolamento cobrem esse ponto.
        var linhas = await _dbContext.Database.SqlQuery<LinhaBusca>($@"
            SELECT id AS ""Id"", nome AS ""Nome"", telefone AS ""Telefone"", email AS ""Email""
            FROM clientes
            WHERE negocio_id = {negocioId}
              AND NOT excluido
              AND (sem_acento(lower(nome)) LIKE sem_acento(lower({padraoContem})) ESCAPE '\'
                   OR ({buscaTelefone} AND telefone LIKE {padraoTelefoneContem}))
            ORDER BY CASE
                         WHEN sem_acento(lower(nome)) LIKE sem_acento(lower({padraoComeca})) ESCAPE '\' THEN 0
                         WHEN {buscaTelefone} AND telefone LIKE {padraoTelefoneTermina} THEN 1
                         WHEN sem_acento(lower(nome)) LIKE sem_acento(lower({padraoContem})) ESCAPE '\' THEN 2
                         ELSE 3
                     END,
                     nome, id
            LIMIT {limite}").ToListAsync(cancellationToken);

        return linhas.Select(l => new ClienteBusca(l.Id, l.Nome, l.Telefone, l.Email)).ToList();
    }

    public async Task<ResultadoCadastroRapido> CadastrarRapidoAsync(CadastroRapidoCliente dados, CancellationToken cancellationToken = default)
    {
        var nome = dados.Nome?.Trim();
        if (string.IsNullOrEmpty(nome))
            return ResultadoCadastroRapido.ComErro("Informe o nome do cliente.");
        if (nome.Length > TamanhoMaximoNome)
            return ResultadoCadastroRapido.ComErro($"O nome pode ter no máximo {TamanhoMaximoNome} caracteres.");

        if (string.IsNullOrWhiteSpace(dados.Telefone))
            return ResultadoCadastroRapido.ComErro("Informe o telefone do cliente.");
        if (!TelefoneE164.TentarCriar(dados.Telefone, out var telefone) || telefone is null)
            return ResultadoCadastroRapido.ComErro("Telefone inválido. Informe o DDD e o número.");

        var email = string.IsNullOrWhiteSpace(dados.Email) ? null : dados.Email.Trim();
        if (email is not null && (email.Length > TamanhoMaximoEmail || !EmailValido(email)))
            return ResultadoCadastroRapido.ComErro("E-mail inválido.");

        var existente = await BuscarPorTelefoneAsync(telefone, cancellationToken);
        if (existente is not null)
            return ResultadoCadastroRapido.ComExistente(existente);

        var cliente = Cliente.Criar(_contextoNegocio.NegocioId!.Value, nome, telefone, email: email);
        _dbContext.Clientes.Add(cliente);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException excecao) when (excecao.EhViolacaoDeUnicidade())
        {
            // Duas recepcionistas cadastrando o mesmo telefone ao mesmo tempo: a segunda recebe o cliente da primeira.
            _dbContext.Entry(cliente).State = EntityState.Detached;
            var criadoAgora = await BuscarPorTelefoneAsync(telefone, cancellationToken);
            if (criadoAgora is not null)
                return ResultadoCadastroRapido.ComExistente(criadoAgora);
            throw;
        }

        return ResultadoCadastroRapido.ComCriado(new ClienteBusca(cliente.Id, cliente.Nome, cliente.Telefone?.Valor, cliente.Email));
    }

    private async Task<ClienteBusca?> BuscarPorTelefoneAsync(TelefoneE164 telefone, CancellationToken cancellationToken) =>
        await _dbContext.Clientes.AsNoTracking()
            .Where(c => c.Telefone == telefone)
            .Select(c => new ClienteBusca(c.Id, c.Nome, c.Telefone!.Valor, c.Email))
            .FirstOrDefaultAsync(cancellationToken);

    private static bool EmailValido(string email) =>
        MailAddress.TryCreate(email, out var endereco) && endereco.Address == email && email.Contains('@') && email.Split('@')[1].Contains('.');

    private static string EscaparLike(string texto) =>
        texto.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private sealed record LinhaBusca(Guid Id, string Nome, string? Telefone, string? Email);
}
