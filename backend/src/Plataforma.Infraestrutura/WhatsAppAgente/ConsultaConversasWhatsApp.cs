using Microsoft.EntityFrameworkCore;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Dominio.Comum;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Leitura das conversas do agente para o painel (G16). Só o negócio do contexto (filtro global + <c>negocio_id</c> explícito em
/// toda consulta); anonimizada nunca aparece. A LISTA não carrega texto de mensagem; o detalhe só devolve cliente, assistente e
/// mensagens fixas do servidor — papel Ferramenta e notas de contexto (internas) nunca saem, e o link de gestão que algum texto
/// ainda carregue é mascarado outra vez na saída. Nada daqui vai para log.
/// </summary>
public sealed class ConsultaConversasWhatsApp : IConsultaConversasWhatsApp
{
    public const int TamanhoPagina = 20;
    public const int MaximoMensagens = 50;

    private readonly PlataformaDbContext _dbContext;
    private readonly IContextoNegocio _contextoNegocio;

    public ConsultaConversasWhatsApp(PlataformaDbContext dbContext, IContextoNegocio contextoNegocio)
    {
        _dbContext = dbContext;
        _contextoNegocio = contextoNegocio;
    }

    private Guid NegocioId => _contextoNegocio.NegocioId ?? throw new InvalidOperationException("Negócio não resolvido.");

    private IQueryable<ConversaWhatsApp> Conversas(Guid negocioId) =>
        _dbContext.ConversasWhatsApp.AsNoTracking()
            .Where(c => c.NegocioId == negocioId && !c.TelefoneCliente.StartsWith(ConversaWhatsApp.PrefixoTelefoneAnonimizado));

    public async Task<PaginaConversasWhatsApp> ListarAsync(FiltroConversasWhatsApp filtro, int pagina, CancellationToken cancellationToken = default)
    {
        var negocioId = NegocioId;
        pagina = Math.Max(1, pagina);

        var consulta = Conversas(negocioId);
        consulta = filtro switch
        {
            FiltroConversasWhatsApp.Aguardando => consulta.Where(c => c.Status == StatusConversaWhatsApp.Humano && c.HumanoPorUsuarioId == null),
            FiltroConversasWhatsApp.Humano => consulta.Where(c => c.Status == StatusConversaWhatsApp.Humano),
            _ => consulta,
        };

        var total = await consulta.CountAsync(cancellationToken);
        // Em long: (pagina - 1) * 20 estoura int para páginas enormes. Além do que o Skip aceita não há linha nenhuma: página vazia.
        var deslocamento = (long)(pagina - 1) * TamanhoPagina;
        if (deslocamento > int.MaxValue)
            deslocamento = int.MaxValue + 1L;
        var pagina_ = consulta.OrderByDescending(c => c.UltimaMensagemEm).ThenBy(c => c.Id).Skip((int)deslocamento).Take(TamanhoPagina);
        var itens = deslocamento > int.MaxValue ? [] : await ResumirAsync(negocioId, pagina_, cancellationToken);
        return new PaginaConversasWhatsApp(itens, total, pagina, TamanhoPagina);
    }

    public async Task<DetalheConversaWhatsApp?> ObterAsync(Guid conversaId, DateTimeOffset? antesDe, Guid? antesDeId, CancellationToken cancellationToken = default)
    {
        var negocioId = NegocioId;
        var resumo = (await ResumirAsync(negocioId, Conversas(negocioId).Where(c => c.Id == conversaId), cancellationToken)).FirstOrDefault();
        if (resumo is null)
            return null;

        // Só o que o cliente escreveu, o que lhe foi respondido e os textos fixos do servidor. Chamada de ferramenta e nota de contexto
        // (internas, às vezes com ids) nunca saem.
        var mensagens = _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.NegocioId == negocioId && m.ConversaId == conversaId
                && m.Papel != PapelMensagemWhatsApp.Ferramenta
                && (m.NomeFerramenta == null || m.NomeFerramenta != MarcadoresAgente.ContextoConsulta)
                && m.Conteudo != string.Empty);
        // Cursor composto, com os MESMOS critérios da ordenação (instante desc, Id desc): mensagens no mesmo instante da fronteira
        // não se perdem nem se repetem. Sem o Id (cliente antigo), vale só o instante.
        if (antesDe is { } limite)
        {
            mensagens = antesDeId is { } limiteId
                ? mensagens.Where(m => m.CriadoEm < limite || (m.CriadoEm == limite && m.Id.CompareTo(limiteId) < 0))
                : mensagens.Where(m => m.CriadoEm < limite);
        }

        var lote = await mensagens.OrderByDescending(m => m.CriadoEm).ThenByDescending(m => m.Id)
            .Take(MaximoMensagens + 1)
            .Select(m => new { m.Id, m.Papel, m.Conteudo, m.CriadoEm })
            .ToListAsync(cancellationToken);

        var haMaisAntigas = lote.Count > MaximoMensagens;
        var cronologicas = lote.Take(MaximoMensagens).Reverse()
            .Select(m => new MensagemConversaWhatsApp(m.Id, NomeDoPapel(m.Papel), MascaraLinkGestao.Aplicar(m.Conteudo), m.CriadoEm))
            .ToList();

        return new DetalheConversaWhatsApp(
            resumo.Id, resumo.Telefone, resumo.ClienteNome, resumo.Status, resumo.HumanoDesde, resumo.HumanoMotivo, resumo.HumanoAte,
            resumo.UltimaMensagemEm, resumo.MensagensDesdeHumano, resumo.AguardandoAtendimento, cronologicas, haMaisAntigas);
    }

    public async Task<ResumoConversasWhatsApp> ResumoAsync(CancellationToken cancellationToken = default)
    {
        var humanas = Conversas(NegocioId).Where(c => c.Status == StatusConversaWhatsApp.Humano);
        var emHumano = await humanas.CountAsync(cancellationToken);
        var aguardando = await humanas.CountAsync(c => c.HumanoPorUsuarioId == null, cancellationToken);
        return new ResumoConversasWhatsApp(aguardando, emHumano);
    }

    private static string NomeDoPapel(PapelMensagemWhatsApp papel) => papel switch
    {
        PapelMensagemWhatsApp.Usuario => "Cliente",
        PapelMensagemWhatsApp.Assistente => "Assistente",
        _ => "Sistema",
    };

    /// <summary>Resumos das conversas da consulta: uma consulta para as conversas (com a contagem desde o humano) e UMA para os nomes dos clientes.</summary>
    private async Task<List<ConversaWhatsAppResumo>> ResumirAsync(Guid negocioId, IQueryable<ConversaWhatsApp> consulta, CancellationToken cancellationToken)
    {
        var linhas = await consulta.Select(c => new
        {
            c.Id,
            c.TelefoneCliente,
            c.Status,
            c.HumanoDesde,
            c.HumanoMotivo,
            c.HumanoAte,
            c.HumanoPorUsuarioId,
            c.UltimaMensagemEm,
            Desde = c.Status == StatusConversaWhatsApp.Humano && c.HumanoDesde != null
                ? _dbContext.MensagensWhatsApp.Count(m => m.ConversaId == c.Id && m.NegocioId == negocioId
                    && m.Papel == PapelMensagemWhatsApp.Usuario && m.CriadoEm > c.HumanoDesde)
                : 0,
        }).ToListAsync(cancellationToken);

        var nomes = await NomesDosClientesAsync(negocioId, linhas.Select(l => l.TelefoneCliente), cancellationToken);

        return linhas.Select(l => new ConversaWhatsAppResumo(
            l.Id, l.TelefoneCliente, nomes.GetValueOrDefault(l.TelefoneCliente), l.Status.ToString(), l.HumanoDesde,
            l.HumanoMotivo?.ToString(), l.HumanoAte, l.UltimaMensagemEm, l.Desde,
            l.Status == StatusConversaWhatsApp.Humano && l.HumanoPorUsuarioId == null)).ToList();
    }

    /// <summary>
    /// Nome do cadastro por telefone, nas duas formas do 9º dígito, numa única consulta para a página inteira (sem N+1). Cliente
    /// excluído (anonimizado) não entra. Havendo mais de um, vale o que tem o telefone exatamente igual ao da conversa.
    /// </summary>
    private async Task<Dictionary<string, string>> NomesDosClientesAsync(Guid negocioId, IEnumerable<string> telefones, CancellationToken cancellationToken)
    {
        var formasPorTelefone = new Dictionary<string, IReadOnlyList<TelefoneE164>>(StringComparer.Ordinal);
        foreach (var telefone in telefones.Distinct(StringComparer.Ordinal))
        {
            if (TelefoneE164.TentarCriar(telefone, out var valido) && valido is not null)
                formasPorTelefone[telefone] = valido.FormasEquivalentes();
        }

        if (formasPorTelefone.Count == 0)
            return [];

        var todas = formasPorTelefone.Values.SelectMany(f => f).Distinct().ToList();
        var clientes = await _dbContext.Clientes.AsNoTracking()
            .Where(c => c.NegocioId == negocioId && !c.Excluido && c.Telefone != null && todas.Contains(c.Telefone))
            .OrderBy(c => c.CriadoEm)
            .Select(c => new { c.Nome, c.Telefone })
            .ToListAsync(cancellationToken);

        var resultado = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (telefone, formas) in formasPorTelefone)
        {
            var achado = clientes.FirstOrDefault(c => c.Telefone!.Valor == telefone)
                ?? clientes.FirstOrDefault(c => formas.Any(f => f.Valor == c.Telefone!.Valor));
            if (achado is not null)
                resultado[telefone] = achado.Nome;
        }

        return resultado;
    }
}
