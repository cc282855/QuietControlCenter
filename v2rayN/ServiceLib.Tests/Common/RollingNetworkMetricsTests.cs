using ServiceLib.Common;
using Xunit;

namespace ServiceLib.Tests.Common;

public class RollingNetworkMetricsTests
{
    [Fact]
    public void AddSample_ComputesCurrentDelayJitterAndLoss()
    {
        var metrics = new RollingNetworkMetrics(10);

        metrics.AddSample(50);
        metrics.AddSample(70);
        metrics.AddSample(-1);
        var snapshot = metrics.AddSample(90);

        Assert.Equal(90, snapshot.DelayMs);
        Assert.Equal(20, snapshot.JitterMs);
        Assert.Equal(25, snapshot.LossPercent);
        Assert.Equal(4, snapshot.SampleCount);
    }

    [Fact]
    public void AddSample_RollsWindowAndReportsLatestTimeout()
    {
        var metrics = new RollingNetworkMetrics(3);

        metrics.AddSample(10);
        metrics.AddSample(20);
        metrics.AddSample(30);
        var snapshot = metrics.AddSample(-1);

        Assert.Null(snapshot.DelayMs);
        Assert.Equal(10, snapshot.JitterMs);
        Assert.Equal(33, snapshot.LossPercent);
        Assert.Equal(3, snapshot.SampleCount);
    }

    [Fact]
    public void AddSample_DoesNotInventJitterWithoutTwoSuccessfulSamples()
    {
        var metrics = new RollingNetworkMetrics(10);

        metrics.AddSample(50);
        var snapshot = metrics.AddSample(-1);

        Assert.Null(snapshot.JitterMs);
        Assert.Equal(1, snapshot.SuccessfulSampleCount);
        Assert.Equal(50, snapshot.LossPercent);
    }
}
