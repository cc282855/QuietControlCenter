using System.Text.RegularExpressions;

namespace ServiceLib.Services;

public static partial class SubscriptionOfficialUrlParser
{
    private const int MaxOfficialUrlLength = 2048;
    private const int MaxMetadataCharacters = 64 * 1024;

    public static string? Detect(IEnumerable<string>? headerValues, string? content)
    {
        if (headerValues is not null)
        {
            foreach (var value in headerValues.Take(2))
            {
                var normalized = Normalize(value);
                if (normalized is not null)
                {
                    return normalized;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var bounded = content.Length > MaxMetadataCharacters
            ? content[..MaxMetadataCharacters]
            : content;
        var match = OfficialUrlMetadataRegex().Match(bounded);
        return match.Success ? Normalize(match.Groups["url"].Value) : null;
    }

    public static string? Normalize(string? value)
    {
        var candidate = value?.Trim().Trim('"', '\'', ' ');
        if (candidate.IsNullOrEmpty()
            || candidate!.Length > MaxOfficialUrlLength
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.Host.IsNullOrEmpty()
            || uri.UserInfo.IsNotEmpty()
            || !IsPublicDestination(uri))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    public static string? GetCanonicalOrigin(string? value)
    {
        var normalized = Normalize(value);
        if (normalized is null || !Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var builder = new UriBuilder(Uri.UriSchemeHttps, uri.IdnHost)
        {
            Port = uri.IsDefaultPort ? -1 : uri.Port,
            Path = "/",
            Query = string.Empty,
            Fragment = string.Empty
        };
        return builder.Uri.AbsoluteUri;
    }

    public static bool HasExactOrigin(string? candidate, string? pinnedOrigin)
        => GetCanonicalOrigin(candidate) is { } candidateOrigin
           && GetCanonicalOrigin(pinnedOrigin) is { } expectedOrigin
           && string.Equals(candidateOrigin, expectedOrigin, StringComparison.Ordinal);

    private static bool IsPublicDestination(Uri uri)
    {
        var host = uri.IdnHost.TrimEnd('.');
        if (uri.IsLoopback
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (!IPAddress.TryParse(host, out var address))
        {
            return true;
        }
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

    [GeneratedRegex(
        "(?im)^\\s*[\\\"']?(?:profile-web-page-url|profile-web-page|homepage|official-url)[\\\"']?\\s*[:=]\\s*[\\\"']?(?<url>https?://[^\\s\\\"'<>]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex OfficialUrlMetadataRegex();
}
