using Microsoft.EntityFrameworkCore;

namespace Plataforma.Infraestrutura.Persistencia;

/// <summary>
/// Conta as execuções de um callback repetível da <c>ExecutionStrategy</c> (<c>EnableRetryOnFailure</c>, seção 8.5.6).
/// A primeira roda intacta; a partir da segunda, <see cref="Iniciar"/> faz <c>ChangeTracker.Clear()</c>. A transação
/// anterior foi desfeita no banco, mas o contexto ainda guardava o que ela incluiu (<c>Added</c>), alterou (<c>Modified</c>)
/// e o que o <c>SaveChanges</c> dela marcou como gravado — a nova tentativa gravaria esses restos de novo (venda duplicada)
/// ou deixaria de gravar o que "já estava lá" (comissão perdida).
/// <para>
/// Regra para quem usa: chamar <see cref="Iniciar"/> na primeira linha do callback, e carregar, criar e registrar (inclusive
/// auditoria) tudo <b>dentro</b> dele — o que foi rastreado antes do callback some na limpeza. Não é global de propósito: cada
/// fluxo foi revisado para isso.
/// </para>
/// </summary>
internal sealed class TentativasDaTransacao(DbContext dbContext)
{
    private int _executadas;

    public void Iniciar()
    {
        if (_executadas++ > 0)
            dbContext.ChangeTracker.Clear();
    }
}
