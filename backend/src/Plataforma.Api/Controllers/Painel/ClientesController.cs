using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Api.Assinaturas;
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
    [PermitirComAssinaturaSuspensa]
    public async Task<ActionResult<IReadOnlyList<ClienteResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    [PermitirComAssinaturaSuspensa]
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

    /// <summary>Exportação de dados sob demanda (LGPD, seção 8.4).</summary>
    [HttpGet("{id:guid}/exportar")]
    [PermitirComAssinaturaSuspensa]
    public async Task<ActionResult<ExportacaoCliente>> Exportar(Guid id, CancellationToken cancellationToken)
    {
        var exportacao = await _gerenciador.ExportarAsync(id, cancellationToken);
        return exportacao is null ? NotFound() : Ok(exportacao);
    }

    /// <summary>Exclusão sob demanda (LGPD, seção 8.4) — anonimiza, nunca apaga a linha.</summary>
    [HttpPost("{id:guid}/excluir")]
    [PermitirComAssinaturaSuspensa]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.ExcluirAsync(id, cancellationToken) ? NoContent() : NotFound();
}
