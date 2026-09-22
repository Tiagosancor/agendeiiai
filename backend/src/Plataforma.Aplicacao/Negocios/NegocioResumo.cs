namespace Plataforma.Aplicacao.Negocios;

/// <summary>
/// Recorte público de <c>Negocio</c> — só o que pode ser exposto por endpoints
/// anônimos (seção 8.1.3: nenhum dado pessoal de terceiros, e aqui nem se aplica
/// porque Negocio não é dado pessoal, mas mantemos o recorte mínimo mesmo assim).
/// </summary>
public sealed record NegocioResumo(Guid Id, string Slug, string NomeExibido, string Tipo, string Fuso);
