using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Apagar mensagens do agente sem quebrar auditoria nem limites (G11): as mensagens somem; a linha da conversa fica, com o telefone
/// trocado (<c>usos_ia</c> e <c>rodadas_agente</c> têm FK cascade para ela e são a base da cota e do diagnóstico, só números).
/// Ids de mensagem nas pendências não têm FK e ficam órfãos; as pendências não são tocadas (ordem de locks).
/// </summary>
public sealed class ServicoLgpdConversasWhatsApp : IServicoLgpdConversasWhatsApp
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ServicoLgpdConversasWhatsApp(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    public async Task<int> AnonimizarDoClienteAsync(Guid clienteId, TelefoneE164? telefone, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var ids = await ConversasDoClienteAsync(negocioId, clienteId, telefone, cancellationToken);
        return await AnonimizarAsync(negocioId, ids, cancellationToken);
    }

    public async Task<bool> ApagarConversaAsync(Guid conversaId, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var estrategia = _dbContext.Database.CreateExecutionStrategy();
        return await estrategia.ExecuteAsync(async () =>
        {
            await using var transacao = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            var existe = await _dbContext.ConversasWhatsApp.AsNoTracking()
                .AnyAsync(c => c.Id == conversaId && c.NegocioId == negocioId, cancellationToken);
            if (!existe)
            {
                await transacao.RollbackAsync(cancellationToken);
                return false;
            }

            await AnonimizarAsync(negocioId, [conversaId], cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            return true;
        });
    }

    public async Task<IReadOnlyList<ExportacaoConversaWhatsApp>> ExportarDoClienteAsync(
        Guid clienteId, TelefoneE164? telefone, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;
        var ids = await ConversasDoClienteAsync(negocioId, clienteId, telefone, cancellationToken);
        if (ids.Count == 0)
            return [];

        var conversas = await _dbContext.ConversasWhatsApp.AsNoTracking()
            .Where(c => c.NegocioId == negocioId && ids.Contains(c.Id))
            .OrderBy(c => c.UltimaMensagemEm)
            .Select(c => new { c.Id, c.UltimaMensagemEm })
            .ToListAsync(cancellationToken);

        // O que o cliente escreveu e o que lhe foi respondido. Chamadas de ferramenta e notas de contexto (internas) não saem.
        var mensagens = await _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.NegocioId == negocioId && ids.Contains(m.ConversaId)
                && m.Papel != PapelMensagemWhatsApp.Ferramenta
                && (m.NomeFerramenta == null || m.NomeFerramenta != MarcadoresAgente.ContextoConsulta))
            .OrderBy(m => m.CriadoEm)
            .Select(m => new { m.ConversaId, m.CriadoEm, m.Papel, m.Conteudo })
            .ToListAsync(cancellationToken);

        return conversas.Select(c => new ExportacaoConversaWhatsApp(
            c.UltimaMensagemEm,
            mensagens.Where(m => m.ConversaId == c.Id)
                .Select(m => new ExportacaoMensagemWhatsApp(m.CriadoEm, NomeDoPapel(m.Papel), m.Conteudo))
                .ToList())).ToList();
    }

    private static string NomeDoPapel(PapelMensagemWhatsApp papel) => papel switch
    {
        PapelMensagemWhatsApp.Usuario => "cliente",
        PapelMensagemWhatsApp.Assistente => "assistente",
        _ => "sistema",
    };

    /// <summary>Conversas do cliente: as que apontam para ele OU as do telefone dele (as duas formas do 9º dígito) — a busca é sempre pelo telefone também.</summary>
    private async Task<List<Guid>> ConversasDoClienteAsync(Guid negocioId, Guid clienteId, TelefoneE164? telefone, CancellationToken cancellationToken)
    {
        var telefones = telefone is null ? new List<string>() : telefone.FormasEquivalentes().Select(t => t.Valor).ToList();
        return await _dbContext.ConversasWhatsApp.AsNoTracking()
            .Where(c => c.NegocioId == negocioId && (c.ClienteId == clienteId || telefones.Contains(c.TelefoneCliente)))
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>Roda na transação do chamador: apaga as mensagens e anonimiza as linhas.</summary>
    private async Task<int> AnonimizarAsync(Guid negocioId, IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
            return 0;

        await _dbContext.MensagensWhatsApp
            .Where(m => m.NegocioId == negocioId && ids.Contains(m.ConversaId))
            .ExecuteDeleteAsync(cancellationToken);

        // Pendências NÃO são tocadas de propósito: a confirmação toma pendência → agendamento → cliente, e esta rotina roda com o cliente já
        // travado; mexer numa pendência aqui inverteria a ordem global de locks (o teste de anonimização no meio da confirmação trava).
        // O nome informado numa pendência já é zerado quando ela termina (PendenciaAgendamentoWhatsApp.Encerrar), no máximo 10 min depois.

        var conversas = await _dbContext.ConversasWhatsApp
            .Where(c => c.NegocioId == negocioId && ids.Contains(c.Id))
            .ToListAsync(cancellationToken);

        // Telefones já usados na(s) instância(s): o índice único é (instância, telefone).
        var instancias = conversas.Select(c => c.InstanciaId).Distinct().ToList();
        var emUso = (await _dbContext.ConversasWhatsApp.AsNoTracking()
                .Where(c => instancias.Contains(c.InstanciaId) && c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado))
                .Select(c => c.TelefoneCliente)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        foreach (var conversa in conversas)
            conversa.Anonimizar(emUso);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return conversas.Count;
    }
}
