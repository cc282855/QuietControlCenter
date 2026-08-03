namespace ServiceLib.Services;

public interface IConnectionQualityProbe
{
    Task<int> ProbeAsync(CancellationToken cancellationToken);
}

public sealed class ConnectionQualityMonitor(IConnectionQualityProbe probe, int capacity = 10)
{
    private readonly RollingNetworkMetrics _metrics = new(capacity);
    private int _probeInProgress;

    public async Task<RollingNetworkMetricsSnapshot?> SampleAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _probeInProgress, 1) != 0)
        {
            return null;
        }

        try
        {
            var delay = await probe.ProbeAsync(cancellationToken).ConfigureAwait(false);
            return _metrics.AddSample(delay);
        }
        finally
        {
            Interlocked.Exchange(ref _probeInProgress, 0);
        }
    }

    public void Reset() => _metrics.Reset();
}
