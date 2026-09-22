namespace Plataforma.Aplicacao.Fidelidade;

/// <summary>Cartão de selos (seção 7): configuração por negócio, registro automático de selo ao concluir atendimento, e resgate manual da recompensa.</summary>
public interface IGerenciadorFidelidade
{
    Task<ProgramaFidelidadeResumo?> ObterProgramaAsync(CancellationToken cancellationToken = default);

    Task DefinirProgramaAsync(DefinirProgramaFidelidade dados, CancellationToken cancellationToken = default);

    /// <summary>Chamado ao concluir um atendimento (seção 7) — não faz nada se não houver programa ativo para o negócio.</summary>
    Task RegistrarSeloAsync(Guid clienteId, Guid agendamentoId, CancellationToken cancellationToken = default);

    Task<ProgressoFidelidade> ObterProgressoAsync(Guid clienteId, CancellationToken cancellationToken = default);

    /// <summary>Consome os selos mais antigos não resgatados (quantidade = <c>SelosNecessarios</c>). Retorna <c>false</c> se ainda não há selos suficientes.</summary>
    Task<bool> ResgatarRecompensaAsync(Guid clienteId, CancellationToken cancellationToken = default);
}

public sealed record ProgramaFidelidadeResumo(int SelosNecessarios, string DescricaoRecompensa, bool Ativo);

public sealed record DefinirProgramaFidelidade(int SelosNecessarios, string DescricaoRecompensa);

public sealed record ProgressoFidelidade(int SelosAtuais, int SelosNecessarios, bool PodeResgatar, string? DescricaoRecompensa);
