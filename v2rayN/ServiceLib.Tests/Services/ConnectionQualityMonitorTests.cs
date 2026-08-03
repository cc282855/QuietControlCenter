using ServiceLib.Services;
using Xunit;

namespace ServiceLib.Tests.Services;

public class ConnectionQualityMonitorTests
{
    [Fact]
    public async Task SampleAsync_DoesNotOverlapProbes()
    {
        var probe = new BlockingProbe();
        var monitor = new ConnectionQualityMonitor(probe);

        var first = monitor.SampleAsync(CancellationToken.None);
        var second = await monitor.SampleAsync(CancellationToken.None);
        probe.Complete(42);
        var firstResult = await first;

        Assert.Null(second);
        Assert.Equal(42, firstResult?.DelayMs);
        Assert.Equal(1, probe.CallCount);
    }

    [Fact]
    public async Task SampleAsync_PropagatesExternalCancellation()
    {
        var monitor = new ConnectionQualityMonitor(new CancellationProbe());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => monitor.SampleAsync(cancellation.Token));
    }

    private sealed class BlockingProbe : IConnectionQualityProbe
    {
        private readonly TaskCompletionSource<int> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }
        public Task<int> ProbeAsync(CancellationToken cancellationToken)
        {
            CallCount++;
            return _completion.Task.WaitAsync(cancellationToken);
        }
        public void Complete(int delay) => _completion.TrySetResult(delay);
    }

    private sealed class CancellationProbe : IConnectionQualityProbe
    {
        public Task<int> ProbeAsync(CancellationToken cancellationToken) => Task.FromCanceled<int>(cancellationToken);
    }
}
