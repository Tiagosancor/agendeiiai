namespace Plataforma.Aplicacao.Negocios;

/// <summary>
/// Recorte público de <c>Negocio</c> — só o que pode ser exposto por endpoints
/// anônimos (seção 8.1.3: nenhum dado pessoal de terceiros, e aqui nem se aplica
/// porque Negocio não é dado pessoal, mas mantemos o recorte mínimo mesmo assim).
/// Inclui o perfil completo (marca, endereço, horário) porque é o que a página pública
/// do negócio (seção 6.1) renderiza — nunca inclui e-mail de contato do painel/usuários.
/// </summary>
public sealed record NegocioResumo(
    Guid Id, string Slug, string NomeExibido, string Tipo, string Fuso,
    string? LogoUrl, string? CorPrimaria, string? CorSecundaria,
    string? TituloPagina, string? SubtituloPagina, string? TextoSobre,
    string? Bairro, string? Cidade, string? Rua, string? Numero, string? Cep,
    string? Telefone, string? Instagram, string? Facebook, string? WhatsApp,
    IReadOnlyCollection<HorarioFuncionamentoDiaDto> HorarioFuncionamento);
