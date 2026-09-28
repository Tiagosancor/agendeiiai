using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Comissoes;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Comissões dos profissionais (seção 7). "Minhas comissões" é de qualquer usuário do painel e
/// o profissional vem do token (não há parâmetro para trocar de pessoa); o resumo e o detalhe de
/// outros exigem "ver comissões de todos"; alterar o percentual exige "gerenciar comissões".
/// </summary>
[ApiController]
[Route("painel")]
[Authorize]
public sealed class ComissoesController : ControllerBase
{
    private readonly IServicoComissoes _servico;

    public ComissoesController(IServicoComissoes servico) => _servico = servico;

    [HttpGet("comissoes/minhas")]
    public async Task<ActionResult<ComissoesDoProfissional>> Minhas(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] int pagina = 1, [FromQuery] int tamanho = 20,
        CancellationToken cancellationToken = default)
    {
        if (ValidarFiltro(de, ate, pagina, tamanho) is { } erro)
            return erro;

        return Ok(await _servico.ListarMinhasAsync(new FiltroComissoes(de, ate, pagina, tamanho), cancellationToken));
    }

    [HttpGet("comissoes/resumo")]
    [Authorize(Policy = nameof(Permissao.VerComissoesDeTodos))]
    public async Task<ActionResult<IReadOnlyList<ResumoComissaoProfissional>>> Resumo(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, CancellationToken cancellationToken)
    {
        if (ValidarFiltro(de, ate, 1, 1) is { } erro)
            return erro;

        return Ok(await _servico.ResumirPorProfissionalAsync(de, ate, cancellationToken));
    }

    [HttpGet("comissoes/profissionais/{id:guid}")]
    [Authorize(Policy = nameof(Permissao.VerComissoesDeTodos))]
    public async Task<ActionResult<ComissoesDoProfissional>> DoProfissional(
        Guid id, [FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] int pagina = 1, [FromQuery] int tamanho = 20,
        CancellationToken cancellationToken = default)
    {
        if (ValidarFiltro(de, ate, pagina, tamanho) is { } erro)
            return erro;

        var resultado = await _servico.ListarDoProfissionalAsync(id, new FiltroComissoes(de, ate, pagina, tamanho), cancellationToken);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    /// <summary>Percentual atual, para a ficha do profissional: quem altera ou quem vê as comissões de todos.</summary>
    [HttpGet("profissionais/{id:guid}/comissao")]
    public async Task<ActionResult<PercentualComissaoResposta>> ObterPercentual(Guid id, CancellationToken cancellationToken)
    {
        if (!TemPermissao(Permissao.GerenciarComissoes) && !TemPermissao(Permissao.VerComissoesDeTodos))
            return Forbid();

        var percentual = await _servico.ObterPercentualAsync(id, cancellationToken);
        return percentual is null ? NotFound() : Ok(new PercentualComissaoResposta(percentual.Value));
    }

    [HttpPut("profissionais/{id:guid}/comissao")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> DefinirPercentual(Guid id, PercentualComissaoRequisicao dados, CancellationToken cancellationToken)
    {
        try
        {
            return await _servico.DefinirPercentualAsync(id, dados.Percentual, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException excecao)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: MensagemSemParametro(excecao));
        }
    }

    private ActionResult? ValidarFiltro(DateOnly de, DateOnly ate, int pagina, int tamanho)
    {
        if (de == default || ate == default)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Informe as datas de início e de fim.");
        if (ate < de)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "A data final não pode ser antes da inicial.");
        if (pagina < 1 || tamanho < 1 || tamanho > ServicoComissoes.TamanhoMaximoPagina)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: "Página inválida.");
        return null;
    }

    private bool TemPermissao(Permissao permissao) => User.HasClaim(ClaimsPlataforma.Permissao, permissao.ToString());

    /// <summary>Tira o " (Parameter 'x')" que o .NET acrescenta à mensagem.</summary>
    private static string MensagemSemParametro(ArgumentException excecao) =>
        excecao.ParamName is null ? excecao.Message : excecao.Message.Replace($" (Parameter '{excecao.ParamName}')", string.Empty);
}

public sealed record PercentualComissaoRequisicao(decimal Percentual);

public sealed record PercentualComissaoResposta(decimal Percentual);
