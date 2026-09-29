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
    public const string ReabrirAtendimento = "ReabrirAtendimento";
    public const string AlterarComissao = "AlterarComissao";
    public const string CriarQuinzena = "CriarQuinzena";
    public const string EditarQuinzena = "EditarQuinzena";
    public const string ExcluirQuinzena = "ExcluirQuinzena";
    public const string FecharQuinzena = "FecharQuinzena";
    public const string ReabrirQuinzena = "ReabrirQuinzena";
    public const string AjustarValorAtendimento = "AjustarValorAtendimento";
    public const string CorrigirValorAtendimento = "CorrigirValorAtendimento";
    public const string ForcarAgendamento = "ForcarAgendamento";
    public const string DarAcessoProfissional = "DarAcessoProfissional";
    public const string CriarProduto = "CriarProduto";
    public const string AtivarProduto = "AtivarProduto";
    public const string DesativarProduto = "DesativarProduto";
    public const string EntradaEstoque = "EntradaEstoque";
    public const string AjusteEstoque = "AjusteEstoque";
    public const string VendaProduto = "VendaProduto";
    public const string AlterarComissaoProduto = "AlterarComissaoProduto";
}
