namespace Plataforma.Dominio.Negocios;

/// <summary>
/// De onde veio a contratação do agente de IA do WhatsApp. Os três valores existem desde já: o enum é convertido de texto ao ler
/// o <see cref="Negocio"/>, e um valor desconhecido (gravado por uma versão mais nova durante um rollback de código) quebraria a
/// leitura de todo negócio. Nesta entrega só <see cref="Manual"/> é gravado.
/// </summary>
public enum OrigemAgenteIa
{
    /// <summary>Ligado pelo dono do produto em <c>/plataforma</c>.</summary>
    Manual = 1,

    /// <summary>Incluído no plano do negócio (fase de cobrança).</summary>
    Plano = 2,

    /// <summary>Adicional cobrado à parte (fase de cobrança).</summary>
    Adicional = 3,
}
