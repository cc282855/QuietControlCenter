using ServiceLib.Services.Statistics;
using Xunit;

namespace ServiceLib.Tests.Services.Statistics;

public class TrafficByteAccumulatorTests
{
    [Fact]
    public void Add_UsesTransferredBytesRatherThanSamplingDuration()
    {
        var accumulator = new TrafficByteAccumulator();

        var result = accumulator.Add(1536, 3072);

        Assert.Equal(1, result.UpKilobytes);
        Assert.Equal(3, result.DownKilobytes);
    }

    [Fact]
    public void Add_CarriesSubKilobyteTrafficAcrossSamples()
    {
        var accumulator = new TrafficByteAccumulator();

        Assert.Equal(0, accumulator.Add(400, 200).UpKilobytes);
        Assert.Equal(0, accumulator.Add(400, 300).UpKilobytes);
        var third = accumulator.Add(224, 524);

        Assert.Equal(1, third.UpKilobytes);
        Assert.Equal(1, third.DownKilobytes);
    }
}
