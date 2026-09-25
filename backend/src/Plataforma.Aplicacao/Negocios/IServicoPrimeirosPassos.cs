namespace Plataforma.Aplicacao.Negocios;

/// <summary>Checklist do primeiro acesso ao painel (seção 6.5). Os passos são deduzidos dos dados, menos "copiar o link".</summary>
public interface IServicoPrimeirosPassos
{
    Task<PrimeirosPassos> ObterAsync(CancellationToken cancellationToken = default);

    Task MarcarLinkCopiadoAsync(CancellationToken cancellationToken = default);

    Task DispensarAsync(CancellationToken cancellationToken = default);
}

public sealed record PrimeirosPassos(
    bool ServicosCadastrados, bool ProfissionaisCadastrados, bool HorariosConfigurados, bool LinkCopiado,
    bool Dispensado, string LinkAgendamento)
{
    /// <summary>Some do painel quando tudo foi feito ou o usuário dispensou.</summary>
    public bool Exibir => !Dispensado && !(ServicosCadastrados && ProfissionaisCadastrados && HorariosConfigurados && LinkCopiado);
}
