using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Api.Assinaturas;
using Plataforma.Aplicacao.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// "Minha conta": qualquer usuário logado (sem policy de permissão), inclusive com a assinatura suspensa — o dono suspenso
/// precisa de um e-mail confirmado para pagar. O usuário vem **só** do <c>sub</c> do token; não há id na rota.
/// </summary>
[ApiController]
[Route("painel/minha-conta")]
[Authorize]
[PermitirComAssinaturaSuspensa]
[TraduzirErrosDeAcesso]
public sealed class MinhaContaController : ControllerBase
{
    private readonly IServicoMinhaConta _servico;

    public MinhaContaController(IServicoMinhaConta servico) => _servico = servico;

    [HttpGet]
    public async Task<ActionResult<MinhaConta>> Obter(CancellationToken cancellationToken)
    {
        var conta = await _servico.ObterAsync(cancellationToken);
        return conta is null ? NotFound() : Ok(conta);
    }

    /// <summary>Troca (ou remove, com nulo) o nome de usuário. Exige a senha atual.</summary>
    [HttpPut("nome-usuario")]
    public async Task<IActionResult> AlterarNomeUsuario(AlterarNomeUsuarioRequisicao dados, CancellationToken cancellationToken)
    {
        await _servico.AlterarNomeUsuarioAsync(dados.NomeUsuario, dados.SenhaAtual, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Pede o código de confirmação do e-mail. **202 sempre** com a senha certa (o e-mail já ser de outra conta, limites e
    /// afins não aparecem na resposta).
    /// </summary>
    [HttpPost("email")]
    public async Task<IActionResult> SolicitarEmail(SolicitarEmailRequisicao dados, CancellationToken cancellationToken)
    {
        await _servico.SolicitarEmailAsync(dados.Email, dados.SenhaAtual, cancellationToken);
        return Accepted();
    }

    [HttpPost("email/confirmar")]
    public async Task<IActionResult> ConfirmarEmail(ConfirmarEmailRequisicao dados, CancellationToken cancellationToken)
    {
        await _servico.ConfirmarEmailAsync(dados.Codigo, cancellationToken);
        return NoContent();
    }

    /// <summary>Troca a senha e derruba todas as sessões — o front entra de novo na hora com a senha nova.</summary>
    [HttpPut("senha")]
    public async Task<IActionResult> AlterarSenha(AlterarPropriaSenhaRequisicao dados, CancellationToken cancellationToken)
    {
        await _servico.AlterarSenhaAsync(dados.SenhaAtual, dados.NovaSenha, cancellationToken);
        return NoContent();
    }
}

public sealed record AlterarNomeUsuarioRequisicao(string? NomeUsuario, [Required] string SenhaAtual);

public sealed record SolicitarEmailRequisicao([Required] string Email, [Required] string SenhaAtual);

public sealed record ConfirmarEmailRequisicao([Required] string Codigo);

public sealed record AlterarPropriaSenhaRequisicao([Required] string SenhaAtual, [Required] string NovaSenha);
