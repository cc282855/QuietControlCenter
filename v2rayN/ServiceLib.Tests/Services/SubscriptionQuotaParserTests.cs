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
        var raw = "vless://synthetic-node\n剩余流量：12.5 GB\n到期时间：2027-03-04";
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

    [Fact]
    public void OfficialBody_RejectsJsonAndHtmlWithoutGuessing()
    {
        var exact = Encoding.UTF8.GetBytes("""
            {"data":{"transfer_enable":1000,"u":100,"d":200,"expired_at":1800000000}}
            """);
        var nestedDecoy = Encoding.UTF8.GetBytes("""
            {"unrelated":{"data":{"transfer_enable":1000,"u":100,"d":200}}}
            """);
        var html = Encoding.UTF8.GetBytes("<html>Remaining Traffic: 700 B</html>");

        var result = SubscriptionQuotaParser.ParseOfficialBody(exact, RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported, result.Status);
        Assert.Null(result.Snapshot);
        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseOfficialBody(nestedDecoy, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseOfficialBody(html, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.BodyTooLarge,
            SubscriptionQuotaParser.ParseOfficialBody(
                new byte[SubscriptionQuotaParser.MaxBodyBytes + 1], RetrievedAt).Status);
    }

    [Theory]
    [InlineData("剩余流量：215.67 GB")]
    [InlineData("🔧剩余流量：215.67 GB")]
    [InlineData("⚒️ 剩余流量：215.67 GB")]
    public void Body_ParsesQuotaFromOneDecodedVmessPs(string ps)
    {
        var json = JsonSerializer.Serialize(new { v = "2", ps, add = "synthetic.invalid", id = "synthetic-id" });
        var result = SubscriptionQuotaParser.ParseBody(EncodeOuter(Vmess(json)), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal((ulong)(215.67m * 1024 * 1024 * 1024), result.Snapshot!.RemainingBytes);
    }

    [Fact]
    public void Body_ParsesExactlyTwoAllowedDecorations()
    {
        var result = SubscriptionQuotaParser.ParseBody(
            Encoding.UTF8.GetBytes("🔧⚒️ 剩余流量：1 GB"), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(1024UL * 1024 * 1024, result.Snapshot!.RemainingBytes);
    }

    [Fact]
    public void Body_ParsesDecoratedEligibleShareFragmentOnce()
    {
        var marker = Uri.EscapeDataString("🔧 剩余流量：9 GB");
        var result = SubscriptionQuotaParser.ParseBody(
            Encoding.UTF8.GetBytes($"vless://synthetic.invalid#{marker}"), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(9UL * 1024 * 1024 * 1024, result.Snapshot!.RemainingBytes);
    }

    [Theory]
    [InlineData("notice 剩余流量：1 GB")]
    [InlineData("剩余流量：1 GB suffix")]
    [InlineData("🔧⚒️★剩余流量：1 GB")]
    [InlineData("!剩余流量：1 GB")]
    [InlineData("$剩余流量：1 GB")]
    [InlineData("+剩余流量：1 GB")]
    [InlineData("^剩余流量：1 GB")]
    [InlineData("©剩余流量：1 GB")]
    public void Body_RejectsPrefixesSuffixesAndThreeDecorations(string marker)
    {
        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(marker), RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Body_IgnoresOrdinaryVmessWithoutPsAndFindsLaterQuotaNode()
    {
        var ordinary = Vmess("{\"v\":\"2\",\"add\":\"normal.invalid\",\"id\":\"normal-id\"}");
        var quota = Vmess("{\"v\":\"2\",\"ps\":\"🔧剩余流量：7 GB\"}");

        var result = SubscriptionQuotaParser.ParseBody(EncodeOuter(ordinary + "\n" + quota), RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(7UL * 1024 * 1024 * 1024, result.Snapshot!.RemainingBytes);
    }

    [Theory]
    [InlineData("{\"meta\":{\"ps\":\"剩余流量：1 GB\"}}")]
    [InlineData("{\"ps\":\"剩余流量：1 GB\",\"ps\":\"剩余流量：1 GB\"}")]
    [InlineData("{\"ps\":123}")]
    [InlineData("{\"ps\":\"剩余流量：1 GB\",\"meta\":{\"ps\":\"剩余流量：1 GB\"}}")]
    [InlineData("[]")]
    public void Body_RejectsMissingDuplicateNonStringAndNestedVmessPs(string json)
    {
        var result = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(Vmess(json)), RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, result.Status);
        Assert.Null(result.Snapshot);
    }

    [Fact]
    public void Body_RejectsInvalidAndOversizedVmessPayloadsWithoutReadingPastBounds()
    {
        var invalidBase64 = SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes("vmess://%%%"), RetrievedAt);
        var invalidUtf8 = SubscriptionQuotaParser.ParseBody(
            Encoding.UTF8.GetBytes("vmess://" + Convert.ToBase64String([0xC3, 0x28])), RetrievedAt);
        var invalidJson = SubscriptionQuotaParser.ParseBody(
            Encoding.UTF8.GetBytes(Vmess("{\"ps\":\"剩余流量：1 GB\"} trailing")), RetrievedAt);
        var oversized = SubscriptionQuotaParser.ParseBody(
            Encoding.UTF8.GetBytes("vmess://" + new string('A', 4096)), RetrievedAt);

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, invalidBase64.Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, invalidUtf8.Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, invalidJson.Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, oversized.Status);
    }

    [Fact]
    public void Body_RejectsNonCanonicalVmessBase64AndUnicodeWhitespace()
    {
        var nonZeroPadBits = "vmess://eyJwcyI6IngifR";
        var misplacedPadding = "vmess://eyJw=cyI6IngifQ";
        var excessPadding = "vmess://eyJwcyI6IngifQ===";
        var mixedAlphabet = "vmess://AA+_";
        var unicodeWhitespace = "vmess://eyJwcyI6\u00A0IngifQ";

        foreach (var line in new[] { nonZeroPadBits, misplacedPadding, excessPadding, mixedAlphabet, unicodeWhitespace })
        {
            Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
                SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(line), RetrievedAt).Status);
        }
    }

    [Fact]
    public void Body_OuterBase64AllowsCrLfButRejectsUnicodeWhitespace()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("Remaining Traffic: 1 GB"));
        var wrapped = encoded.Insert(encoded.Length / 2, "\r\n");
        var unicodeWrapped = encoded.Insert(encoded.Length / 2, "\u00A0");

        Assert.True(SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(wrapped), RetrievedAt).IsSuccess);
        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(unicodeWrapped), RetrievedAt).Status);
    }

    [Fact]
    public void Body_MalformedVmessPoisonsMixedResponse()
    {
        var body = Encoding.UTF8.GetBytes("vmess://%%%\n剩余流量：2 GB");

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(body, RetrievedAt).Status);
    }

    [Fact]
    public void Body_DoesNotScanArbitraryJsonUnknownKeysOrDecodePsRecursively()
    {
        var arbitrary = Encoding.UTF8.GetBytes("{\"remaining\":1,\"unknown\":\"剩余流量：8 GB\"}");
        var nestedJson = JsonSerializer.Serialize(new { ps = "剩余流量：8 GB" });
        var recursive = JsonSerializer.Serialize(new { ps = Vmess(nestedJson) });

        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseBody(arbitrary, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseBody(Encoding.UTF8.GetBytes(Vmess(recursive)), RetrievedAt).Status);
    }

    [Fact]
    public void Body_MergesIdenticalMarkersAndRejectsConflicts()
    {
        var identical = Encoding.UTF8.GetBytes("剩余流量：2 GB\n剩余流量：2 GB\n到期时间：2028-01-01\n到期时间：2028-01-01");
        var remainingConflict = Encoding.UTF8.GetBytes("剩余流量：2 GB\n剩余流量：3 GB");
        var expiryConflict = Encoding.UTF8.GetBytes("剩余流量：2 GB\n到期时间：2028-01-01\n到期时间：2028-01-02");

        Assert.True(SubscriptionQuotaParser.ParseBody(identical, RetrievedAt).IsSuccess);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(remainingConflict, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(expiryConflict, RetrievedAt).Status);
    }

    [Fact]
    public void Body_NeverReturnsSyntheticCredentialsOrTokenText()
    {
        const string secret = "synthetic-private-token-must-not-escape";
        var json = JsonSerializer.Serialize(new { ps = "剩余流量：1 GB", id = secret, add = secret });
        var body = Encoding.UTF8.GetBytes(Vmess(json) + "\n剩余流量：2 GB");

        var result = SubscriptionQuotaParser.ParseBody(body, RetrievedAt);
        var observable = result + SubscriptionQuotaService.GetFixedChineseMessage(result.Status);

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed, result.Status);
        Assert.DoesNotContain(secret, observable, StringComparison.Ordinal);
    }

    [Fact]
    public void Body_RejectsAmbiguousFragmentsAndUnknownSchemes()
    {
        var ambiguous = Encoding.UTF8.GetBytes("vless://synthetic.invalid#剩余流量：1 GB#剩余流量：1 GB");
        var unknown = Encoding.UTF8.GetBytes("https://synthetic.invalid/#剩余流量：1 GB");

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(ambiguous, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Unsupported,
            SubscriptionQuotaParser.ParseBody(unknown, RetrievedAt).Status);
    }

    [Fact]
    public void Body_RejectsFragmentsAndSuffixesOnLegacyVmessLines()
    {
        var invalidPayloadWithQuotaFragment = Encoding.UTF8.GetBytes(
            "vmess://%%%#Remaining%20Traffic%3A%201%20GB");
        var validPayloadWithQuotaFragment = Encoding.UTF8.GetBytes(
            Vmess("{\"ps\":\"ordinary node\"}") + "#Remaining%20Traffic%3A%201%20GB");

        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(invalidPayloadWithQuotaFragment, RetrievedAt).Status);
        Assert.Equal(SubscriptionQuotaStatusCode.Malformed,
            SubscriptionQuotaParser.ParseBody(validPayloadWithQuotaFragment, RetrievedAt).Status);
    }

    private static byte[] EncodeOuter(string text) =>
        Encoding.UTF8.GetBytes(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)));

    private static string Vmess(string json) =>
        "vmess://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=');

    private sealed class CultureScope : IDisposable
    {
        private readonly CultureInfo _original = CultureInfo.CurrentCulture;

        public CultureScope(string culture) => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

        public void Dispose() => CultureInfo.CurrentCulture = _original;
    }
}
