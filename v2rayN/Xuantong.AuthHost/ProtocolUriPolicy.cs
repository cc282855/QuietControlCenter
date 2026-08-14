namespace Xuantong.AuthHost;

internal static class UriPolicy
{
    public static bool TryNormalize(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048
            || !Uri.TryCreate(value, UriKind.Absolute, out uri)
            || uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)) return false;
        var host = uri.IdnHost.TrimEnd('.');
        return !uri.IsLoopback
               && !host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               && !host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
               && (!System.Net.IPAddress.TryParse(host, out var ip) || IsGlobalLiteral(ip));
    }

    public static string? Origin(string? value)
    {
        if (!TryNormalize(value, out var uri)) return null;
        return new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost)
        { Port = uri.IsDefaultPort ? -1 : uri.Port, Path = "/", Query = string.Empty, Fragment = string.Empty }.Uri.AbsoluteUri;
    }

    public static bool IsExactOrigin(string? value, string? origin)
        => Origin(value) is { } left && Origin(origin) is { } right && string.Equals(left, right, StringComparison.Ordinal);

    private static bool IsGlobalLiteral(System.Net.IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] != 0 && bytes[0] != 10 && bytes[0] != 127
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127) && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31) && !(bytes[0] == 192 && bytes[1] == 168)
                && !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2)
                && !(bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99) && !(bytes[0] == 198 && bytes[1] is 18 or 19)
                && !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113)
                && bytes[0] < 224;
        var global = (bytes[0] & 0xE0) == 0x20;
        var docs = bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8;
        return address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6
            && !System.Net.IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal && !address.IsIPv6Multicast
            && !address.IsIPv6SiteLocal && global && !docs;
    }
}

internal static class BrowserSecurityOptions
{
    public static string BuildProxyArguments(int socksPort)
    {
        if (!AuthBounds.IsValidProxyPort(socksPort)) throw new ArgumentOutOfRangeException(nameof(socksPort));
        return $"--proxy-server=\"socks5://127.0.0.1:{socksPort}\" "
               + "--proxy-bypass-list=\"<-loopback>\" "
               + "--force-webrtc-ip-handling-policy=disable_non_proxied_udp "
               + "--disable-features=msWebOOUI,msPdfOOUI";
    }
}
