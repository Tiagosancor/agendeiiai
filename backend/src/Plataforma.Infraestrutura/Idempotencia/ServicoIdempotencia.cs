using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Plataforma.Aplicacao.Abstracoes;
using Plataforma.Aplicacao.Idempotencia;
using Plataforma.Dominio.Idempotencia;
using Plataforma.Infraestrutura.Opcoes;
using Plataforma.Infraestrutura.Persistencia;

namespace Plataforma.Infraestrutura.Idempotencia;

/// <summary>
/// Reivindicação atômica da <c>Idempotency-Key</c>. <c>INSERT … ON CONFLICT DO NOTHING</c> na transação do efeito: o duplicado
/// concorrente espera a primeira terminar (commit → não insere e lê o resultado; rollback → vira a dona), sem nunca abortar a
/// transação com um 23505. A espera é limitada por <c>lock_timeout</c> local, devolvido ao padrão logo depois — as travas
/// seguintes do serviço (advisory, <c>FOR UPDATE</c>) mantêm o comportamento de sempre.
/// </summary>
public sealed class ServicoIdempotencia : IServicoIdempotencia
{
    private const string FalhaTravaIndisponivel = "55P03";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly PlataformaDbContext _dbContext;
    private readonly ContextoIdempotencia _contexto;
    private readonly IContextoNegocio _contextoNegocio;
    private readonly IUsuarioAtual _usuarioAtual;
    private readonly OpcoesIdempotencia _opcoes;
    private bool _reivindicadaAgora;

    public ServicoIdempotencia(
        PlataformaDbContext dbContext, ContextoIdempotencia contexto, IContextoNegocio contextoNegocio,
        IUsuarioAtual usuarioAtual, IOptions<OpcoesIdempotencia> opcoes)
    {
        _dbContext = dbContext;
        _contexto = contexto;
        _contextoNegocio = contextoNegocio;
        _usuarioAtual = usuarioAtual;
        _opcoes = opcoes.Value;
    }

    public bool Ativa => _opcoes.Habilitada && _contexto.TemChave && _contextoNegocio.NegocioId is not null && _usuarioAtual.UsuarioId is not null;

    public bool Repeticao => _contexto.Repeticao;

    private string Principal => $"u:{_usuarioAtual.UsuarioId}";

    public Guid? IdDerivado(string finalidade) =>
        Ativa ? HashDeRequisicao.IdDerivado(_contextoNegocio.NegocioId!.Value, Principal, _contexto.Escopo!, _contexto.Chave!, finalidade) : null;

    public async Task<T?> ReivindicarAsync<T>(CancellationToken cancellationToken) where T : class
    {
        _reivindicadaAgora = false;
        if (!Ativa)
            return null;

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var agora = DateTimeOffset.UtcNow;
        var expiraEm = agora.AddHours(_opcoes.RetencaoHoras);
        var principal = Principal;
        var (escopo, chave, hash) = (_contexto.Escopo!, _contexto.Chave!, _contexto.HashRequisicao!);

        // Inteiro vindo da configuração validada (1–25): sem risco de injeção na string do SET.
        await _dbContext.Database.ExecuteSqlRawAsync($"SET LOCAL lock_timeout = '{_opcoes.EsperaMaximaSegundos * 1000}ms'", cancellationToken);

        int inseridas;
        try
        {
            inseridas = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO requisicoes_idempotentes
                    (id, negocio_id, principal, escopo, chave, hash_requisicao, status_http, resultado, criado_em, expira_em)
                VALUES ({Guid.NewGuid()}, {negocioId}, {principal}, {escopo}, {chave}, {hash}, 0, '{{}}'::jsonb, {agora}, {expiraEm})
                ON CONFLICT (negocio_id, principal, escopo, chave) DO NOTHING", cancellationToken);
        }
        catch (Exception excecao) when (EhTravaIndisponivel(excecao))
        {
            // O Npgsql pode tratar 55P03 como transitória e a estratégia repetiria o callback várias vezes: vira um 409 não transitório.
            throw new IdempotenciaException(
                IdempotenciaException.CodigoEmAndamento, 409,
                "Esta operação ainda está sendo processada. Aguarde um instante e tente de novo.", retryAfterSegundos: 2);
        }

        await _dbContext.Database.ExecuteSqlRawAsync("SET LOCAL lock_timeout TO DEFAULT", cancellationToken);

        if (inseridas == 1)
        {
            _reivindicadaAgora = true;
            return null;
        }

        var existente = await _dbContext.Set<RequisicaoIdempotente>().AsNoTracking()
            .Where(r => r.Principal == principal && r.Escopo == escopo && r.Chave == chave)
            .Select(r => new { r.HashRequisicao, r.StatusHttp, r.Resultado })
            .SingleAsync(cancellationToken);

        if (!string.Equals(existente.HashRequisicao, hash, StringComparison.Ordinal))
            throw new IdempotenciaException(
                IdempotenciaException.CodigoChaveReutilizada, 422,
                "Esta chave de idempotência já foi usada com outros dados. Gere uma nova chave para uma nova operação.");

        if (existente.StatusHttp == 0)
            throw new InvalidOperationException("Registro de idempotência confirmado sem resultado.");

        _contexto.Repeticao = true;
        return JsonSerializer.Deserialize<T>(existente.Resultado, Json)
            ?? throw new InvalidOperationException("Resultado de idempotência ilegível.");
    }

    public async Task ConcluirAsync<T>(T resultado, CancellationToken cancellationToken) where T : class
    {
        if (!_reivindicadaAgora)
            return;

        var json = JsonSerializer.Serialize(resultado, Json);
        if (Encoding.UTF8.GetByteCount(json) > RequisicaoIdempotente.TamanhoMaximoResultadoBytes)
            throw new InvalidOperationException("Resultado de idempotência acima do limite.");

        var negocioId = _contextoNegocio.NegocioId!.Value;
        var (principal, escopo, chave) = (Principal, _contexto.Escopo!, _contexto.Chave!);
        var status = _contexto.StatusSucesso;
        var atualizadas = await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE requisicoes_idempotentes SET status_http = {status}, resultado = {json}::jsonb, atualizado_em = {DateTimeOffset.UtcNow}
            WHERE negocio_id = {negocioId} AND principal = {principal} AND escopo = {escopo} AND chave = {chave}", cancellationToken);
        if (atualizadas != 1)
            throw new InvalidOperationException("Registro de idempotência não encontrado para concluir.");

        _reivindicadaAgora = false;
    }

    private static bool EhTravaIndisponivel(Exception excecao) =>
        excecao is PostgresException { SqlState: FalhaTravaIndisponivel }
        || excecao.InnerException is PostgresException { SqlState: FalhaTravaIndisponivel };
}
