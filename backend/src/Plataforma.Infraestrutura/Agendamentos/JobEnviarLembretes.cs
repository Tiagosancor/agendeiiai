using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Notificacoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Agendamentos;

/// <summary>
/// Lembretes 24h/2h antes do agendamento (seção 9), rodando a cada poucos minutos em vez
/// de um job agendado por agendamento — sobrevive à hibernação da API dos planos gratuitos
/// (seção 8.5.6: ao voltar, envia os pendentes ainda válidos e descarta os vencidos) sem
/// depender do agendador de delay do Hangfire ter disparado no momento exato.
/// </summary>
public sealed class JobEnviarLembretes
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly INotificador _notificador;
    private readonly IServicoTokenPublico _servicoToken;
    private readonly OpcoesMarca _opcoesMarca;
    private readonly OpcoesLembretes _opcoesLembretes;

    public JobEnviarLembretes(
        PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, INotificador notificador,
        IServicoTokenPublico servicoToken, IOptions<OpcoesMarca> opcoesMarca, IOptions<OpcoesLembretes> opcoesLembretes)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _notificador = notificador;
        _servicoToken = servicoToken;
        _opcoesMarca = opcoesMarca.Value;
        _opcoesLembretes = opcoesLembretes.Value;
    }

    public async Task ExecutarAsync(CancellationToken cancellationToken = default)
    {
        var agora = DateTimeOffset.UtcNow;
        var antecedencia = TimeSpan.FromMinutes(_opcoesLembretes.AntecedenciaMinutos);
        var intervaloMinimo = TimeSpan.FromMinutes(_opcoesLembretes.IntervaloMinimoAposMarcarMinutos);
        var limite = agora + antecedencia;

        // Descarta os vencidos (seção 8.5.6): agendamento cujo horário já passou nunca mais
        // recebe lembrete, mesmo que o job tenha ficado muito tempo sem rodar.
        await _dbContext.Agendamentos.IgnoreQueryFilters()
            .Where(a => a.Status == StatusAgendamento.Agendado && a.Inicio <= agora && !a.LembreteEnviado)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.LembreteEnviado, true), cancellationToken);

        var candidatos = await _dbContext.Agendamentos.IgnoreQueryFilters()
            .Include(a => a.Servicos)
            .Where(a => a.Status == StatusAgendamento.Agendado && !a.LembreteEnviado && a.Inicio > agora && a.Inicio <= limite)
            .ToListAsync(cancellationToken);

        foreach (var grupo in candidatos.GroupBy(a => a.NegocioId))
        {
            // Infraestrutura de resolução de tenant (seção 8.3.2) — o job varre todos os
            // negócios, então precisa definir o tenant atual explicitamente a cada grupo
            // para o Notificador montar a marca/link certos.
            _contextoNegocio.Definir(grupo.Key);

            var negocio = await _dbContext.Negocios.AsNoTracking().FirstAsync(n => n.Id == grupo.Key, cancellationToken);

            foreach (var agendamento in grupo)
            {
                if (!agendamento.NaJanelaDoLembrete(agora, antecedencia))
                    continue;

                // Marcado em cima da hora: a confirmação acabou de sair — dispensa (e não volta a olhar).
                if (!agendamento.LembreteFazSentido(antecedencia, intervaloMinimo))
                {
                    agendamento.MarcarLembreteEnviado();
                    continue;
                }

                if (agendamento.ClienteId is null)
                    continue;

                var cliente = await _dbContext.Clientes.IgnoreQueryFilters().AsNoTracking()
                    .FirstOrDefaultAsync(c => c.Id == agendamento.ClienteId, cancellationToken);

                if (cliente is null)
                    continue;

                var tokenAgendamento = _servicoToken.GerarTokenAgendamento(negocio.Id, agendamento.Id);
                var link = ConstrutorUrlPublica.Construir(_opcoesMarca, negocio.Slug.Valor, $"/agendamentos/{tokenAgendamento}");

                await _notificador.EnviarLembreteAsync(new DadosNotificacaoAgendamento(
                    cliente.Nome, cliente.Email, cliente.Telefone, agendamento.Inicio, agendamento.Fim,
                    agendamento.Servicos.Select(s => s.Nome).ToList(), agendamento.Total, link, link), cancellationToken);

                agendamento.MarcarLembreteEnviado();
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
