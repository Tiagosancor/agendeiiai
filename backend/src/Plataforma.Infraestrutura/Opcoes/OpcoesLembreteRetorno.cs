using System.ComponentModel.DataAnnotations;

namespace Plataforma.Infraestrutura.Opcoes;

/// <summary>Execução do lembrete de retorno (o que cada negócio configura — dias, horário, mensagem — fica no <c>Negocio</c>).</summary>
public sealed class OpcoesLembreteRetorno
{
    public const string Secao = "LembreteRetorno";

    /// <summary>
    /// Até quanto tempo depois do horário configurado o envio do dia ainda vale (<c>LembreteRetorno__JanelaEnvioMinutos</c>).
    /// Cobre a API hibernando no horário; passou disso (ou virou o dia), aquele dia não é mais enviado.
    /// </summary>
    [Range(15, 720)]
    public int JanelaEnvioMinutos { get; set; } = 180;

    /// <summary>Clientes por lote — o contexto do EF é limpo entre lotes.</summary>
    [Range(1, 1000)]
    public int TamanhoLote { get; set; } = 50;
}
