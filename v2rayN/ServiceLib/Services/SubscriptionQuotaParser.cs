using System.Globalization;

namespace ServiceLib.Services;

public static partial class SubscriptionQuotaParser
{
    public const int MaxHeaderCharacters = 4096;
    public const int MaxHeaderFields = 16;
    public const int MaxBodyBytes = 8 * 1024 * 1024;

    private const int MaxMarkerLines = 65_536;
    private const int MaxMarkerLineCharacters = 2048;
    private static readonly DateTimeOffset MinimumExpiry = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset MaximumExpiry = new(2100, 12, 31, 23, 59, 59, TimeSpan.Zero);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    [GeneratedRegex(@"^(?:剩余流量|剩余流量\s*remaining|Remaining\s+(?:Traffic|Flow))\s*[:：]\s*(?<value>[0-9]+(?:\.[0-9]{1,3})?)\s*(?<unit>B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex RemainingMarkerRegex();

    [GeneratedRegex(@"^(?:到期时间|过期时间|有效期至|Expiry|Expiration\s+Date|Expires)\s*[:：]\s*(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[ T][0-9]{2}:[0-9]{2}:[0-9]{2})?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex ExpiryMarkerRegex();

    public static SubscriptionQuotaResult ParseHeader(string? header, DateTimeOffset retrievedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(header))
        {
            return new(SubscriptionQuotaStatusCode.Unsupported);
        }
        if (header.Length > MaxHeaderCharacters)
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }

        var fields = header.Split(';', StringSplitOptions.TrimEntries);
        if (fields.Length is 0 or > MaxHeaderFields)
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }

        var values = new Dictionary<string, ulong>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields)
        {
            if (field.Length == 0)
            {
                return new(SubscriptionQuotaStatusCode.Malformed);
            }
            var separator = field.IndexOf('=');
            if (separator <= 0 || separator == field.Length - 1 || field.IndexOf('=', separator + 1) >= 0)
            {
                return new(SubscriptionQuotaStatusCode.Malformed);
            }
            var key = field[..separator].Trim().ToLowerInvariant();
            var rawValue = field[(separator + 1)..].Trim();
            if (key is not ("upload" or "download" or "total" or "expire")
                || values.ContainsKey(key)
                || !ulong.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return new(SubscriptionQuotaStatusCode.Malformed);
            }
            values.Add(key, value);
        }

        if (!values.TryGetValue("upload", out var upload)
            || !values.TryGetValue("download", out var download)
            || !values.TryGetValue("total", out var total))
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }
        if (total == 0)
        {
            return new(SubscriptionQuotaStatusCode.Unsupported);
        }
        if (upload > total
            || download > total - upload)
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }

        DateTimeOffset? expiry = null;
        if (values.TryGetValue("expire", out var expireSeconds) && expireSeconds != 0)
        {
            if (expireSeconds > (ulong)MaximumExpiry.ToUnixTimeSeconds())
            {
                return new(SubscriptionQuotaStatusCode.Malformed);
            }
            expiry = DateTimeOffset.FromUnixTimeSeconds((long)expireSeconds);
            if (expiry < MinimumExpiry)
            {
                return new(SubscriptionQuotaStatusCode.Malformed);
            }
        }

        return new(
            SubscriptionQuotaStatusCode.Success,
            new(upload, download, total, total - upload - download, expiry, retrievedAtUtc, SubscriptionQuotaSource.Header));
    }

    public static SubscriptionQuotaResult ParseBody(ReadOnlyMemory<byte> body, DateTimeOffset retrievedAtUtc)
    {
        if (body.Length > MaxBodyBytes)
        {
            return new(SubscriptionQuotaStatusCode.BodyTooLarge);
        }
        if (body.IsEmpty)
        {
            return new(SubscriptionQuotaStatusCode.Unsupported);
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(body.Span);
        }
        catch (DecoderFallbackException)
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }

        var candidates = new List<string>(3) { text };
        if (TryDecodeBase64(text, out var base64Decoded))
        {
            candidates.Add(base64Decoded);
        }
        if (text.IndexOf('%') >= 0 && TryUrlDecode(text, out var urlDecoded))
        {
            candidates.Add(urlDecoded);
        }

        ulong? remaining = null;
        DateTimeOffset? expiry = null;
        foreach (var candidate in candidates)
        {
            ReadMarkers(candidate, ref remaining, ref expiry);
            if (remaining.HasValue && expiry.HasValue)
            {
                break;
            }
        }

        if (!remaining.HasValue)
        {
            return new(SubscriptionQuotaStatusCode.Unsupported);
        }
        return new(
            SubscriptionQuotaStatusCode.Success,
            new(0, 0, null, remaining.Value, expiry, retrievedAtUtc, SubscriptionQuotaSource.ResponseBody));
    }

    private static void ReadMarkers(string text, ref ulong? remaining, ref DateTimeOffset? expiry)
    {
        var start = 0;
        var lines = 0;
        while (start <= text.Length && lines++ < MaxMarkerLines)
        {
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0)
            {
                end = text.Length;
            }
            var length = end - start;
            if (length is > 0 and <= MaxMarkerLineCharacters)
            {
                var line = text.Substring(start, length).Trim();
                ReadMarkerLine(line, ref remaining, ref expiry);
                var fragment = line.LastIndexOf('#');
                if (fragment >= 0 && fragment + 1 < line.Length
                    && TryUrlDecode(line[(fragment + 1)..], out var decodedFragment))
                {
                    ReadMarkerLine(decodedFragment.Trim(), ref remaining, ref expiry);
                }
            }
            if (end == text.Length)
            {
                break;
            }
            start = end + 1;
            if (start < text.Length && text[end] == '\r' && text[start] == '\n')
            {
                start++;
            }
        }
    }

    private static void ReadMarkerLine(string line, ref ulong? remaining, ref DateTimeOffset? expiry)
    {
        if (!remaining.HasValue)
        {
            var match = RemainingMarkerRegex().Match(line);
            if (match.Success && TryTrafficBytes(match.Groups["value"].Value, match.Groups["unit"].Value, out var bytes))
            {
                remaining = bytes;
            }
        }
        if (!expiry.HasValue)
        {
            var match = ExpiryMarkerRegex().Match(line);
            if (match.Success && TryExpiry(match.Groups["date"].Value, out var parsed))
            {
                expiry = parsed;
            }
        }
    }

    private static bool TryTrafficBytes(string valueText, string unit, out ulong bytes)
    {
        bytes = 0;
        if (!decimal.TryParse(valueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }
        var power = unit.ToUpperInvariant() switch
        {
            "B" => 0,
            "KB" or "KIB" => 1,
            "MB" or "MIB" => 2,
            "GB" or "GIB" => 3,
            "TB" or "TIB" => 4,
            _ => -1
        };
        if (power < 0)
        {
            return false;
        }
        decimal multiplier = 1;
        for (var i = 0; i < power; i++) multiplier *= 1024;
        var result = value * multiplier;
        if (result < 0 || result > ulong.MaxValue)
        {
            return false;
        }
        bytes = (ulong)decimal.Truncate(result);
        return true;
    }

    private static bool TryExpiry(string value, out DateTimeOffset expiry)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss" };
        if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out expiry))
        {
            return false;
        }
        return expiry >= MinimumExpiry && expiry <= MaximumExpiry;
    }

    private static bool TryDecodeBase64(string text, out string decoded)
    {
        decoded = string.Empty;
        var compact = string.Concat(text.Where(c => !char.IsWhiteSpace(c)))
            .Replace('-', '+')
            .Replace('_', '/');
        if (compact.Length is 0 or > MaxBodyBytes)
        {
            return false;
        }
        compact = (compact.Length % 4) switch
        {
            0 => compact,
            2 => compact + "==",
            3 => compact + "=",
            _ => string.Empty
        };
        if (compact.Length == 0)
        {
            return false;
        }
        try
        {
            var bytes = Convert.FromBase64String(compact);
            if (bytes.Length > MaxBodyBytes)
            {
                return false;
            }
            decoded = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryUrlDecode(string text, out string decoded)
    {
        decoded = string.Empty;
        if (text.Length > MaxBodyBytes)
        {
            return false;
        }
        try
        {
            decoded = Uri.UnescapeDataString(text);
            return decoded.Length <= MaxBodyBytes;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }
}
