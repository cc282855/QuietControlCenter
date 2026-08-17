using System.Buffers;
using System.Globalization;
using System.Text.Json;

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
    private static readonly string[] ShareUriPrefixes =
    [
        "vmess://", "ss://", "socks://", "socks4://", "socks5://", "trojan://", "vless://",
        "hysteria2://", "hy2://", "hysteria2+realm://", "hysteria2+realm+http://", "tuic://",
        "wireguard://", "anytls://", "naive://", "naive+https://", "naive+quic://"
    ];

    [GeneratedRegex(@"\A(?:\u5269\u4F59\u6D41\u91CF|\u5269\u4F59\u6D41\u91CF\s*remaining|Remaining\s+(?:Traffic|Flow))\s*[:\uFF1A]\s*(?<value>[0-9]+(?:\.[0-9]{1,3})?)\s*(?<unit>B|KB|MB|GB|TB|KiB|MiB|GiB|TiB)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
    private static partial Regex RemainingMarkerRegex();

    [GeneratedRegex(@"\A(?:\u5230\u671F\u65F6\u95F4|\u8FC7\u671F\u65F6\u95F4|\u6709\u6548\u671F\u81F3|Expiry|Expiration\s+Date|Expires)\s*[:\uFF1A]\s*(?<date>[0-9]{4}-[0-9]{2}-[0-9]{2}(?:[ T][0-9]{2}:[0-9]{2}:[0-9]{2})?)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, 100)]
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
        if (body.Length > MaxBodyBytes) return new(SubscriptionQuotaStatusCode.BodyTooLarge);
        if (body.IsEmpty) return new(SubscriptionQuotaStatusCode.Unsupported);

        string text;
        try
        {
            text = StrictUtf8.GetString(body.Span);
        }
        catch (DecoderFallbackException)
        {
            return new(SubscriptionQuotaStatusCode.Malformed);
        }

        var candidates = new List<string>(2) { text };
        if (TryDecodeBase64(text, out var decoded)) candidates.Add(decoded);

        var markers = new MarkerAccumulator();
        foreach (var candidate in candidates) ReadMarkers(candidate, markers);

        if (markers.IsMalformed) return new(SubscriptionQuotaStatusCode.Malformed);
        if (!markers.Remaining.HasValue) return new(SubscriptionQuotaStatusCode.Unsupported);
        return new(SubscriptionQuotaStatusCode.Success,
            new(0, 0, null, markers.Remaining.Value, markers.Expiry, retrievedAtUtc, SubscriptionQuotaSource.ResponseBody));
    }

    public static SubscriptionQuotaResult ParseOfficialBody(ReadOnlyMemory<byte> body, DateTimeOffset retrievedAtUtc)
    {
        if (body.Length > MaxBodyBytes) return new(SubscriptionQuotaStatusCode.BodyTooLarge);
        return new(SubscriptionQuotaStatusCode.Unsupported);
    }

    private static void ReadMarkers(string text, MarkerAccumulator markers)
    {
        var start = 0;
        var lines = 0;
        while (start <= text.Length)
        {
            if (++lines > MaxMarkerLines)
            {
                markers.IsMalformed = true;
                return;
            }
            var end = text.IndexOfAny(['\r', '\n'], start);
            if (end < 0) end = text.Length;
            var length = end - start;
            if (length is > 0 and <= MaxMarkerLineCharacters)
            {
                var line = text.Substring(start, length).Trim();
                ReadMarkerLine(line, markers);
                ReadShareUri(line, markers);
            }
            else if (length > MaxMarkerLineCharacters
                && text.AsSpan(start, length).TrimStart().StartsWith("vmess://", StringComparison.Ordinal))
            {
                markers.IsMalformed = true;
            }
            if (end == text.Length) break;
            start = end + 1;
            if (start < text.Length && text[end] == '\r' && text[start] == '\n') start++;
        }
    }

    private static void ReadShareUri(string line, MarkerAccumulator markers)
    {
        if (!ShareUriPrefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal))) return;

        if (line.StartsWith("vmess://", StringComparison.Ordinal))
        {
            if (line.IndexOf('#') >= 0)
            {
                markers.IsMalformed = true;
                return;
            }
            ReadVmessPs(line["vmess://".Length..], markers);
            return;
        }

        var fragment = line.IndexOf('#');
        if (fragment >= 0)
        {
            if (fragment == line.Length - 1 || fragment != line.LastIndexOf('#')
                || !TryUrlDecode(line[(fragment + 1)..], out var decodedFragment))
            {
                markers.IsMalformed = true;
                return;
            }
            ReadMarkerLine(decodedFragment, markers);
        }
    }

    private static void ReadVmessPs(string payload, MarkerAccumulator markers)
    {
        if (payload.Length is 0 or > MaxMarkerLineCharacters
            || !TryDecodeBase64Bytes(payload, false, MaxMarkerLineCharacters, out var jsonBytes))
        {
            markers.IsMalformed = true;
            return;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(jsonBytes);
            using var document = JsonDocument.Parse(jsonBytes, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 8
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                markers.IsMalformed = true;
                return;
            }

            JsonElement ps = default;
            var psCount = 0;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("ps"))
                {
                    ps = property.Value;
                    psCount++;
                }
                if (ContainsNestedPs(property.Value))
                {
                    markers.IsMalformed = true;
                    return;
                }
            }
            if (psCount == 0)
            {
                return;
            }
            if (psCount != 1 || ps.ValueKind != JsonValueKind.String)
            {
                markers.IsMalformed = true;
                return;
            }
            var marker = ps.GetString();
            if (marker is null || marker.Length > MaxMarkerLineCharacters)
            {
                markers.IsMalformed = true;
                return;
            }
            ReadMarkerLine(marker, markers);
        }
        catch (Exception ex) when (ex is DecoderFallbackException or JsonException)
        {
            markers.IsMalformed = true;
        }
    }

    private static bool ContainsNestedPs(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject())
                if (property.NameEquals("ps") || ContainsNestedPs(property.Value)) return true;
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
                if (ContainsNestedPs(item)) return true;
        }
        return false;
    }

    private static void ReadMarkerLine(string line, MarkerAccumulator markers)
    {
        if (!TryNormalizeMarker(line, out var normalized)) return;

        var remainingMatch = RemainingMarkerRegex().Match(normalized);
        if (remainingMatch.Success)
        {
            if (TryTrafficBytes(remainingMatch.Groups["value"].Value, remainingMatch.Groups["unit"].Value, out var bytes))
                markers.AddRemaining(bytes);
            else
                markers.IsMalformed = true;
        }

        var expiryMatch = ExpiryMarkerRegex().Match(normalized);
        if (expiryMatch.Success)
        {
            if (TryExpiry(expiryMatch.Groups["date"].Value, out var parsed))
                markers.AddExpiry(parsed);
            else
                markers.IsMalformed = true;
        }
    }

    private static bool TryNormalizeMarker(string line, out string normalized)
    {
        normalized = line.Trim();
        if (normalized.Length == 0) return false;

        var index = 0;
        var decorations = 0;
        while (index < normalized.Length
            && Rune.DecodeFromUtf16(normalized.AsSpan(index), out var rune, out var consumed) == OperationStatus.Done
            && IsAllowedDecoration(rune))
        {
            if (++decorations > 2) return false;
            index += consumed;
            if (index < normalized.Length
                && Rune.DecodeFromUtf16(normalized.AsSpan(index), out rune, out consumed) == OperationStatus.Done
                && rune.Value is 0xFE0E or 0xFE0F)
                index += consumed;
            while (index < normalized.Length
                && Rune.DecodeFromUtf16(normalized.AsSpan(index), out rune, out consumed) == OperationStatus.Done
                && Rune.IsWhiteSpace(rune))
                index += consumed;
        }

        if (decorations > 0) normalized = normalized[index..];
        return normalized.Length > 0;
    }

    private static bool IsAllowedDecoration(Rune rune) => rune.Value is 0x1F527 or 0x2692 or 0x1F6E0;

    private static bool TryTrafficBytes(string valueText, string unit, out ulong bytes)
    {
        bytes = 0;
        if (!decimal.TryParse(valueText, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)) return false;
        var power = unit.ToUpperInvariant() switch
        {
            "B" => 0,
            "KB" or "KIB" => 1,
            "MB" or "MIB" => 2,
            "GB" or "GIB" => 3,
            "TB" or "TIB" => 4,
            _ => -1
        };
        if (power < 0) return false;
        decimal multiplier = 1;
        for (var i = 0; i < power; i++) multiplier *= 1024;
        var result = value * multiplier;
        if (result < 0 || result > ulong.MaxValue) return false;
        bytes = (ulong)decimal.Truncate(result);
        return true;
    }

    private static bool TryExpiry(string value, out DateTimeOffset expiry)
    {
        var formats = new[] { "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss" };
        if (!DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out expiry)) return false;
        return expiry >= MinimumExpiry && expiry <= MaximumExpiry;
    }

    private static bool TryDecodeBase64(string text, out string decoded)
    {
        decoded = string.Empty;
        if (!TryDecodeBase64Bytes(text, true, MaxBodyBytes, out var bytes)) return false;
        try
        {
            decoded = StrictUtf8.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static bool TryDecodeBase64Bytes(string text, bool allowLineBreaks, int maxEncodedCharacters, out byte[] bytes)
    {
        bytes = [];
        if (text.Length is 0 || text.Length > maxEncodedCharacters) return false;

        var compact = allowLineBreaks ? text.Replace("\r", string.Empty).Replace("\n", string.Empty) : text;
        if (compact.Length == 0 || compact.Any(c => c > 0x7F || char.IsWhiteSpace(c))) return false;

        var hasStandardAlphabet = compact.IndexOfAny(['+', '/']) >= 0;
        var hasUrlAlphabet = compact.IndexOfAny(['-', '_']) >= 0;
        if (hasStandardAlphabet && hasUrlAlphabet) return false;

        var paddingIndex = compact.IndexOf('=');
        var hasPadding = paddingIndex >= 0;
        if (hasPadding)
        {
            var paddingCount = compact.Length - paddingIndex;
            if (paddingCount is < 1 or > 2 || compact.AsSpan(paddingIndex).IndexOfAnyExcept('=') >= 0
                || compact.Length % 4 != 0)
                return false;
        }

        var normalized = compact.Replace('-', '+').Replace('_', '/');
        normalized = (normalized.Length % 4) switch
        {
            0 => normalized,
            2 => normalized + "==",
            3 => normalized + "=",
            _ => string.Empty
        };
        if (normalized.Length == 0) return false;
        try
        {
            bytes = Convert.FromBase64String(normalized);
            if (bytes.Length > MaxBodyBytes) return false;

            var canonical = Convert.ToBase64String(bytes);
            if (hasUrlAlphabet) canonical = canonical.Replace('+', '-').Replace('/', '_');
            if (!hasPadding) canonical = canonical.TrimEnd('=');
            return string.Equals(canonical, compact, StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            bytes = [];
            return false;
        }
    }

    private static bool TryUrlDecode(string text, out string decoded)
    {
        decoded = string.Empty;
        if (text.Length > MaxBodyBytes) return false;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '%') continue;
            if (i + 2 >= text.Length || !Uri.IsHexDigit(text[i + 1]) || !Uri.IsHexDigit(text[i + 2])) return false;
            i += 2;
        }
        try
        {
            decoded = Uri.UnescapeDataString(text);
            return decoded.Length <= MaxBodyBytes && decoded.IndexOf('\uFFFD') < 0;
        }
        catch (UriFormatException)
        {
            return false;
        }
    }

    private sealed class MarkerAccumulator
    {
        public ulong? Remaining { get; private set; }
        public DateTimeOffset? Expiry { get; private set; }
        public bool IsMalformed { get; set; }

        public void AddRemaining(ulong value)
        {
            if (Remaining.HasValue && Remaining.Value != value) IsMalformed = true;
            else Remaining = value;
        }

        public void AddExpiry(DateTimeOffset value)
        {
            if (Expiry.HasValue && Expiry.Value != value) IsMalformed = true;
            else Expiry = value;
        }
    }
}
