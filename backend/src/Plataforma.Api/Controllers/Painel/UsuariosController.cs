using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Plataforma.Aplicacao.Usuarios;
using Plataforma.Dominio.Usuarios;

namespace Plataforma.Api.Controllers.Painel;

/// <summary>
/// Usuários e permissões (seção 7). Toda a controller exige
/// <see cref="Permissao.GerenciarUsuarios"/> — quem não tem a permissão recebe 403, quem
/// não está autenticado recebe 401 (métrica da Sprint 1: "permissões bloqueiam ações sem acesso").
/// </summary>
[ApiController]
[Route("painel/usuarios")]
[Authorize(Policy = nameof(Permissao.GerenciarUsuarios))]
public sealed class UsuariosController : ControllerBase
{
    private readonly IGerenciadorUsuarios _gerenciador;

    public UsuariosController(IGerenciadorUsuarios gerenciador)
    {
        _gerenciador = gerenciador;
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<UsuarioResumo>>> Listar(CancellationToken cancellationToken) =>
        Ok(await _gerenciador.ListarAsync(cancellationToken));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UsuarioDetalhe>> Obter(Guid id, CancellationToken cancellationToken)
    {
        var usuario = await _gerenciador.ObterAsync(id, cancellationToken);
        return usuario is null ? NotFound() : Ok(usuario);
    }

    [HttpPost]
    public async Task<ActionResult<Guid>> Criar(CriarUsuario dados, CancellationToken cancellationToken)
    {
        try
        {
            var id = await _gerenciador.CriarAsync(dados, cancellationToken);
            return CreatedAtAction(nameof(Obter), new { id }, id);
        }
        catch (EmailJaCadastradoException excecao)
        {
            return Conflict(new ProblemDetails { Title = "E-mail já cadastrado.", Detail = excecao.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> AtualizarDados(Guid id, AtualizarUsuario dados, CancellationToken cancellationToken) =>
        await _gerenciador.AtualizarDadosAsync(id, dados, cancellationToken) ? NoContent() : NotFound();

    [HttpPut("{id:guid}/senha")]
    public async Task<IActionResult> AlterarSenha(Guid id, AlterarSenhaRequisicao dados, CancellationToken cancellationToken) =>
        await _gerenciador.AlterarSenhaAsync(id, dados.NovaSenha, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/permissoes/{permissao}")]
    public async Task<IActionResult> ConcederPermissao(Guid id, Permissao permissao, CancellationToken cancellationToken) =>
        await _gerenciador.ConcederPermissaoAsync(id, permissao, cancellationToken) ? NoContent() : NotFound();

    [HttpDelete("{id:guid}/permissoes/{permissao}")]
    public async Task<IActionResult> RevogarPermissao(Guid id, Permissao permissao, CancellationToken cancellationToken) =>
        await _gerenciador.RevogarPermissaoAsync(id, permissao, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/desativar")]
    public async Task<IActionResult> Desativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.DesativarAsync(id, cancellationToken) ? NoContent() : NotFound();

    [HttpPost("{id:guid}/ativar")]
    public async Task<IActionResult> Ativar(Guid id, CancellationToken cancellationToken) =>
        await _gerenciador.AtivarAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>CPF completo — só quem tem GerenciarUsuarios (seção 8.4: "completo apenas para quem tem permissão").</summary>
    [HttpGet("{id:guid}/cpf")]
    public async Task<ActionResult<string>> RevelarCpf(Guid id, CancellationToken cancellationToken)
    {
        var cpf = await _gerenciador.RevelarCpfAsync(id, cancellationToken);
        return cpf is null ? NotFound() : Ok(cpf);
    }
}

public sealed record AlterarSenhaRequisicao(string NovaSenha);
