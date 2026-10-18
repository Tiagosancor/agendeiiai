using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Plataforma.Aplicacao.WhatsAppAgente;
using Plataforma.Infraestrutura.Opcoes;
using StackExchange.Redis;

namespace Plataforma.Infraestrutura.WhatsAppAgente;

/// <summary>
/// <c>SET wa:msg:{instância}:{id} NX EX 600</c>. Redis não configurado ou fora do ar → devolve <c>true</c> (segue):
/// o índice único (instância, id externo) do banco continua barrando a duplicata. Depois de uma falha, o Redis é
/// pulado por <see cref="PausaAposFalha"/> para o webhook não esperar o timeout a cada mensagem.
/// </summary>
public sealed class DeduplicadorMensagensRedis : IDeduplicadorMensagens, IAsyncDisposable
{
    public static readonly TimeSpan Validade = TimeSpan.FromSeconds(600);
    public static readonly TimeSpan PausaAposFalha = TimeSpan.FromSeconds(30);

    private readonly OpcoesRedis _opcoes;
    private readonly ILogger<DeduplicadorMensagensRedis> _logger;
    private readonly SemaphoreSlim _trava = new(1, 1);
    private ConnectionMultiplexer? _conexao;
    private DateTimeOffset _pularAte = DateTimeOffset.MinValue;

    public DeduplicadorMensagensRedis(IOptions<OpcoesRedis> opcoes, ILogger<DeduplicadorMensagensRedis> logger)
    {
        _opcoes = opcoes.Value;
        _logger = logger;
    }

    public async Task<bool> RegistrarSeNovaAsync(string nomeInstancia, string idMensagem, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_opcoes.ConnectionString) || DateTimeOffset.UtcNow < _pularAte)
            return true;

        try
        {
            var banco = (await ObterConexaoAsync()).GetDatabase();
            return await banco.StringSetAsync($"wa:msg:{nomeInstancia}:{idMensagem}", "1", Validade, When.NotExists);
        }
        catch (Exception excecao) when (excecao is RedisException or TimeoutException or ArgumentException)
        {
            _pularAte = DateTimeOffset.UtcNow + PausaAposFalha;
            _logger.LogWarning("Redis indisponível para deduplicação ({Tipo}); seguindo só com o índice do banco por {Segundos} s.",
                excecao.GetType().Name, (int)PausaAposFalha.TotalSeconds);
            return true;
        }
    }

    private async Task<ConnectionMultiplexer> ObterConexaoAsync()
    {
        if (_conexao is not null)
            return _conexao;

        await _trava.WaitAsync();
        try
        {
            if (_conexao is not null)
                return _conexao;

            var configuracao = ConfigurationOptions.Parse(_opcoes.ConnectionString!);
            configuracao.AbortOnConnectFail = false;
            configuracao.ConnectTimeout = 1000;
            configuracao.SyncTimeout = 1000;
            configuracao.AsyncTimeout = 1000;

            _conexao = await ConnectionMultiplexer.ConnectAsync(configuracao);
            return _conexao;
        }
        finally
        {
            _trava.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_conexao is not null)
            await _conexao.DisposeAsync();

        _trava.Dispose();
    }
}
