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
    [InlineData("https://user:password@provider.example/")]
    public void Normalize_RejectsUnsafeOrCredentialedLinks(string value)
    {
        Assert.Null(SubscriptionOfficialUrlParser.Normalize(value));
    }
}
