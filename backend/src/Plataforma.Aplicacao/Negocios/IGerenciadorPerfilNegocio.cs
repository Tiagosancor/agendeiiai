namespace Plataforma.Aplicacao.Negocios;

/// <summary>Perfil do negócio (marca, endereço, horário de funcionamento — seção 5 / Sprint 1). Sempre o negócio do <c>IContextoNegocio</c>.</summary>
public interface IGerenciadorPerfilNegocio
{
    Task<PerfilNegocio?> ObterAsync(CancellationToken cancellationToken = default);

    /// <summary>Lança <see cref="ArgumentException"/> com dado inválido (ex.: cor de fundo fora de #RRGGBB).</summary>
    Task<bool> AtualizarAsync(AtualizarPerfilNegocio dados, CancellationToken cancellationToken = default);

    /// <summary>
    /// Valida, reprocessa e grava a imagem de fundo, apagando a anterior; devolve a URL nova (nulo = negócio não encontrado).
    /// Lança <c>ImagemInvalidaException</c> para arquivo que não é imagem aceita.
    /// </summary>
    Task<string?> DefinirImagemFundoAsync(byte[] conteudo, CancellationToken cancellationToken = default);

    Task<bool> RemoverImagemFundoAsync(CancellationToken cancellationToken = default);
}

public sealed record PerfilNegocio(
    string Slug, string NomeExibido, string Tipo, string? LogoUrl, string? CorPrimaria, string? CorSecundaria,
    string? TituloPagina, string? SubtituloPagina, string? TextoSobre,
    string? Bairro, string? Cidade, string? Rua, string? Numero, string? Cep,
    string? Telefone, string? EmailContato, string? Instagram, string? Facebook, string? WhatsApp,
    bool WhatsAppAtivoParaConfirmacoes, IReadOnlyCollection<HorarioFuncionamentoDiaDto> HorarioFuncionamento,
    bool WhatsAppAvisoProfissional, string? CorFundo, string? ImagemFundoUrl);

public sealed record HorarioFuncionamentoDiaDto(int DiaSemana, TimeOnly? Abertura, TimeOnly? Fechamento, bool Fechado);

public sealed record AtualizarPerfilNegocio(
    string NomeExibido, string? LogoUrl, string? CorPrimaria, string? CorSecundaria,
    string? TituloPagina, string? SubtituloPagina, string? TextoSobre,
    string? Bairro, string? Cidade, string? Rua, string? Numero, string? Cep,
    string? Telefone, string? EmailContato, string? Instagram, string? Facebook, string? WhatsApp,
    bool WhatsAppAtivoParaConfirmacoes, IReadOnlyCollection<HorarioFuncionamentoDiaDto> HorarioFuncionamento,
    // Opcional para quem ainda não manda o campo: nulo mantém o que está gravado.
    bool? WhatsAppAvisoProfissional = null,
    // A imagem de fundo tem rota própria (upload); aqui só a cor. Nulo ou vazio tira a cor.
    string? CorFundo = null);
