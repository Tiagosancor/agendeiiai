using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>
/// <c>Idempotency-Key</c> do painel (<c>Idempotencia__*</c>). Desligar (<c>Habilitada=false</c>) faz a API ignorar o cabeçalho e
/// voltar ao comportamento anterior, sem tocar na tabela.
/// </summary>
public sealed class OpcoesIdempotencia
{
    public const string Secao = "Idempotencia";

    /// <summary>Liga o mecanismo (padrão ligado; a chave continua opcional).</summary>
    public bool Habilitada { get; set; } = true;

    /// <summary>Exigir a chave nos endpoints que a aceitam. Inicialmente <c>false</c>: só vira <c>true</c> depois de o front enviá-la.</summary>
    public bool Obrigatoria { get; set; }

    /// <summary>Quanto tempo a chave vale depois de confirmada (padrão 24 h).</summary>
    [Range(1, 720)]
    public int RetencaoHoras { get; set; } = 24;

    /// <summary>Quanto um duplicado concorrente espera pela primeira requisição antes de receber 409 (padrão 10 s).</summary>
    [Range(1, 25)]
    public int EsperaMaximaSegundos { get; set; } = 10;
}
