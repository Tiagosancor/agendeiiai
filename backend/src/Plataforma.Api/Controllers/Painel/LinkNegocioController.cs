using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cadastro;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Link público do negócio em "Meu negócio" (seção 5, "Editar o link depois do cadastro"). Quem configura o negócio vê o
/// link; só o Administrador troca (403 para os demais).
/// </summary>
[ApiController]
[Route("painel/negocio/link")]
public sealed class LinkNegocioController : ControllerBase
{
    private readonly IGerenciadorLinkNegocio _gerenciador;

    public LinkNegocioController(IGerenciadorLinkNegocio gerenciador) => _gerenciador = gerenciador;

    public sealed record SituacaoLink(string Slug, string Url, DateTimeOffset? UltimaTrocaEm, DateTimeOffset? ProximaTrocaEm, bool PodeEditar);

    public sealed record TrocarLinkRequisicao(string Slug);

    [HttpGet]
    [Authorize(Policy = nameof(Permissao.GerenciarConfiguracoesDoNegocio))]
    public async Task<ActionResult<SituacaoLink>> Obter(CancellationToken cancellationToken)
    {
        var link = await _gerenciador.ObterAsync(cancellationToken);
        return link is null ? NotFound() : Ok(Situacao(link));
    }

    [HttpGet("disponibilidade")]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<ActionResult<DisponibilidadeSlug>> Disponibilidade([FromQuery] string? slug, CancellationToken cancellationToken) =>
        Ok(await _gerenciador.VerificarAsync(slug ?? string.Empty, cancellationToken));

    [HttpPut]
    [Authorize(Policy = ClaimsPlataforma.PoliticaSomenteAdministradorNegocio)]
    public async Task<IActionResult> Trocar(TrocarLinkRequisicao requisicao, CancellationToken cancellationToken)
    {
        var resultado = await _gerenciador.TrocarAsync(requisicao.Slug ?? string.Empty, cancellationToken);

        return resultado.Erro switch
        {
            null => Ok(Situacao(resultado.Link!)),
            ErroTrocaLink.Invalido => BadRequest(new ProblemDetails { Title = resultado.Mensagem }),
            ErroTrocaLink.EmUso => Conflict(Problema(resultado.Mensagem!, "slug_em_uso")),
            _ => Conflict(Problema(resultado.Mensagem!, "troca_recente", resultado.PodeTrocarEm)),
        };
    }

    private SituacaoLink Situacao(LinkNegocio link) => new(
        link.Slug, link.Url, link.UltimaTrocaEm, link.ProximaTrocaEm,
        User.HasClaim(ClaimsPlataforma.Perfil, nameof(Perfil.Administrador)));

    private static ProblemDetails Problema(string mensagem, string codigo, DateTimeOffset? podeTrocarEm = null)
    {
        var problema = new ProblemDetails { Title = mensagem, Status = StatusCodes.Status409Conflict };
        problema.Extensions["codigo"] = codigo;
        if (podeTrocarEm is not null)
            problema.Extensions["podeTrocarEm"] = podeTrocarEm;
        return problema;
    }
}
