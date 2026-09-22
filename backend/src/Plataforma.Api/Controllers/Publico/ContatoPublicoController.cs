using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Contato;

namespace Plataforma.Api.Controllers.Publico;

/// <summary>Formulário "Fale Conosco" da página pública (seção 6.1.6).</summary>
[ApiController]
[Route("publico/contato")]
public sealed class ContatoPublicoController : ControllerBase
{
    private readonly IServicoContato _servico;

    public ContatoPublicoController(IServicoContato servico)
    {
        _servico = servico;
    }

    public sealed record EnviarContatoRequisicao(string Nome, string? Telefone, string? Email, string Mensagem);

    [HttpPost]
    public async Task<IActionResult> Enviar(EnviarContatoRequisicao requisicao, CancellationToken cancellationToken)
    {
        await _servico.EnviarAsync(
            new EnviarMensagemContato(requisicao.Nome, requisicao.Telefone, requisicao.Email, requisicao.Mensagem), cancellationToken);

        return Accepted();
    }
}
