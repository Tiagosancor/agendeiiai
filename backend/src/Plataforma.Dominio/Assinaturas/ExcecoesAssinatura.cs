namespace Plataforma.Dominio.Assinaturas;

/// <summary>Transição de assinatura recusada por regra de negócio (ex.: estender teste de quem já pagou).</summary>
public class RegraAssinaturaException(string mensagem) : InvalidOperationException(mensagem);

/// <summary>Mais profissionais ativos do que o plano comporta (seção 7).</summary>
public sealed class LimiteProfissionaisExcedidoException(int maximo, int ativos)
    : RegraAssinaturaException($"Este plano permite até {maximo} profissionais ativos. Desative {ativos - maximo} para continuar.")
{
    public int Maximo { get; } = maximo;

    public int Ativos { get; } = ativos;

    public int Excedente => Ativos - Maximo;
}
