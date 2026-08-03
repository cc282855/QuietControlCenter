using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class CountryClassifierTests
{
    [Fact]
    public void Classify_FlagHasHighestPriority()
    {
        CountryClassifier.Classify("🇯🇵 United States relay", "edge.us")
            .Should().Be("JP");
    }

    [Theory]
    [InlineData("🚀 US")]
    [InlineData("\uD83D US")]
    public void Classify_SupplementaryEmojiAndUnpairedSurrogatesAreSafe(string remarks)
    {
        var act = () => CountryClassifier.Classify(remarks, null);

        act.Should().NotThrow();
        act().Should().Be("US");
    }

    [Theory]
    [InlineData("node [US] 01", "US")]
    [InlineData("premium-HK-02", "HK")]
    [InlineData("login id with no inbound", "ZZ")]
    [InlineData("inside node", "ZZ")]
    public void Classify_UsesBoundarySafeUppercaseIsoCodes(string remarks, string expected)
    {
        CountryClassifier.Classify(remarks, null).Should().Be(expected);
    }

    [Theory]
    [InlineData("NO LOGS")]
    [InlineData("IT IS FAST")]
    [InlineData("LA premium")]
    [InlineData("IN transit")]
    [InlineData("ID node")]
    [InlineData("Milan IT fast")]
    public void Classify_DoesNotTreatAmbiguousWordsAsIsoLabels(string remarks)
    {
        CountryClassifier.Classify(remarks, null).Should().Be(CountryClassifier.UnknownCode);
    }

    [Theory]
    [InlineData("[IN]", "IN")]
    [InlineData("IN-01", "IN")]
    [InlineData("relay-IN-01", "IN")]
    [InlineData("[IT] Milan", "IT")]
    [InlineData("[NO] Oslo", "NO")]
    [InlineData("01_ID", "ID")]
    [InlineData("LA", "LA")]
    public void Classify_AcceptsAmbiguousIsoCodesInExplicitLabelContexts(string remarks, string expected)
    {
        CountryClassifier.Classify(remarks, null).Should().Be(expected);
    }

    [Theory]
    [InlineData("US", "US")]
    [InlineData("JP", "JP")]
    [InlineData("CN", "CN")]
    public void Classify_RetainsUnambiguousIsoLabels(string remarks, string expected)
    {
        CountryClassifier.Classify(remarks, null).Should().Be(expected);
    }

    [Theory]
    [InlineData("香港 IPLC", "HK")]
    [InlineData("Los Angeles premium", "US")]
    [InlineData("South Korea Seoul", "KR")]
    [InlineData("新加坡优化", "SG")]
    public void Classify_RecognizesChineseAndEnglishAliases(string remarks, string expected)
    {
        CountryClassifier.Classify(remarks, null).Should().Be(expected);
    }

    [Fact]
    public void Classify_RemarkAliasWinsOverAddressCcTld()
    {
        CountryClassifier.Classify("Tokyo direct", "gateway.de").Should().Be("JP");
    }

    [Theory]
    [InlineData("edge.example.jp", "JP")]
    [InlineData("relay.example.co.uk:443", "GB")]
    [InlineData("192.0.2.1", "ZZ")]
    [InlineData("node.internal", "ZZ")]
    public void Classify_UsesAddressCcTldAsFallback(string address, string expected)
    {
        CountryClassifier.Classify("premium node", address).Should().Be(expected);
    }

    [Theory]
    [InlineData("🇱🇹 relay", "node.internal", "LT")]
    [InlineData("🇳🇬 relay", "node.internal", "NG")]
    [InlineData("relay", "edge.example.pa", "PA")]
    [InlineData("relay", "edge.example.np", "NP")]
    public void Classify_CoversCompleteIsoFlagsAndCcTlds(string remarks, string address, string expected)
    {
        CountryClassifier.Classify(remarks, address).Should().Be(expected);
        CountryClassifier.NormalizeFilterCode(expected).Should().Be(expected);
        CountryClassifier.GetDisplayName(expected).Should().Contain(expected);
    }

    [Fact]
    public void CountryOptionsAndFilter_AreDerivedFromUnfilteredBase()
    {
        var profiles = new[]
        {
            new ProfileItemModel { Remarks = "🇯🇵 Tokyo", Address = "one.example" },
            new ProfileItemModel { Remarks = "US West", Address = "two.example" },
            new ProfileItemModel { Remarks = "private relay", Address = "host.internal" },
        };

        CountryClassifier.GetAvailableCodes(profiles, item => item.Remarks, item => item.Address)
            .Should().BeEquivalentTo(["JP", "US", "ZZ"]);
        CountryClassifier.ApplyFilter(profiles, item => item.Remarks, item => item.Address, "US")
            .Should().ContainSingle().Which.Remarks.Should().Be("US West");
        CountryClassifier.ApplyFilter(profiles, item => item.Remarks, item => item.Address, "")
            .Should().HaveCount(3);
        CountryClassifier.ApplyFilter(profiles, item => item.Remarks, item => item.Address, "LT")
            .Should().BeEmpty("a temporarily absent persisted country must produce empty results, not an All fallback");
    }

    [Fact]
    public void PersistedCode_IsBackwardCompatibleAndNormalized()
    {
        var legacy = JsonSerializer.Deserialize<UIItem>("{}")!;
        CountryClassifier.NormalizeFilterCode(legacy.ProfilesCountryFilterCode).Should().BeEmpty();

        var saved = JsonSerializer.Deserialize<UIItem>("{\"ProfilesCountryFilterCode\":\"jp\"}")!;
        CountryClassifier.NormalizeFilterCode(saved.ProfilesCountryFilterCode).Should().Be("JP");
        CountryClassifier.NormalizeFilterCode("unsupported").Should().BeEmpty();
        CountryClassifier.NormalizeFilterCode("ZZ").Should().Be("ZZ");
    }
}
