using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Api.Assinaturas;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Assinaturas;
using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Tela de assinatura do painel (seção 7): só o Administrador do negócio, e acessível mesmo
/// com a assinatura suspensa (é por aqui que o negócio regulariza). O aviso do topo do painel
/// vale para qualquer usuário logado.
/// </summary>
[ApiController]
[Route("painel/assinatura")]
[PermitirComAssinaturaSuspensa]
public sealed class AssinaturaController : ControllerBase
{
    private readonly IServicoAssinaturaNegocio _servico;

    public AssinaturaController(IServicoAssinaturaNegocio servico) => _servico = servico;

    [HttpGet]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<ActionResult<DetalheAssinaturaNegocio>> Obter(CancellationToken cancellationToken)
    {
        var detalhe = await _servico.ObterAsync(cancellationToken);
        return detalhe is null ? NotFound() : Ok(detalhe);
    }

    [HttpGet("aviso")]
    [Authorize]
    public async Task<IActionResult> ObterAviso(CancellationToken cancellationToken)
    {
        var aviso = await _servico.ObterAvisoAsync(cancellationToken);
        return aviso is null ? NoContent() : Ok(aviso);
    }

    [HttpGet("instrucoes-pagamento")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<ActionResult<InstrucoesPagamento>> ObterInstrucoes(CancellationToken cancellationToken) =>
        Ok(await _servico.ObterInstrucoesPagamentoAsync(cancellationToken));

    public sealed record TrocarPlanoRequisicao(Guid PlanoId, Periodicidade Periodicidade);

    [HttpPost("plano")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<IActionResult> TrocarPlano(TrocarPlanoRequisicao requisicao, CancellationToken cancellationToken)
    {
        try
        {
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value;
            var autor = $"painel:{email}";
            await _servico.TrocarPlanoAsync(autor, requisicao.PlanoId, requisicao.Periodicidade, cancellationToken);
            return NoContent();
        }
        catch (LimiteProfissionaisExcedidoException excecao)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Mais profissionais ativos do que o plano permite.",
                Detail = excecao.Message,
                Extensions = { ["codigo"] = "limite_profissionais", ["excedente"] = excecao.Excedente },
            });
        }
        catch (RegraAssinaturaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }
}
