using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Publico;
using Plataforma.Dominio.Usuarios;
using Plataforma.Testes.Integracao.Infraestrutura;
using SkiaSharp;
using Xunit;

namespace Plataforma.Testes.Integracao.Profissionais;

/// <summary>
/// Upload da foto do profissional (A1-BE): mesmo armazenamento e processamento da imagem de fundo (WebP, sem metadados, até
/// 5 MB), reduzida ao tamanho de avatar. A foto enviada prevalece sobre a <c>FotoUrl</c> externa antiga, que segue valendo
/// como alternativa. Quem gerencia profissionais troca a de qualquer um do negócio; o profissional, só a dele (<c>/painel/minha-foto</c>).
/// </summary>
[Collection(ColecaoComPostgres.NomeColecao)]
public sealed class FotoProfissionalTestes : IAsyncLifetime
{
    private const string UrlExterna = "https://exemplo.com/foto-antiga.jpg";
    private const string MarcaNoExif = "SEGREDO-GPS-B5";

    private readonly PostgresContainerFixture _postgres;
    private PlataformaWebApplicationFactory _fabrica = null!;

    public FotoProfissionalTestes(PostgresContainerFixture postgres) => _postgres = postgres;

    public async Task InitializeAsync()
    {
        _fabrica = new PlataformaWebApplicationFactory(_postgres.ConnectionString);
        using var aquecimento = _fabrica.CreateClient();
        await _fabrica.ResetarBancoAsync();
    }

    public async Task DisposeAsync() => await _fabrica.DisposeAsync();

    [Fact]
    public async Task Upload_valido_vira_webp_reduzido_sem_metadados_e_aparece_em_todo_contrato()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Ana Foto");
        var original = ComExif(Imagem(2000, 1000, SKEncodedImageFormat.Jpeg));
        Encoding.ASCII.GetString(original).Should().Contain(MarcaNoExif, "o arquivo de teste leva o bloco EXIF");

        var url = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", original, "foto.jpg", "image/jpeg"));

        url.Should().MatchRegex("^/arquivos/[0-9a-f]{32}$");
        using var anonimo = _fabrica.CreateClient();
        var arquivo = await anonimo.GetAsync(url);
        arquivo.StatusCode.Should().Be(HttpStatusCode.OK);
        arquivo.Content.Headers.ContentType!.MediaType.Should().Be("image/webp");
        var bytes = await arquivo.Content.ReadAsByteArrayAsync();
        Encoding.ASCII.GetString(bytes).Should().NotContain(MarcaNoExif, "o original nunca é guardado; os metadados somem");
        using (var codec = SKCodec.Create(new MemoryStream(bytes)))
        {
            codec.EncodedFormat.Should().Be(SKEncodedImageFormat.Webp);
            (codec.Info.Width, codec.Info.Height).Should().Be((512, 256), "lado maior reduzido ao tamanho de avatar");
        }

