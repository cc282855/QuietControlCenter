using Xunit;

namespace ServiceLib.Tests;

public sealed class SubscriptionOfficialUrlParserTests
{
    [Fact]
    public void Detect_PrefersStandardResponseHeader()
    {
        var result = SubscriptionOfficialUrlParser.Detect(
            ["https://provider.example/account"],
            "profile-web-page-url: https://body.example/dashboard");

        Assert.Equal("https://provider.example/account", result);
    }

    [Theory]
    [InlineData("profile-web-page-url: https://provider.example/dashboard")]
    [InlineData("\"homepage\": \"https://provider.example/portal\"")]
    public void Detect_ReadsSupportedBodyMetadata(string content)
    {
        Assert.StartsWith("https://provider.example/", SubscriptionOfficialUrlParser.Detect(null, content));
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://provider.example/")]
    [InlineData("https://user:password@provider.example/")]
    [InlineData("https://localhost/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://192.168.1.1/")]
    [InlineData("https://192.0.2.1/")]
    [InlineData("https://198.51.100.1/")]
    [InlineData("https://[::]/")]
    [InlineData("https://[2001:db8::1]/")]
    public void Normalize_RejectsUnsafeOrCredentialedLinks(string value)
    {
        Assert.Null(SubscriptionOfficialUrlParser.Normalize(value));
    }

    [Fact]
    public void OriginPolicy_CanonicalizesAndRequiresExactPort()
    {
        Assert.Equal("https://provider.example/",
            SubscriptionOfficialUrlParser.GetCanonicalOrigin("https://provider.example/account?q=1"));
        Assert.True(SubscriptionOfficialUrlParser.HasExactOrigin(
            "https://provider.example/other", "https://provider.example/"));
        Assert.False(SubscriptionOfficialUrlParser.HasExactOrigin(
            "https://provider.example:8443/", "https://provider.example/"));
        Assert.False(SubscriptionOfficialUrlParser.HasExactOrigin(
            "https://login.example/", "https://provider.example/"));
    }
}
