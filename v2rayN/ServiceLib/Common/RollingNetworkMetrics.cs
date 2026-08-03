namespace ServiceLib.Common;

public sealed record RollingNetworkMetricsSnapshot(int? DelayMs, int? JitterMs, int LossPercent, int SampleCount, int SuccessfulSampleCount);

public sealed class RollingNetworkMetrics(int capacity = 10)
{
    private readonly int _capacity = Math.Max(2, capacity);
    private readonly Queue<int?> _samples = new();
    private readonly object _sync = new();

    public RollingNetworkMetricsSnapshot AddSample(int delayMs)
    {
        lock (_sync)
        {
            _samples.Enqueue(delayMs > 0 ? delayMs : null);
            while (_samples.Count > _capacity)
            {
                _samples.Dequeue();
            }
            return GetSnapshotCore();
        }
    }

    public RollingNetworkMetricsSnapshot GetSnapshot()
    {
        lock (_sync)
        {
            return GetSnapshotCore();
        }
    }

    public void Reset()
    {
        lock (_sync)
        {
            _samples.Clear();
        }
    }

    private RollingNetworkMetricsSnapshot GetSnapshotCore()
    {
        var samples = _samples.ToArray();
        var successful = samples.Where(sample => sample.HasValue).Select(sample => sample!.Value).ToArray();
        int? jitter = successful.Length < 2
            ? null
            : (int)Math.Round(successful.Zip(successful.Skip(1), (left, right) => Math.Abs(right - left)).Average());
        var losses = samples.Count(sample => !sample.HasValue);
        var lossPercent = samples.Length == 0 ? 0 : (int)Math.Round(losses * 100d / samples.Length);

        return new(samples.LastOrDefault(), jitter, lossPercent, samples.Length, successful.Length);
    }
}
