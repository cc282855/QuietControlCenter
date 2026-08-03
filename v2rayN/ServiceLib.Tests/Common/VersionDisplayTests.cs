using AwesomeAssertions;
using Xunit;

namespace ServiceLib.Tests.Common;

public sealed class VersionDisplayTests
{
    [Fact]
    public void HotfixRevisionIsNotHidden()
    {
        Utils.FormatVersion(new Version(7, 24, 4, 5)).Should().Be("7.24.4.5");
    }

    [Fact]
    public void ZeroRevisionKeepsTheOfficialThreePartFormat()
    {
        Utils.FormatVersion(new Version(7, 24, 4, 0)).Should().Be("7.24.4");
    }
}
