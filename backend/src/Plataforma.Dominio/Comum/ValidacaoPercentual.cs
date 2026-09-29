namespace Plataforma.Dominio.Comum;

/// <summary>Percentual de comissão (seção 7): 0 a 100, com até duas casas decimais.</summary>
public static class ValidacaoPercentual
{
    public static decimal Comissao(decimal percentual, string nomeParametro)
    {
        if (percentual is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nomeParametro, "A comissão precisa estar entre 0 e 100%.");

        if (decimal.Round(percentual, 2) != percentual)
            throw new ArgumentException("A comissão aceita no máximo duas casas decimais.", nomeParametro);

        return percentual;
    }
}
