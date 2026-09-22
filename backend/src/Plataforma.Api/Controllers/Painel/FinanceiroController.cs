using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Financeiro;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>Faturamento por período/profissional/serviço e "painel do mês" (seção 7) — o mês é só este resumo com início/fim do mês corrente, decidido pelo chamador.</summary>
[ApiController]
[Route("painel/financeiro")]
[Authorize(Policy = nameof(Permissao.VerFinanceiro))]
public sealed class FinanceiroController : ControllerBase
{
    private readonly IServicoFinanceiro _servico;

    public FinanceiroController(IServicoFinanceiro servico)
    {
        _servico = servico;
    }

    [HttpGet("resumo")]
    public async Task<ActionResult<ResumoFinanceiro>> ObterResumo(
        [FromQuery] DateOnly inicio, [FromQuery] DateOnly fim, [FromQuery] Guid? profissionalId,
        [FromQuery] Guid? servicoId, CancellationToken cancellationToken)
    {
        var resumo = await _servico.ObterResumoAsync(new FiltroFinanceiro(inicio, fim, profissionalId, servicoId), cancellationToken);
        return Ok(resumo);
    }
}
