using ServiceLib.Models.Dto;
using ServiceLib.Services.Statistics;
using Xunit;

namespace ServiceLib.Tests.Services.Statistics;

public class ClashTrafficSnapshotCalculatorTests
{
    [Fact]
    public void GetDirectDelta_CountsOnlyDirectConnectionDeltas()
    {
        var calculator = new ClashTrafficSnapshotCalculator();
        calculator.GetDelta(Snapshot(("direct", 100, 200, true), ("proxy", 300, 400, false)));

        var delta = calculator.GetDelta(Snapshot(("direct", 150, 275, true), ("proxy", 900, 1200, false)));

        Assert.Equal(50, delta.DirectUp);
        Assert.Equal(75, delta.DirectDown);
        Assert.Equal(600, delta.ProxyUp);
        Assert.Equal(800, delta.ProxyDown);
        Assert.True(delta.HasBaseline);
    }

    [Fact]
    public void GetDirectDelta_ResetDiscardsPreviousBaselines()
    {
        var calculator = new ClashTrafficSnapshotCalculator();
        calculator.GetDelta(Snapshot(("direct", 100, 200, true)));
        calculator.Reset();

        var delta = calculator.GetDelta(Snapshot(("direct", 500, 800, true)));

        Assert.False(delta.HasBaseline);
        Assert.Equal(0, delta.DirectUp);
        Assert.Equal(0, delta.DirectDown);
    }

    [Fact]
    public void GetDelta_CountsNewConnectionsAfterTheInitialBaseline()
    {
        var calculator = new ClashTrafficSnapshotCalculator();
        calculator.GetDelta(Snapshot(("existing", 100, 200, false)));

        var delta = calculator.GetDelta(Snapshot(("existing", 150, 250, false), ("new", 300, 400, true)));

        Assert.True(delta.HasBaseline);
        Assert.Equal(50, delta.ProxyUp);
        Assert.Equal(50, delta.ProxyDown);
        Assert.Equal(300, delta.DirectUp);
        Assert.Equal(400, delta.DirectDown);
    }

    private static ClashConnections Snapshot(params (string Id, ulong Up, ulong Down, bool Direct)[] items) => new()
    {
        connections = items.Select(item => new ConnectionItem
        {
            id = item.Id,
            upload = item.Up,
            download = item.Down,
            chains = [item.Direct ? "direct" : "proxy"]
        }).ToList()
    };
}
