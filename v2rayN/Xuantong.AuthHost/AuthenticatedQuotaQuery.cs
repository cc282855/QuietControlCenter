using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;

namespace Xuantong.AuthHost;

internal static class AuthenticatedQuotaParser
{
    public static bool TryParseHeader(string? value, out AuthResponse response)
    {
        response = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 4096) return false;
        var values = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        var fields = value.Split(';', StringSplitOptions.TrimEntries);
        if (fields.Length is 0 or > 16) return false;
        foreach (var field in fields)
        {
            var parts = field.Split('=', StringSplitOptions.TrimEntries);
            var key = parts.Length == 2 ? parts[0].ToLowerInvariant() : string.Empty;
            if (parts.Length != 2 || key is not ("upload" or "download" or "total" or "expire")
                || !ulong.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var number)
                || !values.TryAdd(key, number)) return false;
        }
        if (!values.TryGetValue("upload", out var upload) || !values.TryGetValue("download", out var download)
            || !values.TryGetValue("total", out var total) || total == 0 || upload > total || download > total - upload) return false;
        long? expires = values.TryGetValue("expire", out var expiry) && expiry <= long.MaxValue ? (long)expiry : null;
        response = new("Success", upload, download, total, total - upload - download, expires);
        return true;
    }
}

internal static class AuthenticatedQuotaQuery
{
    public static async Task<AuthResponse> QueryAsync(AuthTicket ticket)
    {
        if (!await HostSecurity.IsLoopbackSocksAvailableAsync(ticket.SocksPort)) return new("ProxyUnavailable");
        var cookies = await SessionStore.GetExactHostCookieHeaderAsync(ticket);
        if (cookies is null) return new("LoginRequired");
        try
        {
            using var handler = new SocketsHttpHandler
            {
                AllowAutoRedirect = false, UseProxy = true,
                Proxy = new SocksProxy(ticket.SocksPort), ConnectTimeout = TimeSpan.FromSeconds(5)
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, ticket.OfficialUrl);
            request.Headers.TryAddWithoutValidation("Cookie", cookies);
            request.Headers.AcceptEncoding.ParseAdd("identity");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden
                || (int)response.StatusCode is >= 300 and < 400) return new("LoginRequired");
            if (!response.IsSuccessStatusCode) return new("NetworkError");
            if (response.Headers.TryGetValues("Subscription-Userinfo", out var headers))
            {
                var values = headers.Take(2).ToArray();
                if (values.Length == 1 && AuthenticatedQuotaParser.TryParseHeader(values[0], out var parsed)) return parsed;
            }
            return new("AuthenticatedUnsupported");
        }
        catch { return new("NetworkError"); }
    }

    private sealed class SocksProxy(int port) : IWebProxy
    {
        private readonly Uri _proxy = new($"socks5://127.0.0.1:{port}");
        public ICredentials? Credentials { get; set; }
        public Uri GetProxy(Uri destination) => _proxy;
        public bool IsBypassed(Uri host) => false;
    }
}
