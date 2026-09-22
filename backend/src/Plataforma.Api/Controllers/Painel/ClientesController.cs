using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/clientes")]
[Authorize(Policy = nameof(Permissao.GerenciarClientes))]
public sealed class ClientesController : ControllerBase
{
    private readonly IGerenciadorClientes _gerenciador;

    public ClientesController(IGerenciadorClientes gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ClienteResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ClienteResumo>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var cliente = await _gerenciador.ObterAsync(id, cancellationToken);
        return cliente is null ? NotFound() : Ok(cliente);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarCliente dados, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _gerenciador.CriarAsync(dados, cancellationToken);
            return CreatedAtAction(nameof(Obter), new { id }, id);
        }
        catch (TelefoneJaCadastradoException excecao)
        {
            return Conflict(new ProblemDetails { Title = "Telefone já cadastrado.", Detail = excecao.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, AtualizarCliente dados, CancellationToken cancellationToken) =>
        await _gerenciador.AtualizarAsync(id, dados, cancellationToken) ? NoContent() : NotFound();
}
