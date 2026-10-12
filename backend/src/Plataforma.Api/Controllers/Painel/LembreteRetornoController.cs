using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Negocios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Lembrete de retorno por WhatsApp: configuração do negócio, relatório e opt-out do cliente. Tudo com "configurar o negócio"
/// (inclusive o opt-out, por decisão do pedido) e sempre no negócio do token.
/// </summary>
[ApiController]
[Route("painel")]
[Authorize(Policy = nameof(Permissao.GerenciarConfiguracoesDoNegocio))]
public sealed class LembreteRetornoController : ControllerBase
{
    private static readonly string[] FormatosHorario = ["HH:mm", "HH:mm:ss"];

    private readonly IServicoLembreteRetorno _servico;

    public LembreteRetornoController(IServicoLembreteRetorno servico) => _servico = servico;

    [HttpGet("configuracoes/lembrete-retorno")]
    public async Task<ActionResult<ConfiguracaoLembreteRetorno>> Obter(CancellationToken cancellationToken)
    {
        var configuracao = await _servico.ObterConfiguracaoAsync(cancellationToken);
        return configuracao is null ? NotFound() : Ok(configuracao);
    }

    /// <summary>Inválido → 400 com <c>errors</c> por campo (<c>ativo</c>, <c>dias</c>, <c>horario</c>, <c>mensagem</c>).</summary>
    [HttpPut("configuracoes/lembrete-retorno")]
    public async Task<IActionResult> Atualizar(AtualizarLembreteRetornoRequisicao dados, CancellationToken cancellationToken)
    {
        TimeOnly? horario = TimeOnly.TryParseExact(
            dados.Horario?.Trim(), FormatosHorario, CultureInfo.InvariantCulture, DateTimeStyles.None, out var lido) ? lido : null;

        var erros = new Dictionary<string, string[]>(LembreteRetorno.Validar(dados.Dias, horario, dados.Mensagem));
        if (dados.Ativo is null)
            erros["ativo"] = ["Informe se o lembrete fica ativo."];

        if (erros.Count > 0)
            return ErrosDeValidacao(erros);

        return await _servico.AtualizarConfiguracaoAsync(dados.Ativo!.Value, dados.Dias!.Value, horario!.Value, dados.Mensagem!, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    /// <summary>Envios com sucesso nos últimos <paramref name="dias"/> dias (1 a 365, padrão 30) e quantos viraram agendamento em até 7 dias.</summary>
    [HttpGet("lembrete-retorno/relatorio")]
    public async Task<IActionResult> Relatorio([FromQuery] int dias = 30, CancellationToken cancellationToken = default)
    {
        if (dias is < 1 or > 365)
            return ErrosDeValidacao(new Dictionary<string, string[]> { ["dias"] = ["Informe um período entre 1 e 365 dias."] });

        return Ok(await _servico.ObterRelatorioAsync(dias, cancellationToken));
    }

    [HttpPatch("clientes/{id:guid}/aceita-lembrete-retorno")]
    public async Task<IActionResult> DefinirAceite(Guid id, AceiteLembreteRetornoRequisicao dados, CancellationToken cancellationToken)
    {
        if (dados.Aceita is null)
            return ErrosDeValidacao(new Dictionary<string, string[]> { ["aceita"] = ["Informe se o cliente aceita o lembrete."] });

        return await _servico.DefinirAceiteClienteAsync(id, dados.Aceita.Value, cancellationToken) ? NoContent() : NotFound();
    }

    private ActionResult ErrosDeValidacao(IReadOnlyDictionary<string, string[]> erros)
    {
        var estado = new ModelStateDictionary();
        foreach (var (campo, mensagens) in erros)
            foreach (var mensagem in mensagens)
                estado.AddModelError(campo, mensagem);

        return ValidationProblem(estado);
    }
}

/// <param name="Horario">"HH:mm" (também aceita "HH:mm:ss"), no fuso do negócio.</param>
public sealed record AtualizarLembreteRetornoRequisicao(bool? Ativo, int? Dias, string? Horario, string? Mensagem);

public sealed record AceiteLembreteRetornoRequisicao(bool? Aceita);
