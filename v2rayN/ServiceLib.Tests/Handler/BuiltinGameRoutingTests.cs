using ServiceLib.Common;
using ServiceLib.Models.Entities;
using Xunit;

namespace ServiceLib.Tests.Handler;

public class BuiltinGameRoutingTests
{
    [Theory]
    [InlineData("white")]
    [InlineData("black")]
    public void OverseasGameRule_IsFirstAndExcludesChina(string preset)
    {
        var rules = JsonUtils.Deserialize<List<RulesItem>>(
            EmbedUtils.GetEmbedText(Global.CustomRoutingFileName + preset));

        Assert.NotNull(rules);
        var gameRule = Assert.Single(rules!, rule => rule.Remarks == "代理海外游戏平台与联机游戏");
        Assert.Same(rules[0], gameRule);
        Assert.Equal(Global.ProxyTag, gameRule.OutboundTag);
        Assert.Equal(["geosite:category-games-!cn"], gameRule.Domain);
        Assert.DoesNotContain("geosite:category-games", gameRule.Domain!);

        var udp443Index = rules.FindIndex(rule =>
            rule.OutboundTag == Global.BlockTag && rule.Network == "udp" && rule.Port == "443");
        Assert.True(udp443Index > 0);
    }

    [Fact]
    public void GlobalPreset_DoesNotDuplicateOverseasGameRule()
    {
        var rules = JsonUtils.Deserialize<List<RulesItem>>(
            EmbedUtils.GetEmbedText(Global.CustomRoutingFileName + "global"));

        Assert.NotNull(rules);
        Assert.DoesNotContain(rules!, rule => rule.Remarks == "代理海外游戏平台与联机游戏");
    }
}
