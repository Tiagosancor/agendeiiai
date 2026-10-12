using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Plataforma.Testes.Integracao.Infraestrutura;

/// <summary>
/// Proxy TCP entre a aplicação e o PostgreSQL que reproduz o COMMIT ambíguo de verdade: repassa o pedido ao servidor (que
/// executa e confirma) e derruba a conexão <b>antes</b> de a resposta voltar. A armação é por conexão, em dois estágios: o
/// <c>primeiro</c> padrão marca a conexão que está fazendo a operação testada (sem isso o <c>COMMIT</c> de qualquer outra
/// conexão, como o do Hangfire, consumiria a falha); o <c>depois</c> é o pedido cuja resposta se perde nessa conexão.
/// </summary>
public sealed class ProxyQuePerdeResposta : IDisposable
{
    private readonly TcpListener _escuta;
    private readonly string _host;
    private readonly int _porta;
    private readonly CancellationTokenSource _fim = new();
    private readonly object _trava = new();
    private string? _primeiro;
    private string? _depois;
    private int _derrubadas;

    public ProxyQuePerdeResposta(string host, int porta)
    {
        _host = host;
        _porta = porta;
        _escuta = new TcpListener(IPAddress.Loopback, 0);
        _escuta.Start();
        Porta = ((IPEndPoint)_escuta.LocalEndpoint).Port;
        _ = Task.Run(AceitarAsync);
    }

    public int Porta { get; }

    public int Derrubadas => Volatile.Read(ref _derrubadas);

    /// <summary>
    /// A próxima conexão que enviar algo com <paramref name="primeiro"/> passa a ser a alvo; se <paramref name="depois"/> for
    /// nulo, a resposta a esse mesmo pedido é a descartada (INSERT avulso); senão, a do pedido seguinte que o contiver.
    /// </summary>
    public void Armar(string primeiro, string? depois = null)
    {
        lock (_trava)
        {
            (_primeiro, _depois) = (primeiro, depois);
            _derrubadas = 0;
        }
    }

    private async Task AceitarAsync()
    {
        try
        {
            while (!_fim.IsCancellationRequested)
            {
                var cliente = await _escuta.AcceptTcpClientAsync(_fim.Token);
                _ = Task.Run(() => AtenderAsync(cliente));
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task AtenderAsync(TcpClient cliente)
    {
        using var aplicacao = cliente;
        using var servidor = new TcpClient();
        try
        {
            await servidor.ConnectAsync(_host, _porta);
        }
        catch
        {
            return;
        }

        var alvo = false;
        var descartar = false;

        var deIda = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            var origem = aplicacao.GetStream();
            var destino = servidor.GetStream();
            try
            {
                int n;
                while ((n = await origem.ReadAsync(buffer)) > 0)
                {
                    var texto = Encoding.UTF8.GetString(buffer, 0, n);
                    lock (_trava)
                    {
                        if (!alvo && _primeiro is not null && texto.Contains(_primeiro, StringComparison.Ordinal))
                        {
                            alvo = true;
                            if (_depois is null)
                                Volatile.Write(ref descartar, true);
                            _primeiro = null;
                        }
                        else if (alvo && !Volatile.Read(ref descartar) && _depois is not null && texto.Contains(_depois, StringComparison.Ordinal))
                        {
                            Volatile.Write(ref descartar, true);
                            _depois = null;
                        }
                    }

                    await destino.WriteAsync(buffer.AsMemory(0, n));
                }
            }
            catch
            {
            }
        });

        var deVolta = Task.Run(async () =>
        {
            var buffer = new byte[65536];
            var origem = servidor.GetStream();
            var destino = aplicacao.GetStream();
            try
            {
                int n;
                while ((n = await origem.ReadAsync(buffer)) > 0)
                {
                    if (Volatile.Read(ref descartar))
                    {
                        // O servidor já executou e confirmou; a resposta nunca chega ao aplicativo.
                        Interlocked.Increment(ref _derrubadas);
                        return;
                    }

                    await destino.WriteAsync(buffer.AsMemory(0, n));
                }
            }
            catch
            {
            }
        });

        await Task.WhenAny(deIda, deVolta);
        servidor.Client.Close();
        aplicacao.Client.Close();
    }

    public void Dispose()
    {
        _fim.Cancel();
        _escuta.Stop();
    }
}
