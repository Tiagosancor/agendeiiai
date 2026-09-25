using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

[ApiController]
[Route("painel/profissionais")]
[Authorize(Policy = nameof(Permissao.GerenciarProfissionais))]
public sealed class ProfissionaisController : ControllerBase
{
    private readonly IGerenciadorProfissionais _gerenciador;

    public ProfissionaisController(IGerenciadorProfissionais gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ProfissionalResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProfissionalDetalhe>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var profissional = await _gerenciador.ObterAsync(id, cancellationToken);
        return profissional is null ? NotFound() : Ok(profissional);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarProfissional dados, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _gerenciador.CriarAsync(dados, cancellationToken);
            return CreatedAtAction(nameof(Obter), new { id }, id);
        }
        catch (LimitePlanoAtingidoException excecao)
        {
            return LimiteAtingido(excecao);
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Atualizar(Guid id, AtualizarProfissional dados, CancellationToken cancellationToken) =>
        await _gerenciador.AtualizarDadosAsync(id, dados, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.DesativarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            return await _gerenciador.AtivarAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (LimitePlanoAtingidoException excecao)
        {
            return LimiteAtingido(excecao);
        }
    }

    /// <summary>O front usa o <c>codigo</c> para mostrar o botão de mudar de plano (seção 7).</summary>
    private ObjectResult LimiteAtingido(LimitePlanoAtingidoException excecao) => Conflict(new ProblemDetails
    {
        Title = "Limite de profissionais do plano atingido.",
        Detail = excecao.Message,
        Extensions = { ["codigo"] = "limite_profissionais", ["maximo"] = excecao.Maximo },
    });

    [HttpGet("{id:guid}/cpf")]
    public async Task<ActionResult<string>> RevelarCpf(Guid id, CancellationToken cancellationToken)
    {
        var cpf = await _gerenciador.RevelarCpfAsync(id, cancellationToken);
        return cpf is null ? NotFound() : Ok(cpf);
    }
}
