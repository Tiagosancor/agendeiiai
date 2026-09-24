using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Assinaturas;
using Plataforma.Dominio.Usuarios;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Assinaturas;

/// <summary>
/// Move teste → carência → suspensa e envia os avisos de 7/3/1 dias (seção 7). Roda de hora
/// em hora (varredura, como os lembretes): se a API hibernar, a próxima execução aplica tudo
/// o que o tempo decidiu enquanto isso, e cada aviso sai no máximo uma vez por prazo.
/// </summary>
public sealed class JobAtualizarAssinaturas
{
    private readonly PlataformaDbContext _dbContext;
    private readonly INotificador _notificador;
    private readonly OpcoesMarca _opcoesMarca;

    public JobAtualizarAssinaturas(PlataformaDbContext dbContext, INotificador notificador, IOptions<OpcoesMarca> opcoesMarca)
    {
        _dbContext = dbContext;
        _notificador = notificador;
        _opcoesMarca = opcoesMarca.Value;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;

        // Job sem tenant: varre todos os negócios explicitamente.
        var assinaturas = await _dbContext.Assinaturas.IgnoreQueryFilters()
            .Where(a => a.Estado == EstadoAssinatura.EmTeste || a.Estado == EstadoAssinatura.Ativa || a.Estado == EstadoAssinatura.Atrasada)
            .ToListAsync(cancellationToken);

        foreach (var assinatura in assinaturas)
            ServicoAssinatura.AtualizarPorTempo(assinatura, agora);

        await _dbContext.SaveChangesAsync(cancellationToken);

        var comAviso = assinaturas.Where(a => a.AvisoPendente(agora) is not null).ToList();
        if (comAviso.Count == 0)
            return;

        var negocioIds = comAviso.Select(a => a.NegocioId).ToList();
        var planoIds = comAviso.Select(a => a.PlanoId).Distinct().ToList();

        var negocios = await _dbContext.Negocios.AsNoTracking()
            .Where(n => negocioIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, n => n.NomeExibido, cancellationToken);
        var planos = await _dbContext.Planos.AsNoTracking()
            .Where(p => planoIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Nome, cancellationToken);
        var administradores = await _dbContext.Usuarios.IgnoreQueryFilters().AsNoTracking()
            .Where(u => negocioIds.Contains(u.NegocioId) && u.Perfil == Perfil.Administrador && u.Ativo)
            .Select(u => new { u.NegocioId, u.Email })
            .ToListAsync(cancellationToken);

        var linkAssinatura = ConstrutorUrlPublica.ConstruirPainel(_opcoesMarca, "/painel/assinatura");

        foreach (var assinatura in comAviso)
        {
            var dias = assinatura.AvisoPendente(agora)!.Value;
            var emails = administradores.Where(a => a.NegocioId == assinatura.NegocioId).Select(a => a.Email).ToList();

            if (emails.Count > 0)
            {
                var prazo = assinatura.PrazoAtual!.Value;
                var diasRestantes = (int)Math.Ceiling((prazo - agora).TotalDays);

                await _notificador.EnviarAvisoAssinaturaAsync(new DadosAvisoAssinatura(
                    emails, negocios.GetValueOrDefault(assinatura.NegocioId, ""), assinatura.Estado == EstadoAssinatura.EmTeste,
                    diasRestantes, prazo, planos.GetValueOrDefault(assinatura.PlanoId, ""),
                    assinatura.ValorDoPeriodo, linkAssinatura), cancellationToken);
            }

            assinatura.MarcarAvisoEnviado(dias);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
