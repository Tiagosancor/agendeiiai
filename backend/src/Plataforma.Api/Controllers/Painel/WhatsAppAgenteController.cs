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

    private readonly IConsultaUsoIaWhatsApp _uso;

    public WhatsAppAgenteController(IGerenciadorInstanciaWhatsApp gerenciador, IConsultaUsoIaWhatsApp uso)
    {
        _gerenciador = gerenciador;
        _uso = uso;
    }

    public sealed record DefinirAgenteRequisicao(bool? Ativo);

    public sealed record DefinirMensagemTransferenciaRequisicao(string? Mensagem);

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

    /// <summary>Texto que o negócio acrescenta ao aviso de transferência para atendimento humano. <c>null</c>/vazio tira. Até 300 caracteres.</summary>
    [HttpPut("mensagem-transferencia")]
    public async Task<IActionResult> DefinirMensagemTransferencia(DefinirMensagemTransferenciaRequisicao dados, CancellationToken cancellationToken) =>
        await _gerenciador.DefinirMensagemTransferenciaAsync(dados.Mensagem, cancellationToken) switch
        {
            ResultadoMensagemTransferencia.Ok => NoContent(),
            ResultadoMensagemTransferencia.Invalida => BadRequest(new ProblemDetails
            {
                Title = "A mensagem de transferência tem no máximo 300 caracteres e não pode ter caracteres de controle.",
                Extensions = { ["codigo"] = "mensagem_invalida" },
            }),
            _ => NotFound(),
        };

    /// <summary>Uso de IA do mês (UTC, o mesmo recorte da cota). <c>mes</c> = <c>AAAA-MM</c>; sem ele, o mês corrente.</summary>
    [HttpGet("uso")]
    public async Task<ActionResult<UsoIaMes>> Uso([FromQuery] string? mes, CancellationToken cancellationToken)
    {
        int ano, numero;
        if (string.IsNullOrWhiteSpace(mes))
        {
            var agora = DateTimeOffset.UtcNow;
            ano = agora.Year;
            numero = agora.Month;
        }
        else if (DateTime.TryParseExact(mes.Trim(), "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var data)
            && data.Year >= 2020)
        {
            ano = data.Year;
            numero = data.Month;
        }
        else
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Informe o mês no formato AAAA-MM.",
                Extensions = { ["codigo"] = "mes_invalido" },
            });
        }

        return Ok(await _uso.ObterAsync(ano, numero, cancellationToken));
    }

    /// <summary>Situação do serviço de IA do negócio (contratado e origem). Responde 200 mesmo sem instância conectada.</summary>
    [HttpGet("servico-ia")]
    public async Task<ActionResult<ServicoAgenteIaPainel>> ServicoIa(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ObterServicoIaAsync(cancellationToken));
}
