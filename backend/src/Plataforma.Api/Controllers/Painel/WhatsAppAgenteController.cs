using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Conexão do WhatsApp do negócio e liga/desliga do agente (Fase 1). Sempre no negócio do token — nenhum parâmetro
/// de negócio. A tela vem numa fase posterior.
/// </summary>
[ApiController]
[Route("painel/whatsapp")]
[Authorize(Policy = nameof(Permissao.GerenciarConfiguracoesDoNegocio))]
public sealed class WhatsAppAgenteController : ControllerBase
{
    private readonly IGerenciadorInstanciaWhatsApp _gerenciador;

    public WhatsAppAgenteController(IGerenciadorInstanciaWhatsApp gerenciador) => _gerenciador = gerenciador;

    public sealed record DefinirAgenteRequisicao(bool? Ativo);

    /// <summary>Cria a instância (se preciso) e devolve o QR code (data URI) para ler no WhatsApp. Evolution fora do ar → 502.</summary>
    [HttpPost("conectar")]
    public async Task<ActionResult<ConexaoWhatsApp>> Conectar(CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _gerenciador.ConectarAsync(cancellationToken));
        }
        catch (FalhaEvolutionException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { mensagem = "Não foi possível falar com o WhatsApp agora. Tente de novo em instantes." });
        }
    }

    /// <summary>404 se o negócio ainda não conectou.</summary>
    [HttpGet("status")]
    public async Task<ActionResult<StatusWhatsApp>> Status(CancellationToken cancellationToken)
    {
        var status = await _gerenciador.ObterStatusAsync(cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    [HttpPut("agente")]
    public async Task<IActionResult> DefinirAgente(DefinirAgenteRequisicao dados, CancellationToken cancellationToken)
    {
        if (dados.Ativo is null)
            return BadRequest(new { mensagem = "Informe se o agente fica ativo." });

        return await _gerenciador.DefinirAgenteAsync(dados.Ativo.Value, cancellationToken) switch
        {
            ResultadoDefinirAgente.Ok => NoContent(),
            ResultadoDefinirAgente.NaoContratado => Conflict(new ProblemDetails
            {
                Title = "O serviço de atendimento automático não está contratado para este negócio.",
                Extensions = { ["codigo"] = CodigoAgenteIaNaoContratado },
            }),
            _ => NotFound(),
        };
    }

    public const string CodigoAgenteIaNaoContratado = "agente_ia_nao_contratado";

    /// <summary>Situação do serviço de IA do negócio (contratado e origem). Responde 200 mesmo sem instância conectada.</summary>
    [HttpGet("servico-ia")]
    public async Task<ActionResult<ServicoAgenteIaPainel>> ServicoIa(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ObterServicoIaAsync(cancellationToken));
}
