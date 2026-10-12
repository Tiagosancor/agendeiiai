using Plataforma.Aplicacao.Cadastros;
using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Profissionais;

public interface IGerenciadorProfissionais
{
    Task<IReadOnlyList<ProfissionalResumo>> ListarAsync(CancellationToken cancellationToken = default);

    Task<ProfissionalDetalhe?> ObterAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Com <c>Acesso</c>, cria também o usuário (perfil Profissional) já vinculado, na mesma transação; com
    /// <c>UsuarioId</c>, vincula um usuário que já existe (o caminho "Novo usuário, perfil Profissional").
    /// Lança <c>EmailJaCadastradoException</c> e <see cref="OperacaoCadastroBloqueadaException"/>.
    /// </summary>
    Task<Guid> CriarAsync(CriarProfissional dados, CancellationToken cancellationToken = default);

    /// <summary>"Dar acesso ao sistema" a um profissional que ainda não tem usuário (seção 7). Devolve o id do usuário.</summary>
    Task<Guid?> DarAcessoAsync(Guid profissionalId, AcessoProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> AtualizarDadosAsync(Guid profissionalId, AtualizarProfissional dados, CancellationToken cancellationToken = default);

    Task<bool> DesativarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<bool> AtivarAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    Task<string?> RevelarCpfAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia (ou troca) a foto: reprocessada em WebP, sem metadados, até <c>LimitesImagem.LadoMaximoAvatar</c>, guardada em
    /// <c>IArmazenamentoArquivos</c>. Devolve a URL pública; nulo se o profissional não existe neste negócio. A foto anterior
    /// enviada é apagada depois de gravar a nova. Lança <c>ImagemInvalidaException</c>.
    /// </summary>
    Task<string?> DefinirFotoAsync(Guid profissionalId, byte[] conteudo, CancellationToken cancellationToken = default);

    /// <summary>Fica sem foto: apaga o arquivo enviado (se houver) e esquece a URL externa antiga, sem tentar apagá-la.</summary>
    Task<bool> RemoverFotoAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Lista os agendamentos futuros que precisam ser transferidos ou cancelados antes (seção 7).</summary>
    Task<PreviaExclusao?> ObterPreviaExclusaoAsync(Guid profissionalId, CancellationToken cancellationToken = default);

    /// <summary>Bloqueada enquanto houver agendamento futuro — transferir ou cancelar cada um antes.</summary>
    Task<ResultadoExclusao> ExcluirAsync(Guid profissionalId, CancellationToken cancellationToken = default);
}

/// <summary>
/// <c>FotoUrl</c> = a foto efetiva: a enviada pelo painel (relativa à API, <c>/arquivos/{chave}</c>) ou, sem ela, a URL externa
/// antiga. <c>CorAgenda</c> nula = o painel usa a cor automática.
/// </summary>
public sealed record ProfissionalResumo(Guid Id, string Nome, bool Ativo, string? FotoUrl, string? Funcao, string? CorAgenda = null);

/// <summary><c>Acesso</c> = o usuário vinculado (quem faz login como este profissional); nulo = só um nome na agenda.</summary>
public sealed record ProfissionalDetalhe(
    Guid Id, string Nome, string? Telefone, string? Email, bool Ativo, string? FotoUrl, string? CpfMascarado, string? Funcao,
    Endereco? Endereco = null, AcessoVinculado? Acesso = null, string? CorAgenda = null);

public sealed record AcessoVinculado(Guid UsuarioId, string Email, bool Ativo);

public sealed record CriarProfissional(
    string Nome, string? Telefone = null, string? Email = null, string? Cpf = null, string? Funcao = null,
    AcessoProfissional? Acesso = null, Guid? UsuarioId = null, string? CorAgenda = null);

/// <summary>
/// Login do profissional (seção 7): senha definida agora ou convite por e-mail para ele criar a própria
/// (<c>EnviarConvite</c>, e aí a senha é ignorada).
/// </summary>
public sealed record AcessoProfissional(string Email, string? Senha, bool EnviarConvite);

/// <summary>
/// Edição da ficha (seção 7). <c>Cpf</c>/<c>Endereco</c> nulos ficam como estão; <c>FotoUrl</c> e <c>CorAgenda</c>
/// nulas ficam como estão, vazias apagam (a cor volta à automática). Cor só no formato <c>#RRGGBB</c>.
/// </summary>
public sealed record AtualizarProfissional(
    string Nome, string? Telefone, string? Email, string? Funcao = null, string? Cpf = null, Endereco? Endereco = null,
    string? FotoUrl = null, string? CorAgenda = null);
