using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Arquivos;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Envio da foto do profissional (multipart, campo <c>arquivo</c>), compartilhado pela rota de quem gerencia profissionais e
/// pela "minha foto" do próprio profissional. Mesmos limites da imagem de fundo: até 5 MB, conferidos de novo no processamento.
/// </summary>
internal static class FotoEnviada
{
    /// <summary>Folga para o envelope multipart em volta do arquivo.</summary>
    public const long LimiteRequisicao = LimitesImagem.BytesMaximos + 64 * 1024;

    public static async Task<IActionResult> EnviarAsync(
        ControllerBase controller, IFormFile? arquivo, Func<byte[], Task<string?>> definir, CancellationToken cancellationToken)
    {
        if (arquivo is null || arquivo.Length == 0)
            return controller.BadRequest(new ProblemDetails { Title = "Envie uma imagem." });
        if (arquivo.Length > LimitesImagem.BytesMaximos)
            return controller.BadRequest(new ProblemDetails { Title = $"A imagem pode ter até {LimitesImagem.BytesMaximos / (1024 * 1024)} MB." });

        byte[] conteudo;
        using (var memoria = new MemoryStream((int)arquivo.Length))
        {
            await arquivo.CopyToAsync(memoria, cancellationToken);
            conteudo = memoria.ToArray();
        }

        try
        {
            var url = await definir(conteudo);
            return url is null ? controller.NotFound() : controller.Ok(new { fotoUrl = url });
        }
        catch (ImagemInvalidaException excecao)
        {
            return controller.BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }
}
