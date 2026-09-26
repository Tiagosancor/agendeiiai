namespace Plataforma.Aplicacao.Auditoria;

/// <summary>
/// Log de auditoria do negócio (seção 7: toda edição e exclusão de cadastro). Só enfileira a
/// entrada no mesmo <c>DbContext</c> — ela é gravada no <c>SaveChanges</c> de quem chamou,
/// junto com a alteração, nunca separada dela.
/// </summary>
public interface IRegistroAuditoria
{
    void Registrar(string acao, string entidade, Guid entidadeId, string? detalhes = null);
}

public static class AcoesAuditoria
{
    public const string Editar = "Editar";
    public const string ApagarDefinitivo = "ApagarDefinitivo";
    public const string ExcluirLogicamente = "ExcluirLogicamente";
    public const string TransferirAgendamento = "TransferirAgendamento";
    public const string CancelarAgendamento = "CancelarAgendamento";
}
