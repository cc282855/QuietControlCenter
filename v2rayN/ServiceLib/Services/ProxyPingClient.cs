namespace ServiceLib.Services;

public sealed class ProxyPingClient : IConnectionQualityProbe, IDisposable
{
    private readonly object _sync = new();
    private readonly Func<int> _portProvider;
    private readonly Func<string> _urlProvider;
    private readonly Func<int, HttpMessageHandler> _handlerFactory;
    private HttpClient? _client;
    private int _clientPort = -1;
    private bool _disposed;

    public ProxyPingClient(
        Func<int>? portProvider = null,
        Func<string>? urlProvider = null,
        Func<int, HttpMessageHandler>? handlerFactory = null)
    {
        _portProvider = portProvider ?? (() => AppManager.Instance.GetLocalPort(EInboundProtocol.socks));
        _urlProvider = urlProvider ?? (() => AppManager.Instance.Config.SpeedTestItem.SpeedPingTestUrl);
        _handlerFactory = handlerFactory ?? CreateHandler;
    }

    public async Task<int> ProbeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var client = GetClient();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMilliseconds(900));
            var timer = Stopwatch.StartNew();
            using var response = await client.GetAsync(
                _urlProvider(),
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);
            timer.Stop();
            return (int)Math.Max(1, timer.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return -1;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            _client?.Dispose();
            _client = null;
        }
    }

    private HttpClient GetClient()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            var port = _portProvider();
            if (_client is not null && port == _clientPort)
            {
                return _client;
            }

            _client?.Dispose();
            _client = new HttpClient(_handlerFactory(port), true);
            _clientPort = port;
            return _client;
        }
    }

    private static HttpMessageHandler CreateHandler(int port) => new SocketsHttpHandler
    {
        Proxy = new WebProxy($"socks5://{Global.Loopback}:{port}"),
        UseProxy = true,
        ConnectTimeout = TimeSpan.FromMilliseconds(900),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
    };
}
