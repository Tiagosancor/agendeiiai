using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Ajuste de valor durante o atendimento (seção 7). Ajustar exige a permissão "ajustar valor do
/// atendimento" (desligada para todos, só o Administrador a tem de origem); o alcance (só os
/// próprios, para o Profissional; só o Administrador, depois de concluído) fica no serviço.
/// </summary>
[ApiController]
[Route("painel/agendamentos/{id:guid}")]
[Authorize]
public sealed class AjustesAtendimentoController : ControllerBase
{
    private readonly IServicoAjustesAtendimento _servico;

    public AjustesAtendimentoController(IServicoAjustesAtendimento servico) => _servico = servico;

    [HttpGet("valores")]
    public async Task<ActionResult<ValoresAtendimento>> Valores(Guid id, CancellationToken cancellationToken)
    {
        var valores = await _servico.ObterValoresAsync(id, cancellationToken);
        return valores is null ? NotFound() : Ok(valores);
    }

    [HttpPost("servicos/{linhaId:guid}/ajuste")]
    [Authorize(Policy = nameof(Permissao.AjustarValorAtendimento))]
    public async Task<IActionResult> Ajustar(Guid id, Guid linhaId, AjustarValor dados, CancellationToken cancellationToken)
    {
        var resultado = await _servico.AjustarAsync(id, linhaId, dados, cancellationToken);
        if (resultado.Sucesso)
            return Ok(new { valorFinal = resultado.ValorFinal });

        var status = resultado.Erro switch
        {
            ErroAjusteValor.NaoEncontrado => StatusCodes.Status404NotFound,
            ErroAjusteValor.SemAcesso => StatusCodes.Status403Forbidden,
            ErroAjusteValor.StatusInvalido => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest,
        };
        return Problem(statusCode: status, detail: resultado.Mensagem, title: resultado.Mensagem);
    }
}
