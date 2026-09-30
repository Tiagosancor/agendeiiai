using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Persistencia;
using Plataforma.Testes.Integracao.Infraestrutura;
using SkiaSharp;
using Xunit;

namespace Plataforma.Testes.Integracao.Negocios;

/// <summary>Cor e imagem de fundo da página pública e do assistente (item 15 da seção 14, seção 5).</summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class FundoPaginaNegocioTestes : IAsyncLifetime
{
    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public FundoPaginaNegocioTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Cor_de_fundo_salva_no_painel_aparece_na_pagina_publica()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var perfil = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");

        (await cliente.PutAsJsonAsync("/painel/negocio", Atualizacao(perfil!, "#1A2B3C"))).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.CorFundo.Should().Be("#1a2b3c");
        var publico = await NegocioPublicoAsync(perfil!.Slug);
        publico.GetProperty("corFundo").GetString().Should().Be("#1a2b3c");
        publico.GetProperty("imagemFundoUrl").ValueKind.Should().Be(JsonValueKind.Null);

        // Vazio tira a cor.
        (await cliente.PutAsJsonAsync("/painel/negocio", Atualizacao(perfil!, ""))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await NegocioPublicoAsync(perfil.Slug)).GetProperty("corFundo").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("azul")]
    [InlineData("#12345")]
    [InlineData("url(javascript:alert(1))")]
    public async Task Cor_de_fundo_fora_do_formato_e_recusada_com_400(string cor)
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var perfil = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");

        var resposta = await cliente.PutAsJsonAsync("/painel/negocio", Atualizacao(perfil!, cor));

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.CorFundo.Should().BeNull();
    }

    [Fact]
    public async Task Imagem_enviada_e_reprocessada_em_webp_reduzida_e_servida_na_url_publica()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var perfil = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");

        var resposta = await EnviarAsync(cliente, Imagem(3000, 1500, SKEncodedImageFormat.Jpeg), "fundo.jpg", "image/jpeg");

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        var url = (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imagemFundoUrl").GetString();
        url.Should().MatchRegex("^/arquivos/[0-9a-f]{32}$");
        (await NegocioPublicoAsync(perfil!.Slug)).GetProperty("imagemFundoUrl").GetString().Should().Be(url);
        (await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.ImagemFundoUrl.Should().Be(url);

        using var anonimo = _fabrica.CreateClient();
        var arquivo = await anonimo.GetAsync(url);
        arquivo.StatusCode.Should().Be(HttpStatusCode.OK);
        arquivo.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        arquivo.Headers.CacheControl!.MaxAge.Should().BeGreaterThan(TimeSpan.FromDays(30));
        using var codec = SKCodec.Create(new MemoryStream(await arquivo.Content.ReadAsByteArrayAsync()));
        codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Webp);
        (codec.Info.Width, codec.Info.Height).Should().Be((1920, 960));
    }

    [Fact]
    public async Task Trocar_a_imagem_apaga_a_anterior_e_remover_volta_para_so_a_cor()
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var perfil = await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio");
        await cliente.PutAsJsonAsync("/painel/negocio", Atualizacao(perfil!, "#223344"));
        using var anonimo = _fabrica.CreateClient();

        var primeira = await UrlAsync(await EnviarAsync(cliente, Imagem(400, 300, SKEncodedImageFormat.Png), "a.png", "image/png"));
        var segunda = await UrlAsync(await EnviarAsync(cliente, Imagem(400, 300, SKEncodedImageFormat.Webp), "b.webp", "image/webp"));

        segunda.Should().NotBe(primeira);
        (await anonimo.GetAsync(primeira)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonimo.GetAsync(segunda)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await cliente.DeleteAsync("/painel/negocio/imagem-fundo")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var publico = await NegocioPublicoAsync(perfil!.Slug);
        publico.GetProperty("imagemFundoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        publico.GetProperty("corFundo").GetString().Should().Be("#223344");
        (await anonimo.GetAsync(segunda)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Arquivos.Count(a => a.NegocioId == negocioId).Should().Be(0);
    }

    [Theory]
    [InlineData("texto.jpg", "image/jpeg")]
    [InlineData("pagina.html", "text/html")]
    [InlineData("desenho.gif", "image/gif")]
    public async Task Arquivo_que_nao_e_imagem_aceita_e_recusado_sem_gravar_nada(string nome, string tipo)
    {
        var (cliente, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var conteudo = nome.EndsWith(".gif")
            ? Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")
            : "<html><script>alert(1)</script></html>"u8.ToArray();

        var resposta = await EnviarAsync(cliente, conteudo, nome, tipo);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.ImagemFundoUrl.Should().BeNull();
        using var escopo = _fabrica.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<PlataformaDbContext>().Arquivos.Count(a => a.NegocioId == negocioId).Should().Be(0);
    }

    [Fact]
    public async Task Imagem_acima_de_5_MB_e_recusada()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var grande = new byte[5 * 1024 * 1024 + 1];
        Imagem(10, 10, SKEncodedImageFormat.Png).CopyTo(grande, 0);

        var resposta = await EnviarAsync(cliente, grande, "grande.png", "image/png");

        resposta.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        (await cliente.GetFromJsonAsync<PerfilNegocio>("/painel/negocio"))!.ImagemFundoUrl.Should().BeNull();
    }

    [Fact]
    public async Task Sem_permissao_de_configurar_o_negocio_nao_envia_imagem()
    {
        var (cliente, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Profissional);

        var resposta = await EnviarAsync(cliente, Imagem(100, 100, SKEncodedImageFormat.Png), "a.png", "image/png");

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await cliente.DeleteAsync("/painel/negocio/imagem-fundo")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("nao-existe")]
    [InlineData("00000000000000000000000000000000")]
    public async Task Chave_inexistente_ou_invalida_da_404(string chave)
    {
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync($"/arquivos/{chave}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private async Task<JsonElement> NegocioPublicoAsync(string slug)
    {
        using var anonimo = _fabrica.CreateClient();
        return await anonimo.GetFromJsonAsync<JsonElement>($"/publico/negocios-por-slug/{slug}");
    }

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, byte[] conteudo, string nome, string tipo)
    {
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        var formulario = new MultipartFormDataContent { { arquivo, "arquivo", nome } };
        return cliente.PutAsync("/painel/negocio/imagem-fundo", formulario);
    }

    private static async Task<string> UrlAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("imagemFundoUrl").GetString()!;
    }

    internal static byte[] Imagem(int largura, int altura, SKEncodedImageFormat formato)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(new SKColor(30, 42, 56));
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(formato, 90);
        return dados.ToArray();
    }

    private static AtualizarPerfilNegocio Atualizacao(PerfilNegocio p, string? corFundo) => new(
        p.NomeExibido, p.LogoUrl, p.CorPrimaria, p.CorSecundaria, p.TituloPagina, p.SubtituloPagina, p.TextoSobre,
        p.Bairro, p.Cidade, p.Rua, p.Numero, p.Cep, p.Telefone, p.EmailContato, p.Instagram, p.Facebook, p.WhatsApp,
        p.WhatsAppAtivoParaConfirmacoes, p.HorarioFuncionamento, p.WhatsAppAvisoProfissional, corFundo);
}
