using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Agendamentos;

public enum TipoAjusteValor
{
    Desconto = 1,
    Acrescimo = 2,
}

public enum ModoAjusteValor
{
    Reais = 1,
    Percentual = 2,
}

/// <summary>
/// Histórico de um ajuste de valor feito durante o atendimento (seção 7): quem, quando, o valor de
/// antes e o de depois, e o motivo. Nunca alterado; a linha do agendamento guarda só o valor atual.
/// </summary>
public class AjusteValorAtendimento : EntidadeBase, IEntidadeDoNegocio
{
    public const int TamanhoMaximoMotivo = 300;

    public Guid NegocioId { get; private set; }

    public Guid AgendamentoId { get; private set; }

    public Guid AgendamentoServicoId { get; private set; }

    public TipoAjusteValor Tipo { get; private set; }

    public ModoAjusteValor Modo { get; private set; }

    /// <summary>O que a pessoa digitou: R$ ou % conforme <see cref="Modo"/>.</summary>
    public decimal ValorInformado { get; private set; }

    public decimal PrecoOriginal { get; private set; }

    public decimal ValorAntes { get; private set; }

    public decimal ValorDepois { get; private set; }

    public string Motivo { get; private set; } = string.Empty;

    public Guid? UsuarioId { get; private set; }

    /// <summary>Correção do Administrador num atendimento já concluído (e não um ajuste durante o atendimento).</summary>
    public bool AposConclusao { get; private set; }

    protected AjusteValorAtendimento()
    {
    }

    /// <summary>Calcula o valor final sobre o preço original da linha. Motivo obrigatório; resultado nunca negativo.</summary>
    internal static AjusteValorAtendimento Calcular(
        Guid negocioId, Guid agendamentoId, AgendamentoServico linha, TipoAjusteValor tipo, ModoAjusteValor modo,
        decimal valorInformado, string motivo, Guid? usuarioId, DateTimeOffset agora)
    {
        var motivoLimpo = motivo?.Trim() ?? string.Empty;
        if (motivoLimpo.Length == 0)
            throw new ArgumentException("O motivo do ajuste é obrigatório.", nameof(motivo));
        if (motivoLimpo.Length > TamanhoMaximoMotivo)
            throw new ArgumentException($"O motivo pode ter até {TamanhoMaximoMotivo} caracteres.", nameof(motivo));
        if (!Enum.IsDefined(tipo) || !Enum.IsDefined(modo))
            throw new ArgumentException("Tipo de ajuste inválido.", nameof(tipo));
        if (valorInformado < 0)
            throw new ArgumentException("Informe um valor positivo; escolha desconto ou acréscimo.", nameof(valorInformado));
        if (modo == ModoAjusteValor.Percentual && tipo == TipoAjusteValor.Desconto && valorInformado > 100)
            throw new ArgumentException("O desconto não pode passar de 100%.", nameof(valorInformado));

        var diferenca = modo == ModoAjusteValor.Reais
            ? valorInformado
            : decimal.Round(linha.Preco * valorInformado / 100m, 2, MidpointRounding.AwayFromZero);
        var depois = decimal.Round(tipo == TipoAjusteValor.Desconto ? linha.Preco - diferenca : linha.Preco + diferenca, 2);

        if (depois < 0)
            throw new ArgumentException("O desconto é maior que o preço do serviço: o valor final não pode ficar negativo.", nameof(valorInformado));

        return new AjusteValorAtendimento
        {
            NegocioId = negocioId,
            AgendamentoId = agendamentoId,
            AgendamentoServicoId = linha.Id,
            Tipo = tipo,
            Modo = modo,
            ValorInformado = valorInformado,
            PrecoOriginal = linha.Preco,
            ValorAntes = linha.ValorCobrado,
            ValorDepois = depois,
            Motivo = motivoLimpo,
            UsuarioId = usuarioId,
            CriadoEm = agora,
        };
    }

    internal void MarcarAposConclusao() => AposConclusao = true;
}
