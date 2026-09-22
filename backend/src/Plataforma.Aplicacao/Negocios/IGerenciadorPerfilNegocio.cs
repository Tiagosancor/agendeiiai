namespace Plataforma.Aplicacao.Negocios;

/// <summary>Perfil do negócio (marca, endereço, horário de funcionamento — seção 5 / Sprint 1). Sempre o negócio do <c>IContextoNegocio</c>.</summary>
public interface IGerenciadorPerfilNegocio
{
    Task<PerfilNegocio?> ObterAsync(CancellationToken cancellationToken = default);

    Task<bool> AtualizarAsync(AtualizarPerfilNegocio dados, CancellationToken cancellationToken = default);
}

public sealed record PerfilNegocio(
    string Slug, string NomeExibido, string Tipo, string? LogoUrl, string? CorPrimaria, string? CorSecundaria,
    string? TituloPagina, string? SubtituloPagina, string? TextoSobre,
    string? Bairro, string? Cidade, string? Rua, string? Numero, string? Cep,
    string? Telefone, string? EmailContato, string? Instagram, string? Facebook, string? WhatsApp,
    bool WhatsAppAtivoParaConfirmacoes, IReadOnlyCollection<HorarioFuncionamentoDiaDto> HorarioFuncionamento);

public sealed record HorarioFuncionamentoDiaDto(int DiaSemana, TimeOnly? Abertura, TimeOnly? Fechamento, bool Fechado);

public sealed record AtualizarPerfilNegocio(
    string NomeExibido, string? LogoUrl, string? CorPrimaria, string? CorSecundaria,
    string? TituloPagina, string? SubtituloPagina, string? TextoSobre,
    string? Bairro, string? Cidade, string? Rua, string? Numero, string? Cep,
    string? Telefone, string? EmailContato, string? Instagram, string? Facebook, string? WhatsApp,
    bool WhatsAppAtivoParaConfirmacoes, IReadOnlyCollection<HorarioFuncionamentoDiaDto> HorarioFuncionamento);
