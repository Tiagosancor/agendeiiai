using Plataforma.Aplicacao.Cadastros;
using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Profissionais;

public interface IGerenciadorProfissionais
{
    Task<IReadOnlyList<ProfissionalResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<ProfissionalDetalhe?> ObterAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<Guid> CriarAsync(CriarProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarDadosAsync(Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<string?> RevelarCpfAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Lista os agendamentos futuros que precisam ser transferidos ou cancelados antes (seção 7).</summary>
    Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Bloqueada enquanto houver agendamento futuro — transferir ou cancelar cada um antes.</summary>
    Task<ResultadoExclusao> ExcluirAsync(Guid profissionalId, CancellationToken cancellationToken = default);
}

public sealed record ProfissionalResumo(Guid Id, string Nome, bool Ativo, string? FotoUrl, string? Funcao);

public sealed record ProfissionalDetalhe(
    Guid Id, string Nome, string? Telefone, string? Email, bool Ativo, string? FotoUrl, string? CpfMascarado, string? Funcao,
    Endereco? Endereco = null);

public sealed record CriarProfissional(string Nome, string? Telefone = null, string? Email = null, string? Cpf = null, string? Funcao = null);

/// <summary>
/// Edição da ficha (seção 7). <c>Cpf</c>/<c>Endereco</c> nulos ficam como estão; <c>FotoUrl</c>
/// nula fica como está e vazia apaga a foto.
/// </summary>
public sealed record AtualizarProfissional(
    string Nome, string? Telefone, string? Email, string? Funcao = null, string? Cpf = null, Endereco? Endereco = null,
    string? FotoUrl = null);
