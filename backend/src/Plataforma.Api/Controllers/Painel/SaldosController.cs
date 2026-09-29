using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Comissoes;
using Plataforma.Dominio.Comissoes;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Saldo devedor dos profissionais (seção 7): vales e consumo interno. Ver: quem vê as comissões de todos, lança vales
/// ou gerencia o estoque. Vale é de "lançar vales"; consumo (lançado no estoque) é de "gerenciar estoque" — editar e
/// excluir seguem o tipo do lançamento. O profissional vê o dele em <c>/painel/comissoes/minhas/saldo</c>.
/// </summary>
[ApiController]
[Route("painel/saldos")]
[Authorize]
public sealed class SaldosController : ControllerBase
{
    private readonly IServicoSaldoDevedor _saldos;

    public SaldosController(IServicoSaldoDevedor saldos) => _saldos = saldos;

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<SaldoDoProfissional>>> Resumir(CancellationToken cancellationToken) =>
        PodeVer() ? Ok(await _saldos.ResumirAsync(cancellationToken)) : Forbid();

    [HttpGet("profissionais/{id:guid}")]
    public async Task<ActionResult<SaldoDevedor>> Detalhar(Guid id, CancellationToken cancellationToken)
    {
        if (!PodeVer())
            return Forbid();
        return await _saldos.DetalharAsync(id, cancellationToken) is { } saldo ? Ok(saldo) : NotFound();
    }

    [HttpPost("vales")]
    [Authorize(Policy = nameof(Permissao.LancarVales))]
    public async Task<IActionResult> LancarVale(LancarVale dados, CancellationToken cancellationToken)
    {
        try
        {
            return StatusCode(StatusCodes.Status201Created, await _saldos.LancarValeAsync(dados, cancellationToken));
        }
        catch (ArgumentException excecao)
        {
            return BadRequest(new ProblemDetails { Title = Mensagem(excecao) });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Alterar(Guid id, AlterarLancamentoSaldo dados, CancellationToken cancellationToken)
    {
        try
        {
            return Responder(await _saldos.AlterarAsync(id, dados, PodeMexer, cancellationToken));
        }
        catch (ArgumentException excecao)
        {
            return BadRequest(new ProblemDetails { Title = Mensagem(excecao) });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Excluir(Guid id, [FromQuery] bool confirmarQuitado, CancellationToken cancellationToken) =>
        Responder(await _saldos.ExcluirAsync(id, confirmarQuitado, PodeMexer, cancellationToken));

    private IActionResult Responder(ResultadoLancamentoSaldo resultado) => resultado switch
    {
        { Sucesso: true } => NoContent(),
        { Erro: ErroLancamentoSaldo.NaoEncontrado } => NotFound(),
        { Erro: ErroLancamentoSaldo.SemPermissao } => Forbid(),
        _ => Conflict(new { title = resultado.Mensagem, codigo = "quitado" }),
    };

    private bool PodeVer() =>
        Tem(Permissao.VerComissoesDeTodos) || Tem(Permissao.LancarVales) || Tem(Permissao.GerenciarEstoque);

    private bool PodeMexer(string tipo) =>
        tipo == nameof(TipoLancamentoSaldo.Vale) ? Tem(Permissao.LancarVales) : Tem(Permissao.GerenciarEstoque);

    private bool Tem(Permissao permissao) => User.HasClaim(ClaimsPlataforma.Permissao, permissao.ToString());

    private static string Mensagem(ArgumentException excecao) =>
        excecao.ParamName is null ? excecao.Message : excecao.Message.Replace($" (Parameter '{excecao.ParamName}')", string.Empty);
}
