using Plataforma.Dominio.Comum;

namespace Plataforma.Aplicacao.Abstracoes;

/// <summary>
/// Tokens assinados usados fora do login do painel: o <c>tokenVerificacao</c> do código de
/// confirmação (seção 8.1.1.c, 15 min, vinculado a negócio+telefone) e o token dos links
/// de cancelar/remarcar por e-mail (seção 6.3, vinculado a negócio+agendamento). Ambos são
/// JWT HMAC assinados com o mesmo segredo do painel (<c>Jwt__ChaveSecreta</c>), mas com uma
/// claim de finalidade própria — um token de um tipo nunca é aceito como o outro, e nenhum
/// dos dois é aceito pelo <c>JwtBearer</c> do painel (audiência diferente).
/// </summary>
public interface IServicoTokenPublico
{
    string GerarTokenVerificacao(Guid negocioId, TelefoneE164 telefone);

    /// <summary>Só retorna sucesso se o token for válido, não tiver expirado e bater com o negócio e o telefone informados (seção 8.1.1.d: 401/403 caso contrário).</summary>
    Task<bool> ValidarTokenVerificacaoAsync(string token, Guid negocioId, TelefoneE164 telefone);

    /// <summary>Válido por 30 dias — tempo de sobra até a data do agendamento mais distante permitido, para o link de cancelar/remarcar do e-mail continuar funcionando.</summary>
    string GerarTokenAgendamento(Guid negocioId, Guid agendamentoId);

    /// <summary>Retorna o id do agendamento se o token for válido e pertencer a este negócio; <c>null</c> caso contrário.</summary>
    Task<Guid?> ValidarTokenAgendamentoAsync(string token, Guid negocioId);

    /// <summary>E-mail confirmado por código no cadastro de um negócio novo (seção 6.5) — 30 minutos para terminar o cadastro.</summary>
    string GerarTokenCadastro(string email);

    /// <summary>O e-mail confirmado, ou <c>null</c> se o token for inválido ou tiver expirado.</summary>
    Task<string?> ValidarTokenCadastroAsync(string token);
}
