using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Api.Assinaturas;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Escolher ou cadastrar o cliente na tela de agendamento. Fica sob "gerenciar agenda" (a Recepcionista padrão não tem
/// "gerenciar clientes"), no mesmo grupo de <c>/painel/agenda/profissionais</c> e <c>/servicos</c>. Só nome, telefone e e-mail.
/// </summary>
[ApiController]
[Route("painel/agenda/clientes")]
[Authorize(Policy = nameof(Permissao.GerenciarAgenda))]
public sealed class ClientesAgendaController : ControllerBase
{
    private readonly IClientesDaAgenda _clientes;

    public ClientesAgendaController(IClientesDaAgenda clientes)
    {
        _clientes = clientes;
    }

    [HttpGet("busca")]
    [PermitirComAssinaturaSuspensa]
    public async Task<ActionResult<IReadOnlyList<ClienteBusca>>> Buscar(
        [FromQuery] string? termo, [FromQuery] int limite = IClientesDaAgenda.LimitePadraoBusca, CancellationToken cancellationToken = default) =>
        Ok(await _clientes.BuscarAsync(termo, limite, cancellationToken));

    [HttpPost("rapido")]
    public async Task<IActionResult> CadastrarRapido(CadastroRapidoCliente dados, CancellationToken cancellationToken)
    {
        var resultado = await _clientes.CadastrarRapidoAsync(dados, cancellationToken);

        if (resultado.Erro is not null)
            return BadRequest(new { title = resultado.Erro });

        if (resultado.Existente is { } existente)
            return Conflict(new
            {
                title = "Já existe um cliente com este telefone.",
                codigo = "telefone_ja_cadastrado",
                existente.Id, existente.Nome, existente.Telefone, existente.Email,
            });

        var criado = resultado.Criado!;
        return StatusCode(StatusCodes.Status201Created, criado);
    }
}
