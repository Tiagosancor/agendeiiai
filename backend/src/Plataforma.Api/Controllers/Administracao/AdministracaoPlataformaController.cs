using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Administracao;
using Plataforma.Dominio.Assinaturas;

namespace Plataforma.Api.Controllers.Administracao;

/// <summary>Login da administração da plataforma — esquema e audiência próprios, nunca o token do painel.</summary>
[ApiController]
[Route("plataforma/auth")]
[AllowAnonymous]
public sealed class AutenticacaoPlataformaController : ControllerBase
{
    public const string PoliticaLoginPorIp = "LoginPlataformaPorIp";

    private readonly IAdministracaoPlataforma _administracao;

    public AutenticacaoPlataformaController(IAdministracaoPlataforma administracao) => _administracao = administracao;

    public sealed record LoginRequisicao(string Email, string Senha);

    [HttpPost("login")]
    [EnableRateLimiting(PoliticaLoginPorIp)]
    public async Task<IActionResult> Entrar(LoginRequisicao requisicao, CancellationToken cancellationToken)
    {
        var sessao = await _administracao.EntrarAsync(requisicao.Email, requisicao.Senha, cancellationToken);
        return sessao is null ? Unauthorized(new ProblemDetails { Title = "E-mail ou senha incorretos." }) : Ok(sessao);
    }
}

/// <summary>Negócios e assinaturas vistos pelo dono do produto (seção 7). Toda ação fica no log de auditoria.</summary>
[ApiController]
[Route("plataforma/negocios")]
[Authorize(Policy = ClaimsPlataforma.PoliticaAdministradorPlataforma)]
public sealed class AdministracaoPlataformaController : ControllerBase
{
    private readonly IAdministracaoPlataforma _administracao;

    public AdministracaoPlataformaController(IAdministracaoPlataforma administracao) => _administracao = administracao;

    private string Autor => $"plataforma:{User.FindFirst(ClaimTypes.Email)?.Value ?? User.FindFirst("email")?.Value}";

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<NegocioNaPlataforma>>> Listar([FromQuery] EstadoAssinatura? estado, CancellationToken cancellationToken) =>
        Ok(await _administracao.ListarNegociosAsync(estado, cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DetalheNegocioNaPlataforma>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var detalhe = await _administracao.ObterNegocioAsync(id, cancellationToken);
        return detalhe is null ? NotFound() : Ok(detalhe);
    }

    [HttpPost("{id:guid}/pagamentos")]
    public Task<IActionResult> RegistrarPagamento(Guid id, PagamentoManual dados, CancellationToken cancellationToken) =>
        ExecutarAsync(() => _administracao.RegistrarPagamentoAsync(Autor, id, dados, cancellationToken));

    public sealed record EstenderTesteRequisicao(int Dias);

    [HttpPost("{id:guid}/estender-teste")]
    public Task<IActionResult> EstenderTeste(Guid id, EstenderTesteRequisicao requisicao, CancellationToken cancellationToken) =>
        ExecutarAsync(() => _administracao.EstenderTesteAsync(Autor, id, requisicao.Dias, cancellationToken));

    public sealed record TrocarPlanoRequisicao(Guid PlanoId, Periodicidade Periodicidade);

    [HttpPost("{id:guid}/plano")]
    public Task<IActionResult> TrocarPlano(Guid id, TrocarPlanoRequisicao requisicao, CancellationToken cancellationToken) =>
        ExecutarAsync(() => _administracao.TrocarPlanoAsync(Autor, id, requisicao.PlanoId, requisicao.Periodicidade, cancellationToken));

    public sealed record SuspenderRequisicao(string? Motivo);

    [HttpPost("{id:guid}/suspender")]
    public Task<IActionResult> Suspender(Guid id, SuspenderRequisicao requisicao, CancellationToken cancellationToken) =>
        ExecutarAsync(() => _administracao.SuspenderAsync(Autor, id, requisicao.Motivo ?? string.Empty, cancellationToken));

    [HttpPost("{id:guid}/reativar")]
    public Task<IActionResult> Reativar(Guid id, CancellationToken cancellationToken) =>
        ExecutarAsync(() => _administracao.ReativarAsync(Autor, id, cancellationToken));

    private async Task<IActionResult> ExecutarAsync(Func<Task<bool>> acao)
    {
        try
        {
            return await acao() ? NoContent() : NotFound();
        }
        catch (LimiteProfissionaisExcedidoException excecao)
        {
            return Conflict(new ProblemDetails { Title = excecao.Message, Extensions = { ["codigo"] = "limite_profissionais" } });
        }
        catch (RegraAssinaturaException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }
}
