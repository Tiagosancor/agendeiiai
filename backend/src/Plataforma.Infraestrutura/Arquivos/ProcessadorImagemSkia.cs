using Plataforma.Aplicacao.Arquivos;
using SkiaSharp;

namespace Plataforma.Infraestrutura.Arquivos;

/// <summary>
/// <see cref="IProcessadorImagem"/> com SkiaSharp (MIT; o nativo do Alpine vem de <c>SkiaSharp.NativeAssets.Linux.NoDependencies</c>).
/// Só JPEG, PNG e WebP; o tamanho em pixels é conferido pelo cabeçalho <b>antes</b> de decodificar (bomba de descompressão).
/// Saída sempre WebP, reduzida para caber em <c>ladoMaximo</c>, na orientação certa e sem metadados.
/// </summary>
public sealed class ProcessadorImagemSkia : IProcessadorImagem
{
    private const int LadoMaximoEntrada = 10_000;
    private const long PixelsMaximosEntrada = 40_000_000;
    private const int QualidadeWebp = 80;

    private static readonly SKEncodedImageFormat[] FormatosAceitos =
        [SKEncodedImageFormat.Jpeg, SKEncodedImageFormat.Png, SKEncodedImageFormat.Webp];

    public ImagemProcessada Processar(byte[] original, int ladoMaximo)
    {
        if (original.Length == 0)
            throw new ImagemInvalidaException("Envie uma imagem.");
        if (original.Length > LimitesImagem.BytesMaximos)
            throw new ImagemInvalidaException($"A imagem pode ter até {LimitesImagem.BytesMaximos / (1024 * 1024)} MB.");

        using var dados = SKData.CreateCopy(original);
        using var codec = SKCodec.Create(dados)
            ?? throw new ImagemInvalidaException("O arquivo não é uma imagem válida. Envie JPG, PNG ou WebP.");

        if (!FormatosAceitos.Contains(codec.EncodedFormat))
            throw new ImagemInvalidaException("Formato não aceito. Envie JPG, PNG ou WebP.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0 || info.Width > LadoMaximoEntrada || info.Height > LadoMaximoEntrada
            || (long)info.Width * info.Height > PixelsMaximosEntrada)
            throw new ImagemInvalidaException("A imagem é grande demais. Use uma de até 10.000 pixels de lado.");

        // GetPixels em vez de SKBitmap.Decode: arquivo cortado no meio devolve IncompleteInput, e o Decode aceitaria a metade.
        var infoDecodificada = info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Premul);
        using var decodificada = new SKBitmap(infoDecodificada);
        if (codec.GetPixels(infoDecodificada, decodificada.GetPixels()) != SKCodecResult.Success)
            throw new ImagemInvalidaException("Não foi possível ler a imagem. Tente outro arquivo.");

        using var orientada = Orientar(decodificada, codec.EncodedOrigin);
        var (largura, altura) = Reduzir(orientada.Width, orientada.Height, ladoMaximo);

        using var superficie = SKSurface.Create(new SKImageInfo(largura, altura, SKColorType.Rgba8888, SKAlphaType.Premul))
            ?? throw new ImagemInvalidaException("Não foi possível processar a imagem.");
        using (var imagemOrigem = SKImage.FromBitmap(orientada))
        using (var pintura = new SKPaint())
        {
            superficie.Canvas.Clear(SKColors.Transparent);
            superficie.Canvas.DrawImage(imagemOrigem, new SKRect(0, 0, largura, altura),
                new SKSamplingOptions(SKCubicResampler.Mitchell), pintura);
        }

        using var imagem = superficie.Snapshot();
        using var webp = imagem.Encode(SKEncodedImageFormat.Webp, QualidadeWebp)
            ?? throw new ImagemInvalidaException("Não foi possível processar a imagem.");

        return new ImagemProcessada("image/webp", webp.ToArray(), largura, altura);
    }

    public static (int Largura, int Altura) Reduzir(int largura, int altura, int ladoMaximo)
    {
        var maior = Math.Max(largura, altura);
        if (maior <= ladoMaximo)
            return (largura, altura);

        var escala = (double)ladoMaximo / maior;
        return (Math.Max(1, (int)Math.Round(largura * escala)), Math.Max(1, (int)Math.Round(altura * escala)));
    }

    /// <summary>Foto de celular vem "deitada", com a rotação só no EXIF — que some ao regravar; por isso a rotação é aplicada aqui.</summary>
    private static SKBitmap Orientar(SKBitmap origem, SKEncodedOrigin orientacao)
    {
        var graus = orientacao switch
        {
            SKEncodedOrigin.BottomRight => 180,
            SKEncodedOrigin.RightTop => 90,
            SKEncodedOrigin.LeftBottom => 270,
            _ => 0,
        };

        if (graus == 0)
            return origem.Copy();

        var trocaLados = graus != 180;
        var destino = new SKBitmap(trocaLados ? origem.Height : origem.Width, trocaLados ? origem.Width : origem.Height,
            origem.ColorType, origem.AlphaType);
        using var canvas = new SKCanvas(destino);
        canvas.Translate(destino.Width / 2f, destino.Height / 2f);
        canvas.RotateDegrees(graus);
        canvas.Translate(-origem.Width / 2f, -origem.Height / 2f);
        using (var imagemOrigem = SKImage.FromBitmap(origem))
            canvas.DrawImage(imagemOrigem, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return destino;
    }
}
