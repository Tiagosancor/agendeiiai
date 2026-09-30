using FluentAssertions;
using Plataforma.Aplicacao.Arquivos;
using Plataforma.Infraestrutura.Arquivos;
using SkiaSharp;
using Xunit;

namespace Plataforma.Testes.Unidade.Infraestrutura;

/// <summary>Validação e reprocessamento de imagem enviada (seção 8.4: só imagens, tamanho limitado, reprocessadas).</summary>
public sealed class ProcessadorImagemSkiaTestes
{
    private readonly ProcessadorImagemSkia _processador = new();

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg)]
    [InlineData(SKEncodedImageFormat.Png)]
    [InlineData(SKEncodedImageFormat.Webp)]
    public void Imagem_aceita_sai_sempre_em_webp(SKEncodedImageFormat formato)
    {
        var resultado = _processador.Processar(Imagem(300, 200, formato), 1920);

        resultado.TipoConteudo.Should().Be("image/webp");
        using var codec = SKCodec.Create(new MemoryStream(resultado.Conteudo));
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Webp);
        (codec.Info.Width, codec.Info.Height).Should().Be((300, 200));
    }

    [Fact]
    public void Imagem_grande_e_reduzida_mantendo_a_proporcao()
    {
        var resultado = _processador.Processar(Imagem(1200, 4000, SKEncodedImageFormat.Png), 1920);

        (resultado.Largura, resultado.Altura).Should().Be((576, 1920));
    }

    [Theory]
    [InlineData(4000, 3000, 1920, 1920, 1440)]
    [InlineData(1920, 1080, 1920, 1920, 1080)]
    [InlineData(100, 50, 1920, 100, 50)]
    [InlineData(10000, 1, 1920, 1920, 1)]
    public void Reduzir_so_diminui_e_nunca_zera(int largura, int altura, int maximo, int esperadaLargura, int esperadaAltura) =>
        ProcessadorImagemSkia.Reduzir(largura, altura, maximo).Should().Be((esperadaLargura, esperadaAltura));

    [Fact]
    public void Arquivo_que_nao_e_imagem_e_recusado() =>
        FluentActions.Invoking(() => _processador.Processar("<svg onload=alert(1)></svg>"u8.ToArray(), 1920))
            .Should().Throw<ImagemInvalidaException>();

    [Fact]
    public void Gif_e_recusado()
    {
        var gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

        FluentActions.Invoking(() => _processador.Processar(gif, 1920))
            .Should().Throw<ImagemInvalidaException>().WithMessage("*JPG, PNG ou WebP*");
    }

    [Fact]
    public void Arquivo_vazio_ou_acima_do_limite_e_recusado()
    {
        FluentActions.Invoking(() => _processador.Processar([], 1920)).Should().Throw<ImagemInvalidaException>();
        FluentActions.Invoking(() => _processador.Processar(new byte[LimitesImagem.BytesMaximos + 1], 1920))
            .Should().Throw<ImagemInvalidaException>().WithMessage("*5 MB*");
    }

    [Fact]
    public void Imagem_truncada_e_recusada()
    {
        var png = Imagem(200, 200, SKEncodedImageFormat.Png);

        FluentActions.Invoking(() => _processador.Processar(png[..40], 1920)).Should().Throw<ImagemInvalidaException>();
        FluentActions.Invoking(() => _processador.Processar(png[..(png.Length / 2)], 1920)).Should().Throw<ImagemInvalidaException>();
        var jpeg = Imagem(400, 400, SKEncodedImageFormat.Jpeg);
        FluentActions.Invoking(() => _processador.Processar(jpeg[..(jpeg.Length / 2)], 1920)).Should().Throw<ImagemInvalidaException>();
    }

    private static byte[] Imagem(int largura, int altura, SKEncodedImageFormat formato)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(30, 42, 56));
            using var pintura = new SKPaint { Color = new SKColor(217, 142, 59) };
            canvas.DrawCircle(largura / 2f, altura / 2f, Math.Min(largura, altura) / 3f, pintura);
        }
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(formato, 90);
        return dados.ToArray();
    }
}
