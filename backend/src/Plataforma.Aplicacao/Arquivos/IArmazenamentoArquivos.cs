namespace Plataforma.Aplicacao.Arquivos;

/// <summary>
/// Onde ficam os arquivos enviados (seção 8.5: nada no disco do servidor). Hoje a implementação grava no próprio Postgres;
/// trocar por R2/S3 é outra implementação desta interface, sem mexer em quem a usa. Arquivo guardado é público por quem
/// tiver a chave (aleatória) — só entra aqui o que a página pública já mostra.
/// </summary>
public interface IArmazenamentoArquivos
{
    /// <summary>Grava na hora e devolve a chave nova (nunca reaproveitada: trocar uma imagem gera outra chave).</summary>
    Task<string> SalvarAsync(Guid negocioId, string tipoConteudo, byte[] conteudo, CancellationToken cancellationToken = default);

    Task<ConteudoArquivo?> ObterAsync(string chave, CancellationToken cancellationToken = default);

    /// <summary>Sem erro se a chave não existe mais.</summary>
    Task RemoverAsync(string chave, CancellationToken cancellationToken = default);

    /// <summary>Endereço para o navegador buscar o arquivo. Relativo à API quando começa com "/".</summary>
    string UrlPublica(string chave);
}

public sealed record ConteudoArquivo(string TipoConteudo, byte[] Conteudo);