        // A chave fica guardada, mas nenhum contrato a expõe sozinha: todos devolvem a URL efetiva.
        (await DetalheAsync(admin, id)).FotoUrl.Should().Be(url);
        (await admin.GetFromJsonAsync<List<ProfissionalResumo>>("/painel/profissionais"))!.Single(p => p.Id == id).FotoUrl.Should().Be(url);
        (await ColunaDaGradeAsync(admin, id)).GetProperty("fotoUrl").GetString().Should().Be(url);
        using var publico = await ClientePublicoAsync(negocioId);
        (await publico.GetFromJsonAsync<List<ProfissionalPublico>>("/publico/profissionais"))!.Single(p => p.Id == id).FotoUrl.Should().Be(url);
        (await admin.GetStringAsync($"/painel/profissionais/{id}")).Should().NotContain("fotoArquivo");
    }

    [Fact]
    public async Task Trocar_a_foto_apaga_o_arquivo_anterior_depois_de_gravar_a_nova()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Troca Foto");
        using var anonimo = _fabrica.CreateClient();

        var primeira = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", Imagem(300, 300, SKEncodedImageFormat.Png), "a.png", "image/png"));
        var segunda = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", Imagem(300, 300, SKEncodedImageFormat.Webp), "b.webp", "image/webp"));

        segunda.Should().NotBe(primeira);
        (await anonimo.GetAsync(primeira)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonimo.GetAsync(segunda)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await DetalheAsync(admin, id)).FotoUrl.Should().Be(segunda);
    }

    [Fact]
    public async Task Remover_apaga_o_arquivo_enviado_e_a_url_antiga_e_fica_sem_foto()
    {
        var (admin, _, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Remove Foto");
        await DefinirUrlExternaAsync(admin, id);
        var url = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", Imagem(300, 300, SKEncodedImageFormat.Png), "a.png", "image/png"));

        (await admin.DeleteAsync($"/painel/profissionais/{id}/foto")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DetalheAsync(admin, id)).FotoUrl.Should().BeNull("sem foto enviada e sem a URL externa");
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Url_externa_antiga_vale_ate_existir_foto_enviada_e_a_enviada_prevalece()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Url Antiga");
        await DefinirUrlExternaAsync(admin, id);

        (await DetalheAsync(admin, id)).FotoUrl.Should().Be(UrlExterna, "registro antigo continua funcionando");
        (await ColunaDaGradeAsync(admin, id)).GetProperty("fotoUrl").GetString().Should().Be(UrlExterna);
        using var publico = await ClientePublicoAsync(negocioId);
        (await publico.GetFromJsonAsync<List<ProfissionalPublico>>("/publico/profissionais"))!.Single(p => p.Id == id).FotoUrl.Should().Be(UrlExterna);

        var enviada = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", Imagem(300, 300, SKEncodedImageFormat.Png), "a.png", "image/png"));

        (await DetalheAsync(admin, id)).FotoUrl.Should().Be(enviada);
        (await ColunaDaGradeAsync(admin, id)).GetProperty("fotoUrl").GetString().Should().Be(enviada);
        (await publico.GetFromJsonAsync<List<ProfissionalPublico>>("/publico/profissionais"))!.Single(p => p.Id == id).FotoUrl.Should().Be(enviada);
    }

    [Fact]
    public async Task Remover_quando_so_ha_url_externa_nao_mexe_em_nenhum_arquivo_guardado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var outro = await CriarProfissionalAsync(admin, "Com Arquivo");
        var arquivoDoOutro = await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{outro}/foto", Imagem(200, 200, SKEncodedImageFormat.Png), "a.png", "image/png"));
        var id = await CriarProfissionalAsync(admin, "So Externa");
        await DefinirUrlExternaAsync(admin, id);
        var arquivosAntes = await ArquivosAsync(negocioId);

        (await admin.DeleteAsync($"/painel/profissionais/{id}/foto")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await DetalheAsync(admin, id)).FotoUrl.Should().BeNull();
        (await ArquivosAsync(negocioId)).Should().Be(arquivosAntes, "a URL externa só deixa de ser usada; nada guardado é apagado");
        using var anonimo = _fabrica.CreateClient();
        (await anonimo.GetAsync(arquivoDoOutro)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("texto.jpg", "image/jpeg")]
    [InlineData("pagina.html", "text/html")]
    [InlineData("desenho.gif", "image/gif")]
    public async Task Conteudo_que_nao_e_imagem_aceita_e_400_sem_gravar_nada(string nome, string tipo)
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Invalido");
        var conteudo = nome.EndsWith(".gif")
            ? Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7")
            : "<html><script>alert(1)</script></html>"u8.ToArray();

        var resposta = await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", conteudo, nome, tipo);

        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await resposta.Content.ReadAsStringAsync()).Should().NotContainAny("Exception", "Skia", "stack");
        (await DetalheAsync(admin, id)).FotoUrl.Should().BeNull();
        (await ArquivosAsync(negocioId)).Should().Be(0);
    }

    [Fact]
    public async Task Arquivo_acima_de_5_MB_e_recusado()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Grande");
        var grande = new byte[5 * 1024 * 1024 + 1];
        Imagem(10, 10, SKEncodedImageFormat.Png).CopyTo(grande, 0);

        var resposta = await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", grande, "grande.png", "image/png");

        resposta.StatusCode.Should().BeOneOf(HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge);
        (await DetalheAsync(admin, id)).FotoUrl.Should().BeNull();
        (await ArquivosAsync(negocioId)).Should().Be(0);
    }

    [Fact]
    public async Task Profissional_troca_e_tira_so_a_propria_foto()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var meu = await CriarProfissionalAsync(admin, "Eu Mesmo");
        var colega = await CriarProfissionalAsync(admin, "Colega");
        var eu = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Profissional, meu);

        var url = await UrlAsync(await EnviarAsync(eu, "/painel/minha-foto", Imagem(300, 300, SKEncodedImageFormat.Png), "eu.png", "image/png"));
        (await DetalheAsync(admin, meu)).FotoUrl.Should().Be(url);
        (await DetalheAsync(admin, colega)).FotoUrl.Should().BeNull();

        // A ficha e a foto dos outros ficam com quem gerencia profissionais.
        (await EnviarAsync(eu, $"/painel/profissionais/{colega}/foto", Imagem(300, 300, SKEncodedImageFormat.Png), "x.png", "image/png"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await eu.DeleteAsync($"/painel/profissionais/{colega}/foto")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DetalheAsync(admin, colega)).FotoUrl.Should().BeNull();

        (await eu.DeleteAsync("/painel/minha-foto")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await DetalheAsync(admin, meu)).FotoUrl.Should().BeNull();
        (await DetalheAsync(admin, meu)).Nome.Should().Be("Eu Mesmo", "o endpoint de foto não mexe no resto da ficha");
    }

    [Fact]
    public async Task Usuario_sem_vinculo_com_profissional_nao_tem_minha_foto()
    {
        var (_, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var recepcao = await _fabrica.LogarNovoUsuarioAsync(negocioId, Perfil.Recepcionista);

        (await EnviarAsync(recepcao, "/painel/minha-foto", Imagem(100, 100, SKEncodedImageFormat.Png), "a.png", "image/png"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await recepcao.DeleteAsync("/painel/minha-foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ArquivosAsync(negocioId)).Should().Be(0);
    }

    [Fact]
    public async Task Outro_negocio_nao_envia_nem_remove_a_foto()
    {
        var (adminA, negocioA, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var (adminB, negocioB, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(adminA, "Do A");
        var url = await UrlAsync(await EnviarAsync(adminA, $"/painel/profissionais/{id}/foto", Imagem(200, 200, SKEncodedImageFormat.Png), "a.png", "image/png"));

        (await EnviarAsync(adminB, $"/painel/profissionais/{id}/foto", Imagem(200, 200, SKEncodedImageFormat.Png), "b.png", "image/png"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await adminB.DeleteAsync($"/painel/profissionais/{id}/foto")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await DetalheAsync(adminA, id)).FotoUrl.Should().Be(url);
        (await ArquivosAsync(negocioB)).Should().Be(0, "nada é gravado no negócio de quem tentou");
        (await ArquivosAsync(negocioA)).Should().Be(1);
    }

    [Fact]
    public async Task Excluir_o_profissional_apaga_o_arquivo_da_foto()
    {
        var (admin, negocioId, _, _) = await _fabrica.CriarUsuarioELogarAsync(Perfil.Administrador);
        var id = await CriarProfissionalAsync(admin, "Sai Daqui");
        await UrlAsync(await EnviarAsync(admin, $"/painel/profissionais/{id}/foto", Imagem(200, 200, SKEncodedImageFormat.Png), "a.png", "image/png"));

        (await admin.DeleteAsync($"/painel/profissionais/{id}")).IsSuccessStatusCode.Should().BeTrue();

        (await ArquivosAsync(negocioId)).Should().Be(0);
    }

    // ---------------------------------------------------------------- apoio

    private static async Task<Guid> CriarProfissionalAsync(HttpClient cliente, string nome)
    {
        var resposta = await cliente.PostAsJsonAsync("/painel/profissionais", new CriarProfissional(nome));
        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        return await resposta.Content.ReadFromJsonAsync<Guid>();
    }

    private static async Task DefinirUrlExternaAsync(HttpClient admin, Guid id)
    {
        var detalhe = await DetalheAsync(admin, id);
        (await admin.PutAsJsonAsync($"/painel/profissionais/{id}", new AtualizarProfissional(detalhe.Nome, null, null, FotoUrl: UrlExterna)))
            .EnsureSuccessStatusCode();
    }

    private static async Task<ProfissionalDetalhe> DetalheAsync(HttpClient cliente, Guid id) =>
        (await cliente.GetFromJsonAsync<ProfissionalDetalhe>($"/painel/profissionais/{id}"))!;

    private Task<int> ArquivosAsync(Guid negocioId) =>
        _fabrica.NoBancoAsync(db => db.Arquivos.CountAsync(a => a.NegocioId == negocioId));

    private static async Task<JsonElement> ColunaDaGradeAsync(HttpClient cliente, Guid profissionalId)
    {
        var grade = await cliente.GetFromJsonAsync<JsonElement>($"/painel/agenda/grade?data={SemeadorDeComissoes.Dia(3):yyyy-MM-dd}");
        return grade.GetProperty("profissionais").EnumerateArray().Single(c => c.GetProperty("profissionalId").GetGuid() == profissionalId);
    }

    private async Task<HttpClient> ClientePublicoAsync(Guid negocioId)
    {
        var negocio = await _fabrica.NoBancoAsync(db => db.Negocios.IgnoreQueryFilters().SingleAsync(n => n.Id == negocioId));
        return _fabrica.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri($"http://{negocio.Slug.Valor}.{DominioDeTeste.Valor}/") });
    }

    private static Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string rota, byte[] conteudo, string nome, string tipo)
    {
        var arquivo = new ByteArrayContent(conteudo);
        arquivo.Headers.ContentType = new MediaTypeHeaderValue(tipo);
        return cliente.PutAsync(rota, new MultipartFormDataContent { { arquivo, "arquivo", nome } });
    }

    private static async Task<string> UrlAsync(HttpResponseMessage resposta)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await resposta.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("fotoUrl").GetString()!;
    }

    private static byte[] Imagem(int largura, int altura, SKEncodedImageFormat formato)
    {
        using var bitmap = new SKBitmap(largura, altura);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(new SKColor(120, 60, 30));
        using var imagem = SKImage.FromBitmap(bitmap);
        using var dados = imagem.Encode(formato, 90);
        return dados.ToArray();
    }

    /// <summary>Insere um segmento APP1 "Exif" logo depois do SOI do JPEG, com um texto que não pode chegar à saída.</summary>
    private static byte[] ComExif(byte[] jpeg)
    {
        var tiff = new byte[] { 0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00 };
        var carga = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff).Concat(Encoding.ASCII.GetBytes(MarcaNoExif)).ToArray();
        var tamanho = carga.Length + 2;
        var segmento = new byte[] { 0xFF, 0xE1, (byte)(tamanho >> 8), (byte)tamanho }.Concat(carga);
        return jpeg.Take(2).Concat(segmento).Concat(jpeg.Skip(2)).ToArray();
    }
}
