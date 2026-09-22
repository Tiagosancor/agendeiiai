using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/categorias")]
[Authorize(Policy = nameof(Permissao.GerenciarServicos))]
public sealed class CategoriasController : ControllerBase
{
    private readonly IGerenciadorCategorias _gerenciador;

    public CategoriasController(IGerenciadorCategorias gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CategoriaResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarCategoriaRequisicao dados, CancellationToken cancellationToken)
    {
        var id = await _gerenciador.CriarAsync(dados.Nome, cancellationToken);
        return CreatedAtAction(nameof(Listar), id);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Renomear(Guid id, CriarCategoriaRequisicao dados, CancellationToken cancellationToken) =>
        await _gerenciador.RenomearAsync(id, dados.Nome, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.DesativarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.AtivarAsync(id, cancellationToken) ? NoContent() : NotFound();
}

public sealed record CriarCategoriaRequisicao(string Nome);
