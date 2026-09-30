using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Arquivos;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>Perfil do negócio (marca, endereço, horário de funcionamento — seção 5 / Sprint 1).</summary>
[ApiController]
[Route("painel/negocio")]
[Authorize(Policy = nameof(Permissao.GerenciarConfiguracoesDoNegocio))]
public sealed class PerfilNegocioController : ControllerBase
{
    // Folga para o envelope multipart em volta do arquivo; o limite do arquivo em si é conferido no processamento.
    private const long LimiteRequisicaoImagem = LimitesImagem.BytesMaximos + 64 * 1024;

    private readonly IGerenciadorPerfilNegocio _gerenciador;

    public PerfilNegocioController(IGerenciadorPerfilNegocio gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<PerfilNegocio>> Obter(CancellationToken cancellationToken)
    {
        var perfil = await _gerenciador.ObterAsync(cancellationToken);
        return perfil is null ? NotFound() : Ok(perfil);
    }

    [HttpPut]
    public async Task<IActionResult> Atualizar(AtualizarPerfilNegocio dados, CancellationToken cancellationToken)
    {
        try
        {
            return await _gerenciador.AtualizarAsync(dados, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message.Split(" (Parameter")[0] });
        }
    }

    /// <summary>Imagem de fundo da página e do assistente (seção 5): JPG, PNG ou WebP até 5 MB, regravada em WebP.</summary>
    [HttpPut("imagem-fundo")]
    [RequestSizeLimit(LimiteRequisicaoImagem)]
    [RequestFormLimits(MultipartBodyLengthLimit = LimiteRequisicaoImagem)]
    public async Task<IActionResult> EnviarImagemFundo(IFormFile? arquivo, CancellationToken cancellationToken)
    {
        if (arquivo is null || arquivo.Length == 0)
            return BadRequest(new ProblemDetails { Title = "Envie uma imagem." });
        if (arquivo.Length > LimitesImagem.BytesMaximos)
            return BadRequest(new ProblemDetails { Title = $"A imagem pode ter até {LimitesImagem.BytesMaximos / (1024 * 1024)} MB." });

        byte[] conteudo;
        using (var memoria = new MemoryStream((int)arquivo.Length))
        {
            await arquivo.CopyToAsync(memoria, cancellationToken);
            conteudo = memoria.ToArray();
        }

        try
        {
            var url = await _gerenciador.DefinirImagemFundoAsync(conteudo, cancellationToken);
            return url is null ? NotFound() : Ok(new { imagemFundoUrl = url });
        }
        catch (ImagemInvalidaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }

    [HttpDelete("imagem-fundo")]
    public async Task<IActionResult> RemoverImagemFundo(CancellationToken cancellationToken) =>
        await _gerenciador.RemoverImagemFundoAsync(cancellationToken) ? NoContent() : NotFound();
}
