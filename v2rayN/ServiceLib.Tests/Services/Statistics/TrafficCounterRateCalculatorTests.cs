using ServiceLib.Models.Dto;
using ServiceLib.Services.Statistics;
using Xunit;

namespace ServiceLib.Tests.Services.Statistics;

public class TrafficCounterRateCalculatorTests
{
    [Fact]
    public void TryCalculate_NormalizesAllCountersByElapsedTime()
    {
        var calculator = new TrafficCounterRateCalculator();
        Assert.False(calculator.TryCalculate(Counters(1000, 2000, 3000, 4000), 1, out _));

        var success = calculator.TryCalculate(Counters(2024, 4048, 6072, 8096), 2, out var rate);

        Assert.True(success);
        Assert.Equal(1, rate.ProxyUp);
        Assert.Equal(1, rate.ProxyDown);
        Assert.Equal(2, rate.DirectUp);
        Assert.Equal(2, rate.DirectDown);
        Assert.Equal(1024, rate.ProxyUpBytes);
        Assert.Equal(2048, rate.ProxyDownBytes);
        Assert.Equal(3072, rate.DirectUpBytes);
        Assert.Equal(4096, rate.DirectDownBytes);
    }

    [Fact]
    public void TryCalculate_PreservesExactBytesWhenElapsedIsNotOneSecond()
    {
        var calculator = new TrafficCounterRateCalculator();
        calculator.TryCalculate(Counters(0, 0, 0, 0), 1, out _);

        Assert.True(calculator.TryCalculate(Counters(1536, 768, 0, 0), 1.5, out var rate));

        Assert.Equal(1, rate.ProxyUp);
        Assert.Equal(1, rate.ProxyDown);
        Assert.Equal(1536, rate.ProxyUpBytes);
        Assert.Equal(768, rate.ProxyDownBytes);
    }

    [Fact]
    public void TryCalculate_UsesRegressedCountersAsNewBaseline()
    {
        var calculator = new TrafficCounterRateCalculator();
        calculator.TryCalculate(Counters(10000, 10000, 10000, 10000), 1, out _);
        Assert.False(calculator.TryCalculate(Counters(100, 200, 300, 400), 1, out _));

        Assert.True(calculator.TryCalculate(Counters(1124, 1224, 1324, 1424), 1, out var rate));
        Assert.Equal(1, rate.ProxyUp);
        Assert.Equal(1, rate.ProxyDown);
        Assert.Equal(1, rate.DirectUp);
        Assert.Equal(1, rate.DirectDown);
    }

    private static ServerSpeedItem Counters(long proxyUp, long proxyDown, long directUp, long directDown) => new()
    {
        ProxyUp = proxyUp,
        ProxyDown = proxyDown,
        DirectUp = directUp,
        DirectDown = directDown
    };
}
