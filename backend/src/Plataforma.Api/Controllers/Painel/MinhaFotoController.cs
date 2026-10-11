using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Agendamentos;
using Plataforma.Aplicacao.Profissionais;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// O profissional logado troca ou tira a própria foto sem precisar de "gerenciar profissionais" — rota separada para não
/// afrouxar a policy da ficha. O profissional vem do token (vínculo <c>Usuario.ProfissionalId</c>), nunca da requisição; só
/// a foto muda. Sem vínculo: 404.
/// </summary>
[ApiController]
[Route("painel/minha-foto")]
[Authorize]
public sealed class MinhaFotoController : ControllerBase
{
    private readonly IAgendaDoProfissionalLogado _minha;
    private readonly IGerenciadorProfissionais _gerenciador;

    public MinhaFotoController(IAgendaDoProfissionalLogado minha, IGerenciadorProfissionais gerenciador)
    {
        _minha = minha;
        _gerenciador = gerenciador;
    }

    [HttpPut]
    [RequestSizeLimit(FotoEnviada.LimiteRequisicao)]
    [RequestFormLimits(MultipartBodyLengthLimit = FotoEnviada.LimiteRequisicao)]
    public async Task<IActionResult> Enviar(IFormFile? arquivo, CancellationToken cancellationToken) =>
        await _minha.MeuProfissionalAsync(cancellationToken) is { } profissionalId
            ? await FotoEnviada.EnviarAsync(this, arquivo, conteudo => _gerenciador.DefinirFotoAsync(profissionalId, conteudo, cancellationToken), cancellationToken)
            : SemVinculo();

    [HttpDelete]
    public async Task<IActionResult> Remover(CancellationToken cancellationToken) =>
        await _minha.MeuProfissionalAsync(cancellationToken) is { } profissionalId
            ? await _gerenciador.RemoverFotoAsync(profissionalId, cancellationToken) ? NoContent() : SemVinculo()
            : SemVinculo();

    private NotFoundObjectResult SemVinculo() =>
        NotFound(new ProblemDetails { Title = "Seu usuário não está ligado a um profissional." });
}
