using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Auditoria;
using Plataforma.Aplicacao.Clientes;
using Plataforma.Dominio.Clientes;
using Plataforma.Dominio.Negocios;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Clientes;

public sealed class ServicoLembreteRetorno : IServicoLembreteRetorno
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IRegistroAuditoria _auditoria;

    public ServicoLembreteRetorno(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IRegistroAuditoria auditoria)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _auditoria = auditoria;
    }

    public async Task<ConfiguracaoLembreteRetorno?> ObterConfiguracaoAsync(CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);

        return negocio is null ? null : Mapear(negocio);
    }

    public async Task<bool> AtualizarConfiguracaoAsync(
        bool ativo, int dias, TimeOnly horario, string mensagem, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.FirstOrDefaultAsync(n => n.Id == _contextoNegocio.NegocioId, cancellationToken);
        if (negocio is null)
            return false;

        var antes = Mapear(negocio);
        negocio.ConfigurarLembreteRetorno(ativo, dias, horario, mensagem, DateTimeOffset.UtcNow);
        var depois = Mapear(negocio);

        if (antes != depois)
        {
            var alteracoes = new List<string>();
            if (antes.Ativo != depois.Ativo)
                alteracoes.Add(depois.Ativo ? "ligado" : "desligado");
            if (antes.Dias != depois.Dias)
                alteracoes.Add($"dias de {antes.Dias} para {depois.Dias}");
            if (antes.Horario != depois.Horario)
                alteracoes.Add($"horário de {antes.Horario} para {depois.Horario}");
            if (antes.Mensagem != depois.Mensagem)
                alteracoes.Add("mensagem alterada");

            _auditoria.Registrar(AcoesAuditoria.ConfigurarLembreteRetorno, nameof(Negocio), negocio.Id,
                $"Lembrete de retorno: {string.Join("; ", alteracoes)}.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RelatorioLembreteRetorno> ObterRelatorioAsync(int dias, CancellationToken cancellationToken = default)
    {
        var desde = DateTimeOffset.UtcNow.AddDays(-dias);

        // "Gerou agendamento" é marcado pelo JobLembreteRetorno (agendamento feito em até 7 dias depois do envio).
        var enviados = _dbContext.LembretesRetornoEnvios
            .Where(e => e.Status == StatusLembreteRetorno.Enviado && e.EnviadoEm >= desde);

        return new RelatorioLembreteRetorno(
            dias,
            await enviados.CountAsync(cancellationToken),
            await enviados.CountAsync(e => e.AgendamentoGeradoId != null, cancellationToken));
    }

    public async Task<bool> DefinirAceiteClienteAsync(Guid clienteId, bool aceita, CancellationToken cancellationToken = default)
    {
        var cliente = await _dbContext.Clientes.FirstOrDefaultAsync(c => c.Id == clienteId && !c.Excluido, cancellationToken);
        if (cliente is null)
            return false;

        if (cliente.AceitaLembreteRetorno != aceita)
        {
            cliente.DefinirAceitaLembreteRetorno(aceita);
            _auditoria.Registrar(AcoesAuditoria.AlterarAceiteLembreteRetorno, nameof(Cliente), cliente.Id,
                aceita ? "Voltou a aceitar o lembrete de retorno." : "Deixou de aceitar o lembrete de retorno.");
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return true;
    }

    private static ConfiguracaoLembreteRetorno Mapear(Negocio negocio) => new(
        negocio.LembreteRetornoAtivo,
        negocio.LembreteRetornoDias,
        negocio.LembreteRetornoHorario.ToString("HH:mm", CultureInfo.InvariantCulture),
        negocio.LembreteRetornoMensagem);
}
