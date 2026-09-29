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
    private readonly IServicoQuinzenas _quinzenas;
    private readonly IServicoSaldoDevedor _saldos;

    public ComissoesController(IServicoComissoes servico, IServicoQuinzenas quinzenas, IServicoSaldoDevedor saldos)
    {
        _servico = servico;
        _quinzenas = quinzenas;
        _saldos = saldos;
    }

    [HttpGet("comissoes/minhas")]
    public async Task<ActionResult<ComissoesDoProfissional>> Minhas(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, [FromQuery] int pagina = 1, [FromQuery] int tamanho = 20,
        CancellationToken cancellationToken = default)
    {
        if (ValidarFiltro(de, ate, pagina, tamanho) is { } erro)
            return erro;

        return Ok(await _servico.ListarMinhasAsync(new FiltroComissoes(de, ate, pagina, tamanho), cancellationToken));
    }

    /// <summary>Quinzenas do profissional logado (seção 7): a aberta, parcial, e as fechadas, com o valor final — só os dele.</summary>
    [HttpGet("comissoes/minhas/quinzenas")]
    public async Task<ActionResult<QuinzenasDoProfissional>> MinhasQuinzenas(CancellationToken cancellationToken) =>
        Ok(await _quinzenas.ListarMinhasAsync(cancellationToken));

    /// <summary>
    /// Vales e consumo do profissional logado (só leitura). Sem vínculo: os do próprio usuário, se ele recebe por quinzena ou
    /// já teve lançamento; senão 204.
    /// </summary>
    [HttpGet("comissoes/minhas/saldo")]
    public async Task<ActionResult<SaldoDevedor>> MeuSaldo(CancellationToken cancellationToken) =>
        await _saldos.DetalharMeuAsync(cancellationToken) is { } saldo ? Ok(saldo) : NoContent();

    [HttpGet("comissoes/resumo")]
    [Authorize(Policy = nameof(Permissao.VerComissoesDeTodos))]
    public async Task<ActionResult<IReadOnlyList<ResumoComissaoProfissional>>> Resumo(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, CancellationToken cancellationToken)
    {
        if (ValidarFiltro(de, ate, 1, 1) is { } erro)
            return erro;

        return Ok(await _servico.ResumirPorProfissionalAsync(de, ate, cancellationToken));
    }

    /// <summary>Comissão de produto de quem vende sem cadastro de profissional (ex.: Recepcionista).</summary>
    [HttpGet("comissoes/resumo/vendedores")]
    [Authorize(Policy = nameof(Permissao.VerComissoesDeTodos))]
    public async Task<ActionResult<IReadOnlyList<ResumoComissaoVendedor>>> ResumoVendedores(
        [FromQuery] DateOnly de, [FromQuery] DateOnly ate, CancellationToken cancellationToken)
    {
        if (ValidarFiltro(de, ate, 1, 1) is { } erro)
            return erro;

        return Ok(await _servico.ResumirVendedoresSemProfissionalAsync(de, ate, cancellationToken));
    }

    [HttpGet("usuarios/{id:guid}/comissao-produto")]
    public async Task<ActionResult<PercentualComissaoProdutoUsuario>> ObterPercentualProdutoDoUsuario(Guid id, CancellationToken cancellationToken)
    {
        if (!TemPermissao(Permissao.GerenciarComissoes) && !TemPermissao(Permissao.VerComissoesDeTodos))
            return Forbid();

        var percentual = await _servico.ObterPercentualProdutoDoUsuarioAsync(id, cancellationToken);
        return percentual is null ? NotFound() : Ok(percentual);
    }

    [HttpPut("usuarios/{id:guid}/comissao-produto")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> DefinirPercentualProdutoDoUsuario(Guid id, DefinirPercentualProduto dados, CancellationToken cancellationToken)
    {
        try
        {
            return await _servico.DefinirPercentualProdutoDoUsuarioAsync(id, dados, cancellationToken) ? NoContent() : NotFound();
        }
        catch (ArgumentException excecao)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: MensagemSemParametro(excecao));
        }
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
    public async Task<ActionResult<ConfiguracaoComissao>> ObterConfiguracao(Guid id, CancellationToken cancellationToken)
    {
        if (!TemPermissao(Permissao.GerenciarComissoes) && !TemPermissao(Permissao.VerComissoesDeTodos))
            return Forbid();

        var configuracao = await _servico.ObterConfiguracaoAsync(id, cancellationToken);
        return configuracao is null ? NotFound() : Ok(configuracao);
    }

    [HttpPut("profissionais/{id:guid}/comissao")]
    [Authorize(Policy = nameof(Permissao.GerenciarComissoes))]
    public async Task<IActionResult> DefinirConfiguracao(Guid id, ConfiguracaoComissao dados, CancellationToken cancellationToken)
    {
        try
        {
            return await _servico.DefinirConfiguracaoAsync(id, dados, cancellationToken) ? NoContent() : NotFound();
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
