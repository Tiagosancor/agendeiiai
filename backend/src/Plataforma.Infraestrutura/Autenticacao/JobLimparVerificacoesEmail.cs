using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>
/// Retenção das verificações de e-mail de "Minha conta": o e-mail pendente é dado pessoal e não pode ficar guardado depois de
/// vencido, esgotado, usado ou invalidado (quem abandona o fluxo não volta para apagá-lo). Anula o endereço dessas linhas e
/// apaga as vencidas há mais de um dia. Códigos ainda válidos (não vencidos, com tentativas sobrando) são preservados.
/// Varre todos os negócios (<c>IgnoreQueryFilters</c>); registra só quantidades.
/// </summary>
public sealed class JobLimparVerificacoesEmail
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ILogger<JobLimparVerificacoesEmail> _logger;

    public JobLimparVerificacoesEmail(PlataformaDbContext dbContext, ILogger<JobLimparVerificacoesEmail> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<(int Anulados, int Removidas)> ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;

        var anulados = await _dbContext.VerificacoesEmailUsuario.IgnoreQueryFilters()
            .Where(v => v.Email != null
                && (v.UsadoEm != null || v.InvalidadoEm != null || v.ExpiraEm <= agora || v.Tentativas >= v.MaximoTentativas))
            .ExecuteUpdateAsync(s => s.SetProperty(v => v.Email, (string?)null), cancellationToken);

        var limite = agora.AddDays(-1);
        var removidas = await _dbContext.VerificacoesEmailUsuario.IgnoreQueryFilters()
            .Where(v => v.ExpiraEm < limite)
            .ExecuteDeleteAsync(cancellationToken);

        if (anulados > 0 || removidas > 0)
            _logger.LogInformation("Minha conta: {Anulados} e-mail(s) pendente(s) vencido(s) apagado(s), {Removidas} verificação(ões) antiga(s) removida(s).", anulados, removidas);

        return (anulados, removidas);
    }
}
