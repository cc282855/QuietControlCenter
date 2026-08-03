using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public class ProfileColumnVisibilityTests
{
    [Fact]
    public void Columns_ContainsExactlyTheEightConfigurableFields()
    {
        ProfileColumnVisibility.Columns.Should().Equal(
            "ConfigType", "Remarks", "Address", "Port", "Network", "StreamSecurity", "Delay", "SpeedVal");
    }

    [Fact]
    public void MissingOrEmptyConfiguration_ShowsEveryField()
    {
        foreach (var column in ProfileColumnVisibility.Columns)
        {
            ProfileColumnVisibility.IsVisible(null, column).Should().BeTrue();
            ProfileColumnVisibility.IsVisible([], column).Should().BeTrue();
        }
    }

    [Fact]
    public void LegacyDelayNameAndUnknownValues_AreNormalizedSafely()
    {
        ProfileColumnVisibility.NormalizeHiddenColumns(["DelayVal", "Delay", "Unknown", "Address"])
            .Should().Equal("Delay", "Address");

        var legacy = JsonSerializer.Deserialize<UIItem>("{}")!;
        legacy.HiddenProfileColumns.Should().BeNull();
        ProfileColumnVisibility.IsVisible(legacy.HiddenProfileColumns, "Address").Should().BeTrue();
    }

    [Fact]
    public void SavingAndRestoring_PreservesIndependentVisibility()
    {
        var visibility = ProfileColumnVisibility.Columns.ToDictionary(name => name, _ => true);
        visibility[ProfileColumnVisibility.Address] = false;
        visibility[ProfileColumnVisibility.SpeedVal] = false;

        var saved = ProfileColumnVisibility.GetHiddenColumns(visibility);

        saved.Should().Equal("Address", "SpeedVal");
        ProfileColumnVisibility.IsVisible(saved, "Address").Should().BeFalse();
        ProfileColumnVisibility.IsVisible(saved, "SpeedVal").Should().BeFalse();
        ProfileColumnVisibility.IsVisible(saved, "Remarks").Should().BeTrue();
    }

    [Fact]
    public void ReEnablingAField_RemovesItFromHiddenConfiguration()
    {
        var restored = ProfileColumnVisibility.Columns.ToDictionary(name => name, _ => true);

        ProfileColumnVisibility.GetHiddenColumns(restored).Should().BeEmpty();
        ProfileColumnVisibility.IsVisible([], ProfileColumnVisibility.Address).Should().BeTrue();
    }
}
