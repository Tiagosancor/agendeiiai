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
    private readonly IUsuarioAtual _usuarioAtual;

    public AssinaturaController(IServicoAssinaturaNegocio servico, IUsuarioAtual usuarioAtual)
    {
        _servico = servico;
        _usuarioAtual = usuarioAtual;
    }

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
    public async Task<ActionResult<InstrucoesPagamento>> ObterInstrucoes(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _servico.ObterInstrucoesPagamentoAsync(cancellationToken));
        }
        catch (FalhaGatewayPagamentoException excecao)
        {
            return FalhaGateway(excecao);
        }
    }

    /// <summary>
    /// Contrata pelo gateway automático (Asaas): CPF/CNPJ do titular vai direto ao gateway (aqui fica só o mascarado). Devolve o
    /// link da cobrança — Pix, boleto ou cartão na página do gateway; nenhum dado de cartão passa por aqui.
    /// </summary>
    public sealed record ContratarRequisicao(Guid PlanoId, Periodicidade Periodicidade, string CpfCnpj);

    [HttpPost("contratar")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<ActionResult<InstrucoesPagamento>> Contratar(ContratarRequisicao requisicao, CancellationToken cancellationToken)
    {
        // O gateway precisa do e-mail do titular, e a claim "email" só existe com e-mail verificado.
        var email = _usuarioAtual.Email;
        if (email is null)
            return BadRequest(new ProblemDetails
            {
                Title = "Confirme um e-mail em Minha conta para contratar.",
                Extensions = { ["codigo"] = "email_necessario" },
            });

        try
        {
            return Ok(await _servico.ContratarAsync($"painel:{Autor()}", email, requisicao.PlanoId, requisicao.Periodicidade, requisicao.CpfCnpj, cancellationToken));
        }
        catch (LimiteProfissionaisExcedidoException excecao)
        {
            return LimiteExcedido(excecao);
        }
        catch (RegraAssinaturaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
        catch (ArgumentException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message.Split(" (Parameter")[0] });
        }
        catch (FalhaGatewayPagamentoException excecao)
        {
            return FalhaGateway(excecao);
        }
    }

    /// <summary>Usa até o fim do período pago (ou do teste); nenhuma cobrança nova. <c>imediato</c>: já não havia período pela frente.</summary>
    [HttpPost("cancelar")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<IActionResult> Cancelar(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new { imediato = await _servico.CancelarAsync($"painel:{Autor()}", cancellationToken) });
        }
        catch (RegraAssinaturaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
        catch (FalhaGatewayPagamentoException excecao)
        {
            return FalhaGateway(excecao);
        }
    }

    public sealed record TrocarPlanoRequisicao(Guid PlanoId, Periodicidade Periodicidade);

    [HttpPost("plano")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<IActionResult> TrocarPlano(TrocarPlanoRequisicao requisicao, CancellationToken cancellationToken)
    {
        try
        {
            var autor = $"painel:{Autor()}";
            await _servico.TrocarPlanoAsync(autor, requisicao.PlanoId, requisicao.Periodicidade, cancellationToken);
            return NoContent();
        }
        catch (LimiteProfissionaisExcedidoException excecao)
        {
            return LimiteExcedido(excecao);
        }
        catch (RegraAssinaturaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
        catch (FalhaGatewayPagamentoException excecao)
        {
            return FalhaGateway(excecao);
        }
    }

    /// <summary>Quem fez a ação, para o histórico: e-mail verificado, @usuário ou nome — nunca telefone.</summary>
    private string Autor() => _usuarioAtual.Rotulo ?? "desconhecido";

    private ConflictObjectResult LimiteExcedido(LimiteProfissionaisExcedidoException excecao) => Conflict(new ProblemDetails
    {
        Title = "Mais profissionais ativos do que o plano permite.",
        Detail = excecao.Message,
        Extensions = { ["codigo"] = "limite_profissionais", ["excedente"] = excecao.Excedente },
    });

    /// <summary>Recusa do gateway (ex.: CPF inválido para ele) → 400 com o texto dele; fora do ar → 502 com texto genérico.</summary>
    private ObjectResult FalhaGateway(FalhaGatewayPagamentoException excecao) => excecao.Recusado
        ? BadRequest(new ProblemDetails { Title = excecao.Message, Extensions = { ["codigo"] = "gateway_recusou" } })
        : StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails { Title = excecao.Message, Extensions = { ["codigo"] = "gateway_indisponivel" } });
}
