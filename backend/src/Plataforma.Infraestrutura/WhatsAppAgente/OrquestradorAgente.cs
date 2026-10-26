using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Aplicacao.WhatsAppAgente.Ia;
using Plataforma.Dominio.WhatsAppAgente;
using Plataforma.Infraestrutura.Negocios;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// Agente de consulta (Fase 2): monta o histórico, conversa com o modelo e executa as ferramentas de leitura em laço limitado.
/// O modelo só tem o poder das ferramentas (somente leitura de dado público do próprio negócio). Tudo que o cliente recebe passa
/// pelo <see cref="SanitizadorRespostaAgente"/>; qualquer falha (provedor, timeout, laço estourado, resposta vazia) vira o
/// fallback fixo com o link público. Nunca loga texto de mensagem, prompt, resposta nem argumentos de texto livre.
/// </summary>
public sealed class OrquestradorAgente : IRespostaAgente
{
    private const int MaximoConteudoFerramentaAuditoria = 2000;

    private readonly PlataformaDbContext _dbContext;
    private readonly IAiProvider _provedor;
    private readonly IExecutorFerramentasAgente _executor;
    private readonly OpcoesIa _opcoes;
    private readonly OpcoesMarca _marca;
    private readonly ILogger<OrquestradorAgente> _logger;
    private readonly LimitadorUsoAgente _limitador;
    private readonly DisjuntorModeloIa _disjuntor;

    /// <summary>Fatia do orçamento de tempo que o modelo principal pode gastar quando há reserva (o resto é da reserva).</summary>
    private const double FracaoDoOrcamentoDoPrincipal = 0.6;

    public OrquestradorAgente(
        PlataformaDbContext dbContext, IAiProvider provedor, IExecutorFerramentasAgente executor, LimitadorUsoAgente limitador,
        DisjuntorModeloIa disjuntor, IOptions<OpcoesIa> opcoes, IOptions<OpcoesMarca> marca, ILogger<OrquestradorAgente> logger)
    {
        _disjuntor = disjuntor;
        _dbContext = dbContext;
        _provedor = provedor;
        _executor = executor;
        _limitador = limitador;
        _opcoes = opcoes.Value;
        _marca = marca.Value;
        _logger = logger;
    }

