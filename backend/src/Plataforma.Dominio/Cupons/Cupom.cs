using Plataforma.Dominio.Comum;

namespace Plataforma.Dominio.Cupons;

/// <summary>Resultado de tentar aplicar um cupom a um total (seção 6.2.4/7) — sempre revalidado no servidor.</summary>
public sealed record ResultadoAplicacaoCupom(bool Sucesso, decimal Desconto = 0m, string? MensagemErro = null)
{
    public static ResultadoAplicacaoCupom ComSucesso(decimal desconto) => new(true, desconto);

    public static ResultadoAplicacaoCupom ComErro(string mensagem) => new(false, MensagemErro: mensagem);
}

/// <summary>
/// Cupom de desconto (seção 7): código, tipo (percentual ou valor fixo), validade, limite
/// de usos e escopo (todos os serviços ou uma seleção). A revalidação no servidor (nunca
/// confiar no que o cliente calculou) é <see cref="TentarAplicar"/>; o incremento de uso é
/// feito de forma atômica pela infraestrutura (seção 8.2, mesmo espírito da reserva de
/// horário: nunca confiar só na checagem prévia).
/// </summary>
public class Cupom : EntidadeBase, IEntidadeDoNegocio
{
    private readonly List<Guid> _servicoIdsEscopo = [];

    public Guid NegocioId { get; private set; }

    /// <summary>Sempre maiúsculo, único por negócio.</summary>
    public string Codigo { get; private set; } = string.Empty;

    public TipoDescontoCupom Tipo { get; private set; }

    public decimal Valor { get; private set; }

    public DateTimeOffset? ValidoAte { get; private set; }

    /// <summary>Nulo = sem limite de usos.</summary>
    public int? LimiteUsos { get; private set; }

    public int UsosAtuais { get; private set; }

    public bool Ativo { get; private set; } = true;

    /// <summary>Vazio = vale para todos os serviços.</summary>
    public IReadOnlyCollection<Guid> ServicoIdsEscopo => _servicoIdsEscopo.AsReadOnly();

    protected Cupom()
    {
    }

    private Cupom(Guid negocioId, string codigo, TipoDescontoCupom tipo, decimal valor)
    {
        NegocioId = negocioId;
        Codigo = codigo;
        Tipo = tipo;
        Valor = valor;
    }

    public static Cupom Criar(
        Guid negocioId, string codigo, TipoDescontoCupom tipo, decimal valor,
        DateTimeOffset? validoAte = null, int? limiteUsos = null, IEnumerable<Guid>? servicoIdsEscopo = null)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new ArgumentException("O código é obrigatório.", nameof(codigo));

        if (valor <= 0)
            throw new ArgumentException("O valor do desconto precisa ser maior que zero.", nameof(valor));

        if (tipo == TipoDescontoCupom.Percentual && valor > 100)
            throw new ArgumentException("Um desconto percentual não pode passar de 100.", nameof(valor));

        if (limiteUsos is <= 0)
            throw new ArgumentException("O limite de usos, se informado, precisa ser maior que zero.", nameof(limiteUsos));

        var cupom = new Cupom(negocioId, codigo.Trim().ToUpperInvariant(), tipo, valor)
        {
            ValidoAte = validoAte,
            LimiteUsos = limiteUsos,
        };

        if (servicoIdsEscopo is not null)
            cupom._servicoIdsEscopo.AddRange(servicoIdsEscopo.Distinct());

        return cupom;
    }

    /// <summary>
    /// Revalida tudo no servidor: ativo, dentro da validade, dentro do limite de usos e,
    /// se houver escopo, que pelo menos um serviço do agendamento esteja nele. O desconto
    /// percentual incide sobre o total; o valor fixo nunca deixa o total negativo.
    /// </summary>
    public ResultadoAplicacaoCupom TentarAplicar(decimal totalServicos, IReadOnlyCollection<Guid> servicoIdsDoAgendamento, DateTimeOffset agora)
    {
        if (!Ativo)
            return ResultadoAplicacaoCupom.ComErro("Cupom inválido.");

        if (ValidoAte is not null && agora > ValidoAte)
            return ResultadoAplicacaoCupom.ComErro("Cupom expirado.");

        if (LimiteUsos is not null && UsosAtuais >= LimiteUsos)
            return ResultadoAplicacaoCupom.ComErro("Cupom esgotado.");

        if (_servicoIdsEscopo.Count > 0 && !servicoIdsDoAgendamento.Any(_servicoIdsEscopo.Contains))
            return ResultadoAplicacaoCupom.ComErro("Cupom não válido para os serviços escolhidos.");

        var desconto = Tipo == TipoDescontoCupom.Percentual
            ? Math.Round(totalServicos * Valor / 100m, 2)
            : Valor;

        return ResultadoAplicacaoCupom.ComSucesso(Math.Min(desconto, totalServicos));
    }

    public void AtualizarCondicoes(DateTimeOffset? validoAte, int? limiteUsos, IEnumerable<Guid>? servicoIdsEscopo)
    {
        if (limiteUsos is <= 0)
            throw new ArgumentException("O limite de usos, se informado, precisa ser maior que zero.", nameof(limiteUsos));

        ValidoAte = validoAte;
        LimiteUsos = limiteUsos;

        _servicoIdsEscopo.Clear();
        if (servicoIdsEscopo is not null)
            _servicoIdsEscopo.AddRange(servicoIdsEscopo.Distinct());
    }

    public void RegistrarUso() => UsosAtuais++;

    public void Desativar() => Ativo = false;

    public void Ativar() => Ativo = true;
}
