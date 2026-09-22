using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Verificacao;

/// <summary>
/// Código de confirmação do cliente final (seção 8.1). Nunca revela se o telefone já é
/// cliente cadastrado — a resposta de <see cref="SolicitarCodigoAsync"/> não depende disso
/// nem consulta a tabela de clientes (anti-enumeração, seção 8.1.3), porque a identificação
/// do cliente só acontece depois, na confirmação do agendamento (seção 8.1.4).
/// </summary>
public interface IServicoVerificacao
{
    Task<ResultadoSolicitarCodigo> SolicitarCodigoAsync(
        TelefoneE164 telefone, string? email, CancellationToken cancellationToken = default);

    /// <summary>Confere o código e, se válido, emite o <c>tokenVerificacao</c> (seção 8.1.1.c), vinculado a este negócio e telefone, válido por 15 minutos.</summary>
    Task<ResultadoValidarCodigo> ValidarCodigoAsync(
        TelefoneE164 telefone, string codigo, CancellationToken cancellationToken = default);
}

/// <summary>
/// Sempre <c>true</c> a menos que o rate limit tenha estourado — nunca reflete se o
/// telefone existe ou não (seção 8.1.3). O corpo devolvido ao cliente HTTP é sempre o
/// mesmo independente do motivo interno, exceto para "limite excedido", cujo texto é
/// genérico o bastante para não vazar comportamento por telefone.
/// </summary>
public sealed record ResultadoSolicitarCodigo(bool Sucesso, bool LimiteExcedido = false);

public sealed record ResultadoValidarCodigo(bool Sucesso, string? TokenVerificacao = null, string? MensagemErro = null);
