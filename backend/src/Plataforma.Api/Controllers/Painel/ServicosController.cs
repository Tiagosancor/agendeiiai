using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/servicos")]
[Authorize(Policy = nameof(Permissao.GerenciarServicos))]
public sealed class ServicosController : ControllerBase
{
    private readonly IGerenciadorServicos _gerenciador;

    public ServicosController(IGerenciadorServicos gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ServicoResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarServico dados, CancellationToken cancellationToken)
    {
        var id = await _gerenciador.CriarAsync(dados, cancellationToken);
        return CreatedAtAction(nameof(Listar), id);
    }

    /// <summary>
    /// Exige também <see cref="Permissao.EditarCadastros"/> (seção 7). Novo preço/duração valem
    /// só para agendamentos novos.
    /// </summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.EditarCadastros))]
    public async Task<IActionResult> Atualizar(Guid id, AtualizarServico dados, CancellationToken cancellationToken) =>
        await _gerenciador.AtualizarAsync(id, dados, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.DesativarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.AtivarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpGet("{id:guid}/exclusao")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<ActionResult<PreviaExclusao>> PreviaExclusao(Guid id, CancellationToken cancellationToken)
    {
        var previa = await _gerenciador.ObterPreviaExclusaoAsync(id, cancellationToken);
        return previa is null ? NotFound() : Ok(previa);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken) =>
        RespostasCadastro.Exclusao(this, await _gerenciador.ExcluirAsync(id, cancellationToken));
}
