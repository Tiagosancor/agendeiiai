using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Aplicacao.Profissionais;
using Plataforma.Aplicacao.Servicos;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Atendimento sem agendamento — o encaixe do balcão (seção 7). Tudo o que o formulário rápido
/// precisa fica aqui, sob "lançar atendimento sem agendamento": quem recebe só essa permissão (um
/// Profissional, por exemplo) não precisa de "gerenciar clientes/profissionais/serviços".
/// </summary>
[ApiController]
[Route("painel/encaixes")]
[Authorize(Policy = nameof(Permissao.LancarAtendimentoSemAgendamento))]
public sealed class EncaixesController : ControllerBase
{
    private const int LimiteBusca = 10;

    private readonly IServicoAgendamentos _servicoAgendamentos;
    private readonly IBuscaClientes _clientes;
    private readonly IGerenciadorProfissionais _profissionais;
    private readonly IGerenciadorServicos _servicos;

    public EncaixesController(
        IServicoAgendamentos servicoAgendamentos, IBuscaClientes clientes, IGerenciadorProfissionais profissionais,
        IGerenciadorServicos servicos)
    {
        _servicoAgendamentos = servicoAgendamentos;
        _clientes = clientes;
        _profissionais = profissionais;
        _servicos = servicos;
    }

    [HttpGet("opcoes")]
    public async Task<ActionResult<OpcoesEncaixe>> Opcoes(CancellationToken cancellationToken)
    {
        var profissionais = (await _profissionais.ListarAsync(cancellationToken)).Where(p => p.Ativo).ToList();
        var servicos = (await _servicos.ListarAsync(cancellationToken)).Where(s => s.Ativo).ToList();
        return Ok(new OpcoesEncaixe(profissionais, servicos));
    }

    [HttpGet("clientes")]
    public async Task<ActionResult<IReadOnlyList<ClienteResumo>>> BuscarClientes([FromQuery] string? busca, CancellationToken cancellationToken) =>
        Ok(await _clientes.BuscarAsync(busca ?? string.Empty, LimiteBusca, cancellationToken));

    [HttpPost]
    public async Task<IActionResult> Lancar(LancarEncaixe dados, CancellationToken cancellationToken)
    {
        var (resultado, clienteId) = await _servicoAgendamentos.LancarEncaixeAsync(dados, cancellationToken);

        if (resultado.Sucesso)
            return StatusCode(StatusCodes.Status201Created, new { agendamentoId = resultado.AgendamentoId, clienteId });

        // O cadastro rápido pode já ter sido feito: devolve o cliente para a tela reusar na nova tentativa.
        if (resultado.Conflito)
            return Conflict(new { title = resultado.MensagemErro, proximosHorariosLivres = resultado.ProximosHorariosLivres, clienteId });

        return BadRequest(new { title = resultado.MensagemErro, clienteId });
    }
}

public sealed record OpcoesEncaixe(IReadOnlyList<ProfissionalResumo> Profissionais, IReadOnlyList<ServicoResumo> Servicos);
