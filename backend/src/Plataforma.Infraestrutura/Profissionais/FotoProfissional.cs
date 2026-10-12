using Plataforma.Aplicacao.Arquivos;

namespace Plataforma.Infraestrutura.Profissionais;

/// <summary>
/// A foto que a tela mostra: a enviada pelo painel (servida em <c>/arquivos/{chave}</c>) prevalece; sem ela, a URL externa
/// antiga da ficha; sem nenhuma, nula. Toda resposta que leva foto de profissional passa por aqui — a chave nunca vai sozinha.
/// </summary>
internal static class FotoProfissional
{
    public static string? UrlEfetiva(IArmazenamentoArquivos arquivos, string? fotoArquivo, string? fotoUrl) =>
        fotoArquivo is not null ? arquivos.UrlPublica(fotoArquivo) : fotoUrl;
}
