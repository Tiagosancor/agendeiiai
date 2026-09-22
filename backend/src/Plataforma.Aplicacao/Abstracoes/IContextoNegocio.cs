namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>
/// Dá acesso ao negócio (tenant) da requisição atual. No painel o valor vem sempre
/// do token JWT; nos endpoints públicos, do slug resolvido pelo host — nunca de
/// parâmetro, corpo ou cabeçalho (seção 8.3.2). É a partir daqui que o
/// <c>PlataformaDbContext</c> monta o *global query filter* por <c>NegocioId</c>.
/// </summary>
public interface IContextoNegocio
{
    /// <summary>Id do negócio atual, ou <c>null</c> quando ainda não foi resolvido (ex.: rotas sem tenant).</summary>
    Guid? NegocioId { get; }

    /// <summary>True quando a requisição já tem um negócio resolvido.</summary>
    bool TemNegocio { get; }

    /// <summary>Define o negócio atual. Só deve ser chamado pela infraestrutura de resolução de tenant.</summary>
    void Definir(Guid negocioId);
}
