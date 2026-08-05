namespace ServiceLib.Services;

public sealed class SubscriptionQuotaService
{
    public const int MaxUrlCharacters = 4096;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private readonly Func<int, CancellationToken, Task<bool>> _proxyAvailability;
    private readonly Func<int?, HttpMessageHandler> _handlerFactory;
    private readonly TimeProvider _timeProvider;

    public SubscriptionQuotaService(
        Func<int, CancellationToken, Task<bool>>? proxyAvailability = null,
        Func<int?, HttpMessageHandler>? handlerFactory = null,
        TimeProvider? timeProvider = null)
    {
        _proxyAvailability = proxyAvailability ?? IsLocalProxyAvailableAsync;
        _handlerFactory = handlerFactory ?? CreateHandler;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<SubscriptionQuotaResult> FetchAsync(
        string? url,
        bool useLocalSocksProxy,
        int localSocksPort,
        string? userAgent,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateUrl(url, out var uri))
        {
            return new(SubscriptionQuotaStatusCode.InvalidRequest);
        }
        if (useLocalSocksProxy && localSocksPort is <= 0 or > 65535)
        {
            return new(SubscriptionQuotaStatusCode.ProxyUnavailable);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(RequestTimeout);
        var checkingProxy = useLocalSocksProxy;
        try
        {
            if (useLocalSocksProxy
                && !await _proxyAvailability(localSocksPort, linked.Token).ConfigureAwait(false))
            {
                return new(SubscriptionQuotaStatusCode.ProxyUnavailable);
            }
            checkingProxy = false;
            using var handler = _handlerFactory(useLocalSocksProxy ? localSocksPort : null);
            using var client = new HttpClient(handler, false) { Timeout = Timeout.InfiniteTimeSpan };
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            ApplyRequestHeaders(request, userAgent);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                linked.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return new(SubscriptionQuotaStatusCode.HttpError);
            }

            var retrievedAt = _timeProvider.GetUtcNow();
            if (response.Headers.TryGetValues("Subscription-Userinfo", out var headerValues))
            {
                var values = headerValues.Take(2).ToArray();
                if (values.Length == 1)
                {
                    var headerResult = SubscriptionQuotaParser.ParseHeader(values[0], retrievedAt);
                    if (headerResult.IsSuccess)
                    {
                        return headerResult;
                    }
                }
            }

            if (response.Content.Headers.ContentLength > SubscriptionQuotaParser.MaxBodyBytes)
            {
                return new(SubscriptionQuotaStatusCode.BodyTooLarge);
            }
            var body = await ReadBodyBoundedAsync(response.Content, linked.Token).ConfigureAwait(false);
            return body is null
                ? new(SubscriptionQuotaStatusCode.BodyTooLarge)
                : SubscriptionQuotaParser.ParseBody(body, retrievedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(SubscriptionQuotaStatusCode.Cancelled);
        }
        catch (OperationCanceledException) when (checkingProxy)
        {
            return new(SubscriptionQuotaStatusCode.ProxyUnavailable);
        }
        catch (OperationCanceledException)
        {
            return new(SubscriptionQuotaStatusCode.NetworkError);
        }
        catch
        {
            return new(checkingProxy
                ? SubscriptionQuotaStatusCode.ProxyUnavailable
                : SubscriptionQuotaStatusCode.NetworkError);
        }
    }

    public static string GetFixedChineseMessage(SubscriptionQuotaStatusCode status) => status switch
    {
        SubscriptionQuotaStatusCode.Unsupported => "订阅未提供余量",
        SubscriptionQuotaStatusCode.Malformed => "订阅余量格式无效",
        SubscriptionQuotaStatusCode.BodyTooLarge => "订阅内容超出限制",
        SubscriptionQuotaStatusCode.InvalidRequest => "订阅地址不可用",
        SubscriptionQuotaStatusCode.ProxyUnavailable => "代理不可用",
        SubscriptionQuotaStatusCode.NetworkError => "余量查询失败",
        SubscriptionQuotaStatusCode.HttpError => "订阅服务响应异常",
        SubscriptionQuotaStatusCode.Cancelled => "余量查询已取消",
        _ => "订阅余量可用"
    };

    private static bool TryValidateUrl(string? url, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(url)
               && url.Length <= MaxUrlCharacters
               && Uri.TryCreate(url, UriKind.Absolute, out uri)
               && uri.Scheme == Uri.UriSchemeHttps
               && !string.IsNullOrEmpty(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo)
               && IsAllowedDestination(uri);
    }

    private static void ApplyRequestHeaders(HttpRequestMessage request, string? userAgent)
    {
        var selectedUserAgent = string.IsNullOrWhiteSpace(userAgent) ? Utils.GetVersion(false) : userAgent;
        if (selectedUserAgent.Length <= 512)
        {
            try
            {
                request.Headers.UserAgent.TryParseAdd(selectedUserAgent);
            }
            catch (FormatException)
            {
                // Invalid configured values are ignored and are never surfaced or logged.
            }
        }
        request.Headers.AcceptEncoding.ParseAdd("identity");
    }

    private static bool IsAllowedDestination(Uri uri)
    {
        var host = uri.IdnHost;
        if (host.EndsWith(".", StringComparison.Ordinal))
        {
            host = host[..^1];
        }
        if (uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        return !IPAddress.TryParse(host, out var address) || IsGloballyRoutableLiteral(address);
    }

    private static bool IsGloballyRoutableLiteral(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] != 0
                   && bytes[0] != 10
                   && bytes[0] != 127
                   && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                   && !(bytes[0] == 169 && bytes[1] == 254)
                   && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                   && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0)
                   && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2)
                   && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99)
                   && !(bytes[0] == 192 && bytes[1] == 168)
                   && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                   && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100)
                   && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                   && bytes[0] < 224;
        }
        if (address.AddressFamily != AddressFamily.InterNetworkV6
            || address.Equals(IPAddress.IPv6Any)
            || address.Equals(IPAddress.IPv6None)
            || IPAddress.IsLoopback(address)
            || address.IsIPv6LinkLocal
            || address.IsIPv6Multicast
            || address.IsIPv6SiteLocal)
        {
            return false;
        }
        var ipv6 = address.GetAddressBytes();
        var isGlobalUnicast = (ipv6[0] & 0xE0) == 0x20;
        var isDocumentation = ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0D && ipv6[3] == 0xB8;
        return isGlobalUnicast && !isDocumentation;
    }

    private static HttpMessageHandler CreateHandler(int? proxyPort)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            MaxResponseHeadersLength = 16,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            UseProxy = proxyPort.HasValue,
            Proxy = proxyPort.HasValue ? new ForcedLocalSocksProxy(proxyPort.Value) : null,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30)
        };
        var certificateChainPolicy = CertPemManager.Instance.BuildCertificateChainPolicy();
        if (certificateChainPolicy is not null)
        {
            handler.SslOptions.CertificateChainPolicy = certificateChainPolicy;
            handler.SslOptions.RemoteCertificateValidationCallback = null;
        }
        return handler;
    }

    private static async Task<bool> IsLocalProxyAvailableAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<byte[]?> ReadBodyBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return buffer.ToArray();
            }
            if (buffer.Length + read > SubscriptionQuotaParser.MaxBodyBytes)
            {
                return null;
            }
            buffer.Write(chunk, 0, read);
        }
    }

    private sealed class ForcedLocalSocksProxy(int port) : IWebProxy
    {
        private readonly Uri _proxy = new($"socks5://{Global.Loopback}:{port}");

        public ICredentials? Credentials { get; set; }

        public Uri GetProxy(Uri destination) => _proxy;

        public bool IsBypassed(Uri host) => false;
    }
}
