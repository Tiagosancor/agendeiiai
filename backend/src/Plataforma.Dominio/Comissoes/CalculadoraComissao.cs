namespace Plataforma.Dominio.Comissoes;

/// <summary>Valor-base (o que foi cobrado pela linha) e comissão de um serviço concluído.</summary>
public sealed record ComissaoDaLinha(decimal ValorBase, decimal Comissao);

/// <summary>
/// Comissão por linha de serviço (seção 7). Tudo em centavos:
/// <list type="number">
/// <item>o desconto do cupom é dividido entre as linhas proporcionalmente ao preço;</item>
/// <item>a comissão do agendamento é o percentual sobre o total cobrado, arredondado em centavos;</item>
/// <item>essa comissão é dividida entre as linhas proporcionalmente ao valor-base.</item>
/// </list>
/// As divisões usam o método do maior resto: a soma das partes é sempre exatamente o todo, então
/// a soma das comissões das linhas nunca passa (nem fica abaixo) da comissão do total.
/// </summary>
public static class CalculadoraComissao
{
    public static IReadOnlyList<ComissaoDaLinha> Calcular(IReadOnlyList<decimal> precos, decimal desconto, decimal percentual)
    {
        if (percentual is < 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(percentual), "A comissão precisa estar entre 0 e 100%.");

        var totalBruto = precos.Sum();
        var descontoEfetivo = Math.Clamp(desconto, 0m, totalBruto);

        var descontos = Ratear(descontoEfetivo, precos);
        var bases = precos.Select((preco, i) => preco - descontos[i]).ToList();

        var comissaoTotal = decimal.Round(bases.Sum() * percentual / 100m, 2, MidpointRounding.AwayFromZero);
        var comissoes = Ratear(comissaoTotal, bases);

        return bases.Select((valorBase, i) => new ComissaoDaLinha(valorBase, comissoes[i])).ToList();
    }

    /// <summary>
    /// Divide <paramref name="total"/> (em reais, com centavos) proporcionalmente aos pesos. Cada
    /// parte recebe o piso em centavos; os centavos que sobram vão para as partes de maior resto
    /// (empate: a primeira). Pesos todos zero: tudo zero.
    /// </summary>
    public static IReadOnlyList<decimal> Ratear(decimal total, IReadOnlyList<decimal> pesos)
    {
        var somaPesos = pesos.Sum();
        if (pesos.Count == 0)
            return [];
        if (somaPesos <= 0 || total <= 0)
            return pesos.Select(_ => 0m).ToList();

        var totalCentavos = decimal.Round(total * 100m, 0, MidpointRounding.AwayFromZero);
        var exatas = pesos.Select(p => totalCentavos * p / somaPesos).ToList();
        var pisos = exatas.Select(decimal.Floor).ToList();
        var sobra = (int)(totalCentavos - pisos.Sum());

        var ordemPorResto = exatas
            .Select((exata, i) => (Indice: i, Resto: exata - pisos[i]))
            .OrderByDescending(x => x.Resto)
            .ThenBy(x => x.Indice)
            .Take(sobra)
            .Select(x => x.Indice);

        foreach (var i in ordemPorResto)
            pisos[i] += 1;

        return pisos.Select(centavos => centavos / 100m).ToList();
    }
}
