using Plataforma.Aplicacao.Abstracoes;

namespace Plataforma.Infraestrutura.MultiTenant;

/// <summary>
/// Implementação padrão de <see cref="IContextoNegocio"/>, registrada com escopo por
/// requisição (Scoped). O <c>NegocioId</c> só pode ser definido uma vez por requisição;
/// tentar sobrescrevê-lo indica um bug de composição do pipeline.
/// </summary>
public sealed class ContextoNegocio : IContextoNegocio
{
    private Guid? _negocioId;

    public Guid? NegocioId => _negocioId;

    public bool TemNegocio => _negocioId.HasValue;

    public void Definir(Guid negocioId)
    {
        if (_negocioId.HasValue && _negocioId.Value != negocioId)
        {
            throw new InvalidOperationException(
                "O negócio da requisição já foi definido e não pode ser trocado.");
        }

        _negocioId = negocioId;
    }
}
