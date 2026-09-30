using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Arquivos;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>
/// Serve os arquivos do <see cref="IArmazenamentoArquivos"/> gravado no banco (hoje, a imagem de fundo do negócio).
/// Anônimo e fora de <c>/publico</c>: a mesma URL vale na página do negócio e no painel. O conteúdo de uma chave nunca muda
/// (trocar a imagem gera outra chave), então o cache é longo.
/// </summary>
[ApiController]
public sealed class ArquivosController : ControllerBase
{
    private readonly IArmazenamentoArquivos _arquivos;

    public ArquivosController(IArmazenamentoArquivos arquivos) => _arquivos = arquivos;

    [HttpGet("/arquivos/{chave}")]
    public async Task<IActionResult> Obter(string chave, CancellationToken cancellationToken)
    {
        if (chave.Length is < 16 or > 64 || !chave.All(char.IsAsciiHexDigitLower))
            return NotFound();

        var arquivo = await _arquivos.ObterAsync(chave, cancellationToken);
        if (arquivo is null)
            return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(arquivo.Conteudo, arquivo.TipoConteudo);
    }
}
