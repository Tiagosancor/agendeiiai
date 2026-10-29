using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Dominio.Agendamentos;
using Plataforma.Dominio.Comum;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>Agendamento que o número da conversa pode ver e receber o link de gestão. Só id e início; o resto vem da gestão pública.</summary>
public sealed record AgendamentoDoNumero(Guid Id, DateTimeOffset Inicio);

/// <summary>
/// ÚNICA fonte de "quais agendamentos este número do WhatsApp pode ver" (G1/G2 do plano da Fase 4), usada pela leitura, pelo envio do
/// link e pela releitura do servidor antes de enviar. Regras, todas no banco e sob o negócio do contexto (filtro global MAIS o
/// <c>negocio_id</c> explícito): status <c>Agendado</c>, início no futuro e dentro de <c>DiasMaximosConsulta</c>, origem
/// <c>LinkPublico</c> ou <c>WhatsApp</c> (telefone verificado por código ou pela posse do número — P2; <c>Painel</c> e <c>Encaixe</c>
/// nunca aparecem), cliente NÃO excluído cujo telefone esteja numa das formas equivalentes (9º dígito) do telefone da conversa. O
/// telefone vem da conversa (servidor); o modelo nunca o informa. Conversa inexistente, anonimizada ou de outro negócio = vazio.
/// </summary>
public sealed class ConsultaAgendamentosDoClienteAgente
{
    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly OpcoesIa _opcoes;

    public ConsultaAgendamentosDoClienteAgente(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio, IOptions<OpcoesIa> opcoes)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
        _opcoes = opcoes.Value;
    }

    /// <summary>Até <paramref name="maximo"/> agendamentos elegíveis, do mais próximo ao mais distante. Com <paramref name="agendamentoId"/>, só ele (se elegível).</summary>
    public async Task<IReadOnlyList<AgendamentoDoNumero>> ListarAsync(
        Guid conversaId, int maximo, Guid? agendamentoId = null, CancellationToken cancellationToken = default)
    {
        var negocioId = _contextoNegocio.NegocioId!.Value;

        var telefoneConversa = await _dbContext.ConversasWhatsApp.AsNoTracking()
            .Where(c => c.Id == conversaId && c.NegocioId == negocioId)
            .Select(c => c.TelefoneCliente).FirstOrDefaultAsync(cancellationToken);
        if (telefoneConversa is null || !TelefoneE164.TentarCriar(telefoneConversa, out var telefone) || telefone is null)
            return [];

        // Identidade e agendamento na MESMA consulta SQL: troca de telefone ou exclusão do cliente pelo painel entre duas leituras
        // não deixa um id de cliente "velho" autorizar o resultado.
        var formas = telefone.FormasEquivalentes();
        var forma1 = formas[0];
        var forma2 = formas.Count > 1 ? formas[1] : formas[0];

        var agora = DateTimeOffset.UtcNow;
        var limite = agora.AddDays(_opcoes.DiasMaximosConsulta);
        var consulta =
            from a in _dbContext.Agendamentos.AsNoTracking()
            join c in _dbContext.Clientes.AsNoTracking() on a.ClienteId equals c.Id
            where a.NegocioId == negocioId && c.NegocioId == negocioId
                && !c.Excluido && (c.Telefone == forma1 || c.Telefone == forma2)
                && a.Status == StatusAgendamento.Agendado
                && a.Inicio > agora && a.Inicio <= limite
                && (a.Origem == OrigemAgendamento.LinkPublico || a.Origem == OrigemAgendamento.WhatsApp)
            select a;
        if (agendamentoId is { } id)
            consulta = consulta.Where(a => a.Id == id);

        return await consulta.OrderBy(a => a.Inicio).ThenBy(a => a.Id).Take(maximo)
            .Select(a => new AgendamentoDoNumero(a.Id, a.Inicio)).ToListAsync(cancellationToken);
    }

    /// <summary>Este agendamento ainda é elegível para o número da conversa? (releitura do servidor antes de enviar o link)</summary>
    public async Task<bool> ElegivelAsync(Guid conversaId, Guid agendamentoId, CancellationToken cancellationToken = default) =>
        (await ListarAsync(conversaId, 1, agendamentoId, cancellationToken)).Count == 1;
}
