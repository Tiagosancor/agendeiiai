namespace Plataforma.Dominio.Comissoes;

/// <summary>
/// Atendimento de uma quinzena já fechada para o profissional (seção 7): não pode ser concluído,
/// reaberto, cancelado nem ter o valor corrigido até o Administrador reabrir a quinzena.
/// </summary>
public sealed class QuinzenaFechadaException : InvalidOperationException
{
    public QuinzenaFechadaException()
        : base("Este atendimento é de uma quinzena de comissões já fechada. Para mexer nele, o Administrador precisa reabrir a quinzena.")
    {
    }

    /// <summary>A mesma trava para outra coisa que entrou no fechamento (ex.: venda de produto, pela comissão do vendedor).</summary>
    public QuinzenaFechadaException(string mensagem)
        : base(mensagem)
    {
    }
}