    public async Task<string?> ResponderAsync(ContextoRespostaAgente contexto, CancellationToken cancellationToken = default)
    {
        var negocio = await _dbContext.Negocios.AsNoTracking().FirstOrDefaultAsync(n => n.Id == contexto.NegocioId, cancellationToken);
        if (negocio is null)
            return null;

        var telefoneInstancia = await _dbContext.InstanciasWhatsApp.AsNoTracking()
            .Where(i => i.Id == contexto.InstanciaId).Select(i => i.Telefone).FirstOrDefaultAsync(cancellationToken);

        var link = ConstrutorUrlPublica.Construir(_marca, negocio.Slug.Valor, string.Empty);
        var hostPermitido = new Uri(link).Authority;
        // O nome do negócio é cadastro livre: o texto fixo também passa pelo sanitizador (link externo no nome não chega ao cliente).
        var fallback = SanitizadorRespostaAgente.SanitizarTextoFixo(TextoFallback(negocio.NomeExibido, link), link, telefoneInstancia);

        string? bruto;
        try
        {
            bruto = await ConversarAsync(contexto, negocio.NomeExibido, negocio.Fuso, link, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Qualquer falha inesperada: o cliente não fica sem resposta. Só tipo e IDs no log.
            _logger.LogWarning("Agente: falha ao conversar (conversa {ConversaId}, negócio {NegocioId}): {Tipo}.",
                contexto.ConversaId, contexto.NegocioId, excecao.GetType().Name);
            return fallback;
        }

        return SanitizadorRespostaAgente.Sanitizar(bruto, hostPermitido, telefoneInstancia, _opcoes.MaxCaracteresResposta) ?? fallback;
    }

    /// <summary>Texto fixo (nosso, não do modelo) para qualquer falha: leva o cliente ao link público.</summary>
    public static string TextoFallback(string nomeNegocio, string link) =>
        $"Aqui é o assistente virtual de {ConstrutorPromptAgente.LimparNome(nomeNegocio)}. Não consegui consultar as informações agora. " +
        $"Para ver os horários e agendar, acesse: {link}";

    /// <summary>
    /// Devolve o texto do modelo (ainda sem sanitizar) ou <c>null</c> se estourou laço/tempo ou veio vazio. Tenta o modelo principal
    /// e, só em falha de capacidade (<see cref="FalhaProvedorIaException.FalhaDeCapacidade"/>), o de reserva — refazendo a rodada
    /// DO ZERO com o histórico original: o <c>thoughtSignature</c> pertence ao modelo que o gerou e os turnos de ferramenta dele
    /// (com as assinaturas) não passam para a reserva. Ferramentas são só leitura, então executá-las de novo é seguro.
    /// </summary>
    private async Task<string?> ConversarAsync(
        ContextoRespostaAgente contexto, string nomeNegocio, string fuso, string link, CancellationToken cancellationToken)
    {
        var relogio = Stopwatch.StartNew();
        var orcamento = TimeSpan.FromSeconds(_opcoes.TempoMaximoProcessamentoSegundos);
        var agoraLocal = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(fuso));

        var prompt = ConstrutorPromptAgente.Construir(nomeNegocio, agoraLocal, link);
        var historico = await MontarHistoricoAsync(contexto.ConversaId, contexto.LimiteHistorico, cancellationToken);
        if (historico.Count == 0)
            return null;

        var ferramentas = _executor.Ferramentas
            .Select(f => new AiFerramenta(f.Nome, f.Descricao, f.Esquema)).ToList();
        var contextoConversa = new ContextoConversaAgente(contexto.NegocioId, contexto.InstanciaId, contexto.ConversaId);

        var reserva = _opcoes.Gemini.TemModeloReserva ? _opcoes.Gemini.ModeloReserva!.Trim() : null;
        if (reserva is null)
            return (await RodarModeloAsync(contexto, null, historico, prompt, ferramentas, contextoConversa, relogio, orcamento, cancellationToken)).Texto;

        if (!_disjuntor.Aberto(DateTimeOffset.UtcNow))
        {
            var principal = await RodarModeloAsync(contexto, null, historico, prompt, ferramentas, contextoConversa, relogio,
                orcamento * FracaoDoOrcamentoDoPrincipal, cancellationToken);
            if (!principal.FalhaDeCapacidade)
                return principal.Texto;

            _disjuntor.Abrir(DateTimeOffset.UtcNow);
            if (relogio.Elapsed >= orcamento)
                return null;

            // A rodada recomeça com o custo reavaliado: a tentativa que falhou também entrou em usos_ia.
            var motivo = await _limitador.VerificarCustoAsync(DateTimeOffset.UtcNow, cancellationToken);
            if (motivo != MotivoLimiteAgente.Nenhum)
            {
                _logger.LogWarning("Agente: limite {Motivo} atingido antes do modelo de reserva (conversa {ConversaId}).", motivo, contexto.ConversaId);
                return null;
            }

            _logger.LogWarning("Agente: modelo principal indisponível; usando o de reserva {Reserva} (conversa {ConversaId}).", reserva, contexto.ConversaId);
        }
        else
        {
            _logger.LogInformation("Agente: disjuntor do modelo principal aberto; direto ao de reserva {Reserva} (conversa {ConversaId}).", reserva, contexto.ConversaId);
        }

        return (await RodarModeloAsync(contexto, reserva, historico, prompt, ferramentas, contextoConversa, relogio, orcamento, cancellationToken)).Texto;
    }

    private sealed record ResultadoModelo(string? Texto, bool FalhaDeCapacidade);

