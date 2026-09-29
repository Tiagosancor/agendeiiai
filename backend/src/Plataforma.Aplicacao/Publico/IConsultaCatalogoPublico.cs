namespace Plataforma.Aplicacao.Publico;

/// <summary>
/// Catálogo público do negócio (seção 6.1.3/6.1.4) — só o que já é público por natureza
/// (equipe, serviços e preços do site); nada de dado pessoal de cliente passa por aqui
/// (seção 8.1.3).
/// </summary>
public interface IConsultaCatalogoPublico
{
    /// <summary>
    /// Serviços ativos que algum profissional ativo executa (seção 6.2: o catálogo de "Qualquer profissional" é a união do
    /// que a equipe faz). <c>ExibirNaPaginaInicial</c> decide só a vitrine da página; o assistente mostra todos.
    /// </summary>
    Task<IReadOnlyList<CategoriaComServicosPublicos>> ListarServicosAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProfissionalPublico>> ListarProfissionaisAsync(CancellationToken cancellationToken = default);
}

public sealed record CategoriaComServicosPublicos(Guid CategoriaId, string Nome, IReadOnlyList<ServicoPublico> Servicos);

public sealed record ServicoPublico(Guid Id, string Nome, decimal Preco, int DuracaoMinutos, bool Popular, bool ExibirNaPaginaInicial = true);

/// <summary>
/// <c>Servicos</c>: o que ele executa, com o preço e a duração dele (os personalizados do vínculo, quando há — os mesmos que o
/// agendamento grava). <c>ServicoIds</c> repete só os IDs, por compatibilidade.
/// </summary>
public sealed record ProfissionalPublico(
    Guid Id, string Nome, string? FotoUrl, string? Funcao, IReadOnlyList<Guid> ServicoIds,
    IReadOnlyList<ServicoDoProfissionalPublico>? Servicos = null);

public sealed record ServicoDoProfissionalPublico(Guid ServicoId, decimal Preco, int DuracaoMinutos);
