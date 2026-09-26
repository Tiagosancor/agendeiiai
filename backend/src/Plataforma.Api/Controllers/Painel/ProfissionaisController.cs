using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Cadastros;
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
    private readonly IServicoAgendamentos _servicoAgendamentos;

    public ProfissionaisController(IGerenciadorProfissionais gerenciador, IServicoAgendamentos servicoAgendamentos)
    {
        _gerenciador = gerenciador;
        _servicoAgendamentos = servicoAgendamentos;
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

    /// <summary>Corrigir a ficha exige também <see cref="Permissao.EditarCadastros"/> (seção 7).</summary>
    [HttpPut("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.EditarCadastros))]
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

    /// <summary>O que vai acontecer se excluir, com os agendamentos futuros a resolver antes (seção 7).</summary>
    [HttpGet("{id:guid}/exclusao")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<ActionResult<PreviaExclusao>> PreviaExclusao(Guid id, CancellationToken cancellationToken)
    {
        var previa = await _gerenciador.ObterPreviaExclusaoAsync(id, cancellationToken);
        return previa is null ? NotFound() : Ok(previa);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<IActionResult> Excluir(Guid id, CancellationToken cancellationToken) =>
        RespostasCadastro.Exclusao(this, await _gerenciador.ExcluirAsync(id, cancellationToken));

    /// <summary>Passa um agendamento futuro do profissional a excluir para outro (mesmo horário).</summary>
    [HttpPost("{id:guid}/agendamentos-futuros/{agendamentoId:guid}/transferir")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<IActionResult> TransferirAgendamento(
        Guid id, Guid agendamentoId, TransferirAgendamentoRequisicao dados, CancellationToken cancellationToken)
    {
        if (!await EhAgendamentoFuturoDoProfissionalAsync(id, agendamentoId, cancellationToken))
            return NotFound();

        var resultado = await _servicoAgendamentos.TransferirAsync(agendamentoId, dados.NovoProfissionalId, cancellationToken);
        if (resultado.Sucesso)
            return NoContent();

        return resultado.Conflito
            ? RespostasCadastro.Bloqueado(this, resultado.MensagemErro!)
            : BadRequest(new ProblemDetails { Title = "Não foi possível transferir.", Detail = resultado.MensagemErro });
    }

    /// <summary>Cancela um agendamento futuro do profissional a excluir, avisando o cliente.</summary>
    [HttpPost("{id:guid}/agendamentos-futuros/{agendamentoId:guid}/cancelar")]
    [Authorize(Policy = nameof(Permissao.ExcluirCadastros))]
    public async Task<IActionResult> CancelarAgendamento(Guid id, Guid agendamentoId, CancellationToken cancellationToken)
    {
        if (!await EhAgendamentoFuturoDoProfissionalAsync(id, agendamentoId, cancellationToken))
            return NotFound();

        return await _servicoAgendamentos.CancelarAvisandoClienteAsync(agendamentoId, cancellationToken) ? NoContent() : NotFound();
    }

    private async Task<bool> EhAgendamentoFuturoDoProfissionalAsync(Guid profissionalId, Guid agendamentoId, CancellationToken cancellationToken)
    {
        var previa = await _gerenciador.ObterPreviaExclusaoAsync(profissionalId, cancellationToken);
        return previa is not null && previa.Futuros.Any(f => f.Id == agendamentoId);
    }

    [HttpGet("{id:guid}/cpf")]
    public async Task<ActionResult<string>> RevelarCpf(Guid id, CancellationToken cancellationToken)
    {
        var cpf = await _gerenciador.RevelarCpfAsync(id, cancellationToken);
        return cpf is null ? NotFound() : Ok(cpf);
    }
}

public sealed record TransferirAgendamentoRequisicao(Guid NovoProfissionalId);
