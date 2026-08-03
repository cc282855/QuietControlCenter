using ServiceLib.Common;
using Xunit;

namespace ServiceLib.Tests.Common;

public class ConnectionQualitySeverityCalculatorTests
{
    [Theory]
    [InlineData(null, ConnectionQualitySeverity.None)]
    [InlineData(100, ConnectionQualitySeverity.Good)]
    [InlineData(101, ConnectionQualitySeverity.Warning)]
    [InlineData(200, ConnectionQualitySeverity.Warning)]
    [InlineData(201, ConnectionQualitySeverity.Danger)]
    public void DelaySeverity_UsesExpectedBoundaries(int? delayMs, ConnectionQualitySeverity expected)
    {
        Assert.Equal(expected, ConnectionQualitySeverityCalculator.GetDelaySeverity(delayMs));
    }

    [Theory]
    [InlineData(null, ConnectionQualitySeverity.None)]
    [InlineData(20, ConnectionQualitySeverity.Good)]
    [InlineData(21, ConnectionQualitySeverity.Warning)]
    [InlineData(50, ConnectionQualitySeverity.Warning)]
    [InlineData(51, ConnectionQualitySeverity.Danger)]
    public void JitterSeverity_UsesExpectedBoundaries(int? jitterMs, ConnectionQualitySeverity expected)
    {
        Assert.Equal(expected, ConnectionQualitySeverityCalculator.GetJitterSeverity(jitterMs));
    }

    [Theory]
    [InlineData(0, ConnectionQualitySeverity.Good)]
    [InlineData(1, ConnectionQualitySeverity.Warning)]
    [InlineData(5, ConnectionQualitySeverity.Warning)]
    [InlineData(6, ConnectionQualitySeverity.Danger)]
    public void LossSeverity_UsesExpectedBoundaries(int lossPercent, ConnectionQualitySeverity expected)
    {
        Assert.Equal(expected, ConnectionQualitySeverityCalculator.GetLossSeverity(lossPercent));
    }

    [Theory]
    [InlineData(10, 6, ConnectionQualitySeverity.Danger)]
    [InlineData(51, 0, ConnectionQualitySeverity.Danger)]
    [InlineData(21, 0, ConnectionQualitySeverity.Warning)]
    [InlineData(10, 1, ConnectionQualitySeverity.Warning)]
    [InlineData(10, 0, ConnectionQualitySeverity.Good)]
    public void JitterLossSeverity_UsesTheMoreSevereMetric(int? jitterMs, int lossPercent, ConnectionQualitySeverity expected)
    {
        Assert.Equal(expected, ConnectionQualitySeverityCalculator.GetJitterLossSeverity(jitterMs, lossPercent));
    }
}
