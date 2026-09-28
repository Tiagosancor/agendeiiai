using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Quinzenas de acerto de comissões (seção 7). Ver exige "ver comissões de todos"; criar, editar,
/// fechar e reabrir exigem "gerenciar comissões". A visão do próprio profissional fica em
/// <c>ComissoesController</c> (<c>/painel/comissoes/minhas/quinzenas</c>).
/// </summary>
[ApiController]
[Route("painel/quinzenas")]
[Authorize(Policy = nameof(Permissao.VerComissoesDeTodos))]
public sealed class QuinzenasController : ControllerBase
{
    private readonly IServicoQuinzenas _servico;

    public QuinzenasController(IServicoQuinzenas servico) => _servico = servico;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<QuinzenaResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _servico.ListarAsync(cancellationToken));

    [HttpGet("sugestao")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<ActionResult<SugestaoQuinzena>> Sugerir(CancellationToken cancellationToken) =>
        Ok(await _servico.SugerirProximaAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<DetalheQuinzena>> Detalhar(Guid id, CancellationToken cancellationToken)
    {
        var detalhe = await _servico.DetalharAsync(id, cancellationToken);
        return detalhe is null ? NotFound() : Ok(detalhe);
    }

    [HttpPost]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> Criar(DatasQuinzenaRequisicao dados, CancellationToken cancellationToken)
    {
        var resultado = await _servico.CriarAsync(dados.Inicio, dados.Fim, cancellationToken);
        return resultado.Sucesso
            ? StatusCode(StatusCodes.Status201Created, new QuinzenaSalvaResposta(resultado.PeriodoId!.Value, resultado.Aviso))
            : Traduzir(resultado);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> Alterar(Guid id, DatasQuinzenaRequisicao dados, CancellationToken cancellationToken)
    {
        var resultado = await _servico.AlterarAsync(id, dados.Inicio, dados.Fim, cancellationToken);
        return resultado.Sucesso ? Ok(new QuinzenaSalvaResposta(id, resultado.Aviso)) : Traduzir(resultado);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken)
    {
        var resultado = await _servico.ExcluirAsync(id, cancellationToken);
        return resultado.Sucesso ? NoContent() : Traduzir(resultado);
    }

    [HttpPost("{id:guid}/fechar")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> Fechar(Guid id, FecharQuinzenaRequisicao? dados, CancellationToken cancellationToken)
    {
        var resultado = await _servico.FecharAsync(id, dados?.ConfirmarPendentes ?? false, cancellationToken);
        return resultado.Sucesso ? NoContent() : Traduzir(resultado);
    }

    [HttpPost("{id:guid}/reabrir")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> Reabrir(Guid id, ReabrirQuinzenaRequisicao? dados, CancellationToken cancellationToken)
    {
        var resultado = await _servico.ReabrirAsync(id, dados?.Motivo, cancellationToken);
        return resultado.Sucesso ? NoContent() : Traduzir(resultado);
    }

    private ObjectResult Traduzir(ResultadoQuinzena resultado)
    {
        var status = resultado.Erro switch
        {
            ErroQuinzena.NaoEncontrada => StatusCodes.Status404NotFound,
            ErroQuinzena.DatasInvalidas or ErroQuinzena.MotivoObrigatorio => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status409Conflict,
        };

        var problema = new ProblemDetails { Status = status, Title = resultado.Mensagem, Detail = resultado.Mensagem };
        problema.Extensions["codigo"] = resultado.Erro switch
        {
            ErroQuinzena.Sobreposicao => "sobreposicao",
            ErroQuinzena.PendentesSemConfirmacao => "pendentes",
            ErroQuinzena.JaFechada => "quinzena_fechada",
            ErroQuinzena.NaoFechada => "quinzena_aberta",
            _ => resultado.Erro?.ToString(),
        };
        if (resultado.Pendentes is { } pendentes)
            problema.Extensions["pendentes"] = pendentes;

        return new ObjectResult(problema) { StatusCode = status };
    }
}

public sealed record DatasQuinzenaRequisicao(DateOnly Inicio, DateOnly Fim);

public sealed record QuinzenaSalvaResposta(Guid Id, string? Aviso);

public sealed record FecharQuinzenaRequisicao(bool ConfirmarPendentes);

public sealed record ReabrirQuinzenaRequisicao(string? Motivo);
