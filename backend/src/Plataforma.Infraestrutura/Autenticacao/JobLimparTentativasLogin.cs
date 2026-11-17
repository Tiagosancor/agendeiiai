using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Autenticacao;

/// <summary>Apaga os contadores de <c>tentativas_login</c> sem atividade há mais de um dia. Registra só a quantidade.</summary>
public sealed class JobLimparTentativasLogin
{
    private readonly PlataformaDbContext _dbContext;
    private readonly ILogger<JobLimparTentativasLogin> _logger;

    public JobLimparTentativasLogin(PlataformaDbContext dbContext, ILogger<JobLimparTentativasLogin> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<int> ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var limite = DateTimeOffset.UtcNow.AddDays(-1);
        var removidas = await _dbContext.TentativasLogin.Where(t => t.AtualizadoEm < limite).ExecuteDeleteAsync(cancellationToken);

        if (removidas > 0)
            _logger.LogInformation("Login: {Removidas} contador(es) de tentativas sem atividade removido(s).", removidas);

        return removidas;
    }
}
