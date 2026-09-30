using Plataforma.Dominio.Comum;

namespace Plataforma.Infraestrutura.Arquivos;

/// <summary>
/// Arquivo guardado no próprio Postgres (<see cref="ArmazenamentoArquivosBanco"/>). Não é <c>IEntidadeDoNegocio</c> de
/// propósito: é servido por <c>/arquivos/{chave}</c> sem negócio resolvido (a página de um negócio e o painel buscam pela
/// mesma URL). O <see cref="NegocioId"/> fica só para saber de quem é.
/// </summary>
public sealed class ArquivoBanco : EntidadeBase
{
    public Guid NegocioId { get; private set; }

    /// <summary>Aleatória e sem relação com o Id (32 caracteres hexadecimais); é o que aparece na URL.</summary>
    public string Chave { get; private set; } = string.Empty;

    public string TipoConteudo { get; private set; } = string.Empty;

    public byte[] Conteudo { get; private set; } = [];

    public int Tamanho { get; private set; }

    private ArquivoBanco()
    {
    }

    public ArquivoBanco(Guid negocioId, string chave, string tipoConteudo, byte[] conteudo)
    {
        NegocioId = negocioId;
        Chave = chave;
        TipoConteudo = tipoConteudo;
        Conteudo = conteudo;
        Tamanho = conteudo.Length;
    }
}
