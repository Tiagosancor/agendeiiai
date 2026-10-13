namespace Plataforma.Aplicacao.Clientes;

/// <summary>
/// Escolher ou cadastrar o cliente na hora de agendar (agenda do painel): busca por nome ou telefone e cadastro rápido
/// só com o essencial. Tudo restrito ao negócio da requisição.
/// </summary>
public interface IClientesDaAgenda
{
    /// <summary>Termo com menos de 2 caracteres devolve vazio. <paramref name="limite"/> vale de 1 a <see cref="LimiteMaximoBusca"/>.</summary>
    Task<IReadOnlyList<ClienteBusca>> BuscarAsync(string? termo, int limite, CancellationToken cancellationToken = default);

    /// <summary>Telefone já cadastrado no negócio não cria nada: devolve o cliente existente.</summary>
    Task<ResultadoCadastroRapido> CadastrarRapidoAsync(CadastroRapidoCliente dados, CancellationToken cancellationToken = default);

    public const int LimitePadraoBusca = 10;

    public const int LimiteMaximoBusca = 20;
}

public sealed record ClienteBusca(Guid Id, string Nome, string? Telefone, string? Email);

public sealed record CadastroRapidoCliente(string? Nome, string? Telefone, string? Email = null);

/// <summary><c>Erro</c> preenchido = dados inválidos (400); <c>Existente</c> preenchido = telefone repetido (409); senão <c>Criado</c>.</summary>
public sealed record ResultadoCadastroRapido(ClienteBusca? Criado, ClienteBusca? Existente, string? Erro)
{
    public static ResultadoCadastroRapido ComCriado(ClienteBusca cliente) => new(cliente, null, null);

    public static ResultadoCadastroRapido ComExistente(ClienteBusca cliente) => new(null, cliente, null);

    public static ResultadoCadastroRapido ComErro(string erro) => new(null, null, erro);
}
