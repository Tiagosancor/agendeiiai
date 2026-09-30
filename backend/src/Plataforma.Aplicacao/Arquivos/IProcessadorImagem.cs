namespace Plataforma.Aplicacao.Arquivos;

/// <summary>
/// Validação e reprocessamento de imagem enviada (seção 8.4: "só imagens, tamanho limitado, reprocessadas"). O arquivo
/// original nunca é guardado: a imagem é decodificada, reduzida e gravada de novo em WebP, o que também descarta metadados
/// (EXIF, localização) e qualquer conteúdo escondido.
/// </summary>
public interface IProcessadorImagem
{
    /// <summary>Lança <see cref="ImagemInvalidaException"/> com a mensagem para o usuário.</summary>
    ImagemProcessada Processar(byte[] original, int ladoMaximo);
}

public sealed record ImagemProcessada(string TipoConteudo, byte[] Conteudo, int Largura, int Altura);

public sealed class ImagemInvalidaException(string mensagem) : Exception(mensagem);

/// <summary>Limites comuns a todo upload de imagem.</summary>
public static class LimitesImagem
{
    /// <summary>Tamanho máximo do arquivo enviado.</summary>
    public const int BytesMaximos = 5 * 1024 * 1024;

    /// <summary>Lado maior da imagem de fundo depois de reprocessada.</summary>
    public const int LadoMaximoFundo = 1920;
}
