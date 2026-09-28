namespace Plataforma.Aplicacao.Agendamentos;

/// <summary>
/// Ajuste de valor durante o atendimento (seção 7). A permissão "ajustar valor do atendimento" é
/// conferida pela policy do endpoint; aqui ficam as regras de alcance: o Profissional ajusta só os
/// atendimentos dele, e depois de concluído só o Administrador corrige (nunca em quinzena fechada).
/// </summary>
public interface IServicoAjustesAtendimento
{
    Task<ResultadoAjusteValor> AjustarAsync(Guid agendamentoId, Guid linhaId, AjustarValor dados, CancellationToken cancellationToken = default);

    /// <summary>Valores do atendimento e histórico de ajustes. Nulo: não encontrado ou sem acesso.</summary>
    Task<ValoresAtendimento?> ObterValoresAsync(Guid agendamentoId, CancellationToken cancellationToken = default);
}

/// <summary><c>Tipo</c>: "Desconto" | "Acrescimo"; <c>Modo</c>: "Reais" | "Percentual".</summary>
public sealed record AjustarValor(string Tipo, string Modo, decimal Valor, string? Motivo);

public enum ErroAjusteValor
{
    NaoEncontrado,
    SemAcesso,
    StatusInvalido,
    DadosInvalidos,
}

public sealed record ResultadoAjusteValor(bool Sucesso, decimal? ValorFinal = null, ErroAjusteValor? Erro = null, string? Mensagem = null)
{
    public static ResultadoAjusteValor Ok(decimal valorFinal) => new(true, valorFinal);

    public static ResultadoAjusteValor Falha(ErroAjusteValor erro, string mensagem) => new(false, Erro: erro, Mensagem: mensagem);
}

public sealed record AjusteRegistrado(
    DateTimeOffset Em, string Tipo, string Modo, decimal ValorInformado, decimal ValorAntes, decimal ValorDepois,
    string Motivo, string? Por, bool AposConclusao);

public sealed record LinhaValores(Guid LinhaId, string Servico, decimal PrecoOriginal, decimal ValorCobrado, IReadOnlyList<AjusteRegistrado> Ajustes);

/// <summary>
/// <c>PodeAjustar</c>: o usuário pode ajustar agora (atendimento aberto) ou corrigir (concluído,
/// Administrador) — a tela só mostra o botão quando é verdadeiro.
/// </summary>
public sealed record ValoresAtendimento(
    Guid AgendamentoId, string Status, decimal DescontoCupom, decimal Total, bool PodeAjustar, IReadOnlyList<LinhaValores> Linhas);