    /// <summary>
    /// Laço de ferramentas de UM modelo (<paramref name="modelo"/> nulo = o principal), até <paramref name="fim"/> (tempo decorrido
    /// do relógio da rodada). Parte sempre do <paramref name="historico"/> original.
    /// </summary>
    private async Task<ResultadoModelo> RodarModeloAsync(
        ContextoRespostaAgente contexto, string? modelo, List<AiMensagem> historico, string prompt, List<AiFerramenta> ferramentas,
        ContextoConversaAgente contextoConversa, Stopwatch relogio, TimeSpan fim, CancellationToken cancellationToken)
    {
        var mensagens = historico;
        var modeloRegistrado = modelo ?? _opcoes.Gemini.Model;
        for (var iteracao = 1; iteracao <= _opcoes.MaxIteracoesFerramentas; iteracao++)
        {
            var restante = fim - relogio.Elapsed;
            if (restante <= TimeSpan.Zero)
            {
                _logger.LogWarning("Agente: orçamento de tempo esgotado (conversa {ConversaId}, iteração {Iteracao}, modelo {Modelo}).", contexto.ConversaId, iteracao, modeloRegistrado);
                // Estourou o tempo do principal na 2ª+ volta: a reserva ainda pode tentar com o tempo que sobrou.
                return new ResultadoModelo(null, FalhaDeCapacidade: modelo is null);
            }

            // A checagem do processador vale só para a 1ª chamada: nas seguintes o uso desta própria rodada já conta.
            if (iteracao > 1)
            {
                var motivo = await _limitador.VerificarCustoAsync(DateTimeOffset.UtcNow, cancellationToken);
                if (motivo != MotivoLimiteAgente.Nenhum)
                {
                    _logger.LogWarning("Agente: limite {Motivo} atingido no meio da rodada (conversa {ConversaId}, iteração {Iteracao}).",
                        motivo, contexto.ConversaId, iteracao);
                    return new ResultadoModelo(null, false);
                }
            }

            using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            limite.CancelAfter(restante);

            AiResponse resposta;
            var inicio = relogio.Elapsed;
            try
            {
                resposta = await _provedor.GenerateAsync(new AiRequest(prompt, mensagens, ferramentas, _opcoes.MaxTokensSaida, modelo), limite.Token);
            }
            catch (Exception excecao) when (excecao is FalhaProvedorIaException || (excecao is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A tentativa falha também é gravada (sucesso = false) e conta nos limites de chamadas por dia, como sempre.
                await RegistrarUsoAsync(contexto, modeloRegistrado, 0, 0, sucesso: false, cancellationToken);
                _logger.LogWarning("Agente: provedor de IA falhou (conversa {ConversaId}, iteração {Iteracao}, modelo {Modelo}): {Tipo}{Status}.",
                    contexto.ConversaId, iteracao, modeloRegistrado, (excecao as FalhaProvedorIaException)?.Tipo ?? "timeout",
                    (excecao as FalhaProvedorIaException)?.StatusHttp is int s ? $" HTTP {s}" : string.Empty);
                // Timeout (nosso prazo) e falha de capacidade passam para a reserva; 400/401/403 e resposta inválida, não.
                var capacidade = excecao is not FalhaProvedorIaException falha || falha.FalhaDeCapacidade;
                return new ResultadoModelo(null, capacidade && modelo is null);
            }

            await RegistrarUsoAsync(contexto, resposta.Modelo, resposta.TokensEntrada, resposta.TokensSaida, sucesso: true, cancellationToken);
            _logger.LogInformation("Agente: chamada ao modelo (conversa {ConversaId}, iteração {Iteracao}, tokens {Entrada}/{Saida}, {Ms} ms, ferramentas {Ferramentas}).",
                contexto.ConversaId, iteracao, resposta.TokensEntrada, resposta.TokensSaida, (long)(relogio.Elapsed - inicio).TotalMilliseconds, resposta.Chamadas.Count);

            if (resposta.Chamadas.Count == 0)
                return new ResultadoModelo(resposta.Texto, false);

            // Estourou as voltas e o modelo ainda pede ferramenta: não executa mais nada; vai para o fallback.
            if (iteracao == _opcoes.MaxIteracoesFerramentas)
            {
                _logger.LogWarning("Agente: limite de {Max} iterações de ferramenta atingido (conversa {ConversaId}).",
                    _opcoes.MaxIteracoesFerramentas, contexto.ConversaId);
                return new ResultadoModelo(null, false);
            }

            var resultados = new List<AiMensagem>();
            var auditoria = new List<MensagemWhatsApp>();
            for (var i = 0; i < resposta.Chamadas.Count; i++)
            {
                var chamada = resposta.Chamadas[i];
                JsonElement conteudo;
                if (i >= _opcoes.MaxChamadasFerramentaPorIteracao)
                {
                    conteudo = Ferramentas.RespostaFerramenta.Erro(Ferramentas.ErrosFerramenta.LimiteChamadas);
                }
                else
                {
                    conteudo = await _executor.ExecutarAsync(contextoConversa, chamada.Nome, chamada.Argumentos, limite.Token);
                    _logger.LogInformation("Agente: ferramenta executada (conversa {ConversaId}, ferramenta {Ferramenta}).",
                        contexto.ConversaId, NomeSeguro(chamada.Nome, ferramentas));
                }

                resultados.Add(new AiMensagem(PapelIa.Ferramenta, Resultado: new AiResultadoFerramenta(chamada.Nome, conteudo)));
                auditoria.Add(new MensagemWhatsApp(
                    contexto.NegocioId, contexto.InstanciaId, contexto.ConversaId, null, PapelMensagemWhatsApp.Ferramenta,
                    Truncar(chamada.Argumentos.ValueKind == JsonValueKind.Undefined ? "{}" : chamada.Argumentos.GetRawText(), MaximoConteudoFerramentaAuditoria),
                    NomeSeguro(chamada.Nome, ferramentas)));
            }

            _dbContext.MensagensWhatsApp.AddRange(auditoria);
            await _dbContext.SaveChangesAsync(cancellationToken);

            // Resultado de ferramenta só vive dentro desta rodada: não é reenviado em rodadas futuras.
            mensagens = [.. mensagens, new AiMensagem(PapelIa.Modelo, resposta.Texto, resposta.Chamadas), .. resultados];
        }

        return new ResultadoModelo(null, false);
    }

    private async Task RegistrarUsoAsync(ContextoRespostaAgente contexto, string modelo, int entrada, int saida, bool sucesso, CancellationToken cancellationToken)
    {
        _dbContext.UsosIa.Add(new UsoIa(contexto.NegocioId, contexto.ConversaId, modelo, entrada, saida, sucesso));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Últimas mensagens de usuário/assistente DESTA conversa, truncadas e limitadas em quantidade e em caracteres; turnos
    /// seguidos do mesmo papel viram um só e o histórico sempre começa por uma fala do cliente.
    /// </summary>
    private async Task<List<AiMensagem>> MontarHistoricoAsync(Guid conversaId, DateTimeOffset? limiteSuperior, CancellationToken cancellationToken)
    {
        var recentes = await _dbContext.MensagensWhatsApp.AsNoTracking()
            .Where(m => m.ConversaId == conversaId
                && (m.Papel == PapelMensagemWhatsApp.Usuario || m.Papel == PapelMensagemWhatsApp.Assistente)
                && (limiteSuperior == null || m.CriadoEm <= limiteSuperior))
            .OrderByDescending(m => m.CriadoEm)
            .Take(_opcoes.MaxMensagensHistorico)
            .Select(m => new { m.Papel, m.Conteudo })
            .ToListAsync(cancellationToken);

        var escolhidas = new List<(PapelIa Papel, string Texto)>();
        var total = 0;
        foreach (var mensagem in recentes)
        {
            var texto = LimparEntrada(mensagem.Conteudo, _opcoes.MaxCaracteresMensagem);
            if (texto.Length == 0)
                continue;

            // Cada mensagem depois da primeira custa 1 caractere de separador (quebra de linha ao juntar turnos).
            var disponivel = _opcoes.MaxCaracteresHistorico - total - (escolhidas.Count > 0 ? 1 : 0);
            if (texto.Length > disponivel)
            {
                // A mais recente (primeira) nunca estoura o teto: é truncada ao orçamento. As mais antigas que não cabem ficam de fora.
                if (escolhidas.Count > 0 || disponivel <= 0)
                    break;

                texto = texto[..disponivel].Trim();
                if (texto.Length == 0)
                    break;
            }

            total += texto.Length + (escolhidas.Count > 0 ? 1 : 0);

            escolhidas.Add((mensagem.Papel == PapelMensagemWhatsApp.Usuario ? PapelIa.Usuario : PapelIa.Modelo, texto));
        }

        escolhidas.Reverse();

        var resultado = new List<AiMensagem>();
        foreach (var (papel, texto) in escolhidas)
        {
            if (resultado.Count == 0 && papel != PapelIa.Usuario)
                continue;

            if (resultado.Count > 0 && resultado[^1].Papel == papel)
                resultado[^1] = resultado[^1] with { Texto = resultado[^1].Texto + "\n" + texto };
            else
                resultado.Add(new AiMensagem(papel, texto));
        }

        return resultado;
    }

    /// <summary>
    /// Remove caracteres de controle (menos quebra de linha), minimiza dado pessoal (<see cref="MinimizadorDadosPessoais"/>) e só
    /// então limita o tamanho — minimizar antes de cortar evita deixar meio número de CPF/telefone passar. A entrada do cliente é dado.
    /// </summary>
    public static string LimparEntrada(string? texto, int maximo)
    {
        if (string.IsNullOrEmpty(texto))
            return string.Empty;

        // Teto de trabalho para a heurística (texto absurdamente longo seria cortado de qualquer jeito).
        var teto = Math.Min(texto.Length, maximo * 4);
        var sb = new StringBuilder(teto);
        for (var i = 0; i < teto; i++)
        {
            var c = texto[i];
            sb.Append(c == '\n' ? c : char.IsControl(c) ? ' ' : c);
        }

        var minimizado = MinimizadorDadosPessoais.Minimizar(sb.ToString());
        return (minimizado.Length > maximo ? minimizado[..maximo] : minimizado).Trim();
    }

    private static string Truncar(string texto, int maximo) => texto.Length > maximo ? texto[..maximo] : texto;

    /// <summary>Nome devolvido no lugar de qualquer nome que não seja do catálogo.</summary>
    public const string FerramentaDesconhecida = "ferramenta_desconhecida";

    /// <summary>
    /// Nome de ferramenta vem do modelo (pode ser qualquer texto, inclusive um telefone): só nomes do catálogo vão para log e
    /// banco; o resto vira um marcador fixo.
    /// </summary>
    public static string NomeSeguro(string? nome, IEnumerable<AiFerramenta> catalogo) =>
        nome is not null && catalogo.Any(f => string.Equals(f.Nome, nome, StringComparison.Ordinal)) ? nome : FerramentaDesconhecida;
}
