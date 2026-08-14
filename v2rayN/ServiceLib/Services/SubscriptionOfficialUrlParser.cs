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
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || uri.Host.IsNullOrEmpty()
            || uri.UserInfo.IsNotEmpty())
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    [GeneratedRegex(
        "(?im)^\\s*[\\\"']?(?:profile-web-page-url|profile-web-page|homepage|official-url)[\\\"']?\\s*[:=]\\s*[\\\"']?(?<url>https?://[^\\s\\\"'<>]+)",
        RegexOptions.CultureInvariant)]
    private static partial Regex OfficialUrlMetadataRegex();
}
