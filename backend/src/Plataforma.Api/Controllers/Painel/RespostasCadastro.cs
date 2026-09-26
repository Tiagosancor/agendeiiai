using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Cadastros;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>Respostas comuns de exclusão/edição de cadastros (seção 7) — o front usa o <c>codigo</c>.</summary>
internal static class RespostasCadastro
{
    public static IActionResult Exclusao(ControllerBase controller, ResultadoExclusao resultado) => resultado.Tipo switch
    {
        TipoResultadoExclusao.NaoEncontrado => controller.NotFound(),
        TipoResultadoExclusao.Bloqueado => Bloqueado(controller, resultado.Motivo!),
        _ => controller.Ok(new RespostaExclusao(resultado.Tipo == TipoResultadoExclusao.ExcluidoLogicamente)),
    };

    public static ObjectResult Bloqueado(ControllerBase controller, string motivo) => controller.Conflict(new ProblemDetails
    {
        Title = "Operação não permitida.",
        Detail = motivo,
        Extensions = { ["codigo"] = "operacao_bloqueada" },
    });
}

/// <param name="MantidoNoHistorico">True = excluído logicamente (tinha histórico); false = apagado de fato.</param>
public sealed record RespostaExclusao(bool MantidoNoHistorico);
