using System.Globalization;
using Xunit;

namespace ServiceLib.Tests.Services;

public sealed class SubscriptionQuotaParserTests
{
    private static readonly DateTimeOffset RetrievedAt = new(2026, 8, 4, 1, 2, 3, TimeSpan.Zero);

    [Fact]
    public void Header_ParsesStandardFieldsAndComputesRemainingWithoutOverflow()
    {
        var expiry = new DateTimeOffset(2027, 1, 2, 0, 0, 0, TimeSpan.Zero).ToUnixTimeSeconds();
        var result = SubscriptionQuotaParser.ParseHeader(
            $"upload=100; download=200; total=1000; expire={expiry}", RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(700UL, result.Snapshot!.RemainingBytes);
        Assert.Equal(300UL, result.Snapshot.UploadBytes + result.Snapshot.DownloadBytes);
        Assert.Equal(1000UL, result.Snapshot.TotalBytes);
        Assert.Equal(SubscriptionQuotaSource.Header, result.Snapshot.Source);
        Assert.Equal(RetrievedAt, result.Snapshot.RetrievedAtUtc);
    }

    [Theory]
    [InlineData("upload=18446744073709551615; download=1; total=18446744073709551615")]
    [InlineData("upload=1; upload=2; download=1; total=10")]
    [InlineData("upload=-1; download=1; total=10")]
    [InlineData("upload=1; download=1; total=10; expire=999999999999")]
    [InlineData("upload=1; download=1; total=10; vendor=2")]
    public void Header_RejectsMalformedDuplicateUnsupportedAndOverflowValues(string header)
    {
        var result = SubscriptionQuotaParser.ParseHeader(header, RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Header_ReportsZeroTotalAsUnsupported()
    {
        var result = SubscriptionQuotaParser.ParseHeader("upload=0; download=0; total=0", RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Header_EnforcesCharacterAndFieldBounds()
    {
        var tooLong = new string('1', SubscriptionQuotaParser.MaxHeaderCharacters + 1);
        var tooMany = string.Join(';', Enumerable.Range(0, SubscriptionQuotaParser.MaxHeaderFields + 1).Select(i => $"x{i}=1"));

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, SubscriptionQuotaParser.ParseHeader(tooLong, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, SubscriptionQuotaParser.ParseHeader(tooMany, RetrievedAt).Status);
    }

    [Fact]
    public void Body_ParsesBase64ChineseMarkers()
    {
        var raw = "vmess://synthetic-node\n剩余流量：12.5 GB\n到期时间：2027-03-04";
        var encoded = Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(raw)));

        var result = SubscriptionQuotaParser.ParseBody(encoded, RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(12UL * 1024 * 1024 * 1024 + 512UL * 1024 * 1024, result.Snapshot!.RemainingBytes);
        Assert.Null(result.Snapshot.TotalBytes);
        Assert.Equal(new DateTimeOffset(2027, 3, 4, 0, 0, 0, TimeSpan.Zero), result.Snapshot.ExpiresAtUtc);
        Assert.Equal(SubscriptionQuotaSource.ResponseBody, result.Snapshot.Source);
    }

    [Fact]
    public void Body_ParsesUnpaddedUrlSafeBase64WithStrictUtf8()
    {
        const string raw = "剩余流量：1 GB\n到期时间：2028-01-01\nÿ";
        var urlSafe = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        Assert.Contains('_', urlSafe);

        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(urlSafe), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(1024UL * 1024 * 1024, result.Snapshot!.RemainingBytes);
        Assert.Equal(new DateTimeOffset(2028, 1, 1, 0, 0, 0, TimeSpan.Zero), result.Snapshot.ExpiresAtUtc);
    }

    [Fact]
    public void Body_RejectsImpossibleBase64Length()
    {
        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes("A"), RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported, result.Status);
    }

    [Fact]
    public void Body_ParsesUrlEscapedEnglishMarkerFragmentsWithoutFollowingTokens()
    {
        var raw = "vless://not-a-real-endpoint#Remaining%20Traffic%3A%202048%20MB\n"
                  + "trojan://not-a-real-endpoint#Expiration%20Date%3A%202025-01-02";

        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(raw), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(2048UL * 1024 * 1024, result.Snapshot!.RemainingBytes);
        Assert.True(result.Snapshot.ExpiresAtUtc < RetrievedAt);
    }

    [Fact]
    public void Body_RequiresExactMarkerPatternsAndNeverReturnsUrlTokens()
    {
        const string secretToken = "token-that-must-not-escape";
        var raw = $"https://example.invalid/sub?auth={secretToken}\nThere may be Remaining Traffic: 1 GB later";

        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(raw), RetrievedAt);
        var fixedMessage = SubscriptionQuotaService.GetFixedChineseMessage(result.Status);

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported, result.Status);
        Assert.Null(result.Snapshot);
        Assert.DoesNotContain(secretToken, fixedMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("http", fixedMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Body_RejectsStrictCapAndInvalidUtf8()
    {
        var oversized = new byte[SubscriptionQuotaParser.MaxBodyBytes + 1];
        var invalidUtf8 = new byte[] { 0xC3, 0x28 };

        Assert.Equal(SubscriptionQuotaStatusCode.BodyTooLarge, SubscriptionQuotaParser.ParseBody(oversized, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, SubscriptionQuotaParser.ParseBody(invalidUtf8, RetrievedAt).Status);
    }

    [Fact]
    public void Header_UsesInvariantUnsignedNumbers()
    {
        using var scope = new CultureScope("ar-SA");

        var result = SubscriptionQuotaParser.ParseHeader("upload=1; download=2; total=10", RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(7UL, result.Snapshot!.RemainingBytes);
    }

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentCulture;

        public CultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => CultureInfo.CurrentCulture = _original;
    }
}
