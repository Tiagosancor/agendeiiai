using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/pagamentos")]
[Authorize(Policy = nameof(Permissao.VerFinanceiro))]
public sealed class PagamentosController : ControllerBase
{
    private readonly IGerenciadorPagamentos _gerenciador;

    public PagamentosController(IGerenciadorPagamentos gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Registrar(RegistrarPagamento dados, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _gerenciador.RegistrarAsync(dados, cancellationToken);
            return StatusCode(StatusCodes.Status201Created, id);
        }
        catch (InvalidOperationException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }

    [HttpGet("agendamento/{agendamentoId:guid}")]
    public async Task<ActionResult<PagamentoResumo>> ObterPorAgendamento(Guid agendamentoId, CancellationToken cancellationToken)
    {
        var pagamento = await _gerenciador.ObterPorAgendamentoAsync(agendamentoId, cancellationToken);
        return pagamento is null ? NotFound() : Ok(pagamento);
    }
}
