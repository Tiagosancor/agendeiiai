using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Venda de produto (seção 7), avulsa ou dentro do "Concluir atendimento" — tudo sob "vender produtos", inclusive a
/// busca de cliente e a lista de produtos (sem preço de custo).
/// </summary>
[ApiController]
[Route("painel/vendas")]
[Authorize(Policy = nameof(Permissao.VenderProdutos))]
public sealed class VendasController : ControllerBase
{
    private const int LimiteBusca = 10;
    private const int MaximoDiasListagem = 366;

    private readonly IServicoVendas _vendas;
    private readonly IBuscaClientes _clientes;

    public VendasController(IServicoVendas vendas, IBuscaClientes clientes)
    {
        _vendas = vendas;
        _clientes = clientes;
    }

    [HttpGet("opcoes")]
    public async Task<ActionResult<OpcoesVenda>> Opcoes([FromQuery] Guid? agendamentoId, CancellationToken cancellationToken) =>
        Ok(await _vendas.ObterOpcoesAsync(agendamentoId, cancellationToken));

    [HttpGet("clientes")]
    public async Task<ActionResult<IReadOnlyList<ClienteResumo>>> BuscarClientes([FromQuery] string? busca, CancellationToken cancellationToken) =>
        Ok(await _clientes.BuscarAsync(busca ?? string.Empty, LimiteBusca, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VendaResumo>>> Listar(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, CancellationToken cancellationToken)
    {
        if (de == default || ate == default || ate < de || ate.DayNumber - de.DayNumber > MaximoDiasListagem)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Período inválido.");

        return Ok(await _vendas.ListarAsync(de, ate, cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Lancar(LancarVenda dados, CancellationToken cancellationToken)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created, await _vendas.LancarAsync(dados, cancellationToken));
        }
        catch (EstoqueInsuficienteException excecao)
        {
            return Conflict(new { title = excecao.Message, codigo = "estoque_insuficiente", disponivel = excecao.Disponivel });
        }
        catch (ArgumentException excecao)
        {
            var mensagem = excecao.ParamName is null ? excecao.Message : excecao.Message.Replace($" (Parameter '{excecao.ParamName}')", string.Empty);
            return BadRequest(new ProblemDetails { Title = mensagem });
        }
    }
}
