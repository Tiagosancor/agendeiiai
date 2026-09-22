using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Negocios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>Perfil do negócio (marca, endereço, horário de funcionamento — seção 5 / Sprint 1).</summary>
[ApiController]
[Route("painel/negocio")]
[Authorize(Policy = nameof(Permissao.GerenciarConfiguracoesDoNegocio))]
public sealed class PerfilNegocioController : ControllerBase
{
    private readonly IGerenciadorPerfilNegocio _gerenciador;

    public PerfilNegocioController(IGerenciadorPerfilNegocio gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<PerfilNegocio>> Obter(CancellationToken cancellationToken)
    {
        var perfil = await _gerenciador.ObterAsync(cancellationToken);
        return perfil is null ? NotFound() : Ok(perfil);
    }

    [HttpPut]
    public async Task<IActionResult> Atualizar(AtualizarPerfilNegocio dados, CancellationToken cancellationToken) =>
        await _gerenciador.AtualizarAsync(dados, cancellationToken) ? NoContent() : NotFound();
}
