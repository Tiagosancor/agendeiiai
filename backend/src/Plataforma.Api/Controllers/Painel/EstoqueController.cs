using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Cadastros;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Aplicacao.Estoque;
using Plataforma.Dominio.Estoque;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Estoque (seção 7): cadastro de produtos, entrada, ajuste e histórico — "gerenciar estoque", que é também
/// quem vê o preço de custo. As listas de alerta (sem custo) valem também para quem vende produtos.
/// </summary>
[ApiController]
[Route("painel/estoque")]
[Authorize(Policy = nameof(Permissao.GerenciarEstoque))]
public sealed class EstoqueController : ControllerBase
{
    private readonly IGerenciadorEstoque _estoque;
    private readonly IServicoSaldoDevedor _saldos;

    public EstoqueController(IGerenciadorEstoque estoque, IServicoSaldoDevedor saldos)
    {
        _estoque = estoque;
        _saldos = saldos;
    }

    [HttpGet("produtos")]
    public async Task<ActionResult<IReadOnlyList<ProdutoResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _estoque.ListarAsync(cancellationToken));

    [HttpPost("produtos")]
    public Task<IActionResult> Criar(CriarProduto dados, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => StatusCode(StatusCodes.Status201Created, await _estoque.CriarAsync(dados, cancellationToken)));

    [HttpPut("produtos/{id:guid}")]
    public Task<IActionResult> Atualizar(Guid id, AtualizarProduto dados, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => await _estoque.AtualizarAsync(id, dados, cancellationToken) ? NoContent() : NotFound());

    [HttpPost("produtos/{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken cancellationToken) =>
        await _estoque.AlterarAtivoAsync(id, ativo: true, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("produtos/{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken) =>
        await _estoque.AlterarAtivoAsync(id, ativo: false, cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("produtos/{id:guid}")]
    public Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => await _estoque.ExcluirAsync(id, cancellationToken) ? NoContent() : NotFound());

    [HttpPost("produtos/{id:guid}/entradas")]
    public Task<IActionResult> Entrada(Guid id, RegistrarEntrada dados, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => await _estoque.RegistrarEntradaAsync(id, dados, cancellationToken) is { } produto ? Ok(produto) : NotFound());

    [HttpPost("produtos/{id:guid}/ajustes")]
    public Task<IActionResult> Ajuste(Guid id, RegistrarAjuste dados, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => await _estoque.RegistrarAjusteAsync(id, dados, cancellationToken) is { } produto ? Ok(produto) : NotFound());

    /// <summary>Consumo interno (seção 7): baixa o estoque e vira saldo devedor do profissional, pelo custo (editável).</summary>
    [HttpPost("produtos/{id:guid}/consumos")]
    public Task<IActionResult> Consumo(Guid id, LancarConsumo dados, CancellationToken cancellationToken) =>
        TraduzirAsync(async () => await _saldos.LancarConsumoAsync(id, dados, cancellationToken) is { } lancamentoId
            ? StatusCode(StatusCodes.Status201Created, lancamentoId)
            : NotFound());

    [HttpGet("produtos/{id:guid}/movimentos")]
    public async Task<ActionResult<IReadOnlyList<MovimentoEstoqueResumo>>> Movimentos(Guid id, CancellationToken cancellationToken) =>
        await _estoque.ListarMovimentosAsync(id, cancellationToken) is { } movimentos ? Ok(movimentos) : NotFound();

    private async Task<IActionResult> TraduzirAsync(Func<Task<IActionResult>> acao)
    {
        try
        {
            return await acao();
        }
        catch (OperacaoCadastroBloqueadaException excecao)
        {
            return RespostasCadastro.Bloqueado(this, excecao.Message);
        }
        catch (EstoqueInsuficienteException excecao)
        {
            return Conflict(new { title = excecao.Message, codigo = "estoque_insuficiente", disponivel = excecao.Disponivel });
        }
        catch (ArgumentException excecao)
        {
            return BadRequest(new ProblemDetails { Title = excecao.Message });
        }
    }
}

/// <summary>
/// "Estoque baixo" e "Esgotado" — o indicador da tela inicial (seção 7). Só nomes e quantidades, então vale
/// para quem gerencia o estoque ou vende produtos.
/// </summary>
[ApiController]
[Route("painel/estoque/alertas")]
[Authorize]
public sealed class AlertasEstoqueController : ControllerBase
{
    private readonly IGerenciadorEstoque _estoque;

    public AlertasEstoqueController(IGerenciadorEstoque estoque) => _estoque = estoque;

    [HttpGet]
    public async Task<ActionResult<AlertasEstoque>> Obter(CancellationToken cancellationToken)
    {
        if (!User.HasClaim(ClaimsPlataforma.Permissao, nameof(Permissao.GerenciarEstoque))
            && !User.HasClaim(ClaimsPlataforma.Permissao, nameof(Permissao.VenderProdutos)))
            return Forbid();

        return Ok(await _estoque.ObterAlertasAsync(cancellationToken));
    }
}
