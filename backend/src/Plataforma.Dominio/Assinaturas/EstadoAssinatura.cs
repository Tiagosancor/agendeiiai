namespace Plataforma.Dominio.Assinaturas;

/// <summary>Ciclo de vida da assinatura do negócio (seção 7). Só muda via <see cref="ServicoAssinatura"/>.</summary>
public enum EstadoAssinatura
{
    EmTeste,
    Ativa,

    /// <summary>Carência depois do fim do teste ou do vencimento: tudo funciona, com aviso destacado.</summary>
    Atrasada,

    /// <summary>Painel só mostra assinatura e exportação; página pública não aceita novos agendamentos.</summary>
    Suspensa,

    Cancelada,
}
