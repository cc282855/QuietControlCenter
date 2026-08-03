namespace ServiceLib.Services.Statistics;

public class StatisticsSingboxService
{
    private readonly Func<ServerSpeedItem, Task>? _updateFunc;
    private readonly ClashTrafficSnapshotCalculator _trafficSnapshot = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _runTask;
    private long _lastSnapshotTimestamp;

    public StatisticsSingboxService(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        _updateFunc = updateFunc;
        _runTask = Task.Run(Run);
    }

    public async Task CloseAsync()
    {
        _cancellation.Cancel();
        try
        {
            await _runTask;
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
        Reset();
        _cancellation.Dispose();
    }

    private async Task Run()
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (await timer.WaitForNextTickAsync(_cancellation.Token))
            {
                try
                {
                    if (!CoreManager.Instance.IsRunning || !AppManager.Instance.IsRunningCore(ECoreType.sing_box))
                    {
                        Reset();
                        continue;
                    }

                    var generation = CoreManager.Instance.Generation;
                    var connections = await ClashApiManager.Instance.GetClashConnectionsAsync(_cancellation.Token);
                    if (connections is null || generation != CoreManager.Instance.Generation)
                    {
                        Reset();
                        continue;
                    }

                    var now = Stopwatch.GetTimestamp();
                    var elapsedSeconds = _lastSnapshotTimestamp == 0
                        ? 0
                        : Stopwatch.GetElapsedTime(_lastSnapshotTimestamp, now).TotalSeconds;
                    _lastSnapshotTimestamp = now;
                    var delta = _trafficSnapshot.GetDelta(connections);
                    if (!delta.HasBaseline || elapsedSeconds <= 0)
                    {
                        continue;
                    }

                    var update = new ServerSpeedItem
                    {
                        ProxyUp = ToKilobytesPerSecond(delta.ProxyUp, elapsedSeconds),
                        ProxyDown = ToKilobytesPerSecond(delta.ProxyDown, elapsedSeconds),
                        DirectUp = ToKilobytesPerSecond(delta.DirectUp, elapsedSeconds),
                        DirectDown = ToKilobytesPerSecond(delta.DirectDown, elapsedSeconds),
                        ProxyUpBytes = delta.ProxyUp,
                        ProxyDownBytes = delta.ProxyDown,
                        DirectUpBytes = delta.DirectUp,
                        DirectDownBytes = delta.DirectDown,
                        CoreGeneration = generation
                    };
                    if (CoreManager.Instance.IsRunning
                        && AppManager.Instance.IsRunningCore(ECoreType.sing_box)
                        && generation == CoreManager.Instance.Generation)
                    {
                        await _updateFunc?.Invoke(update);
                    }
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(StatisticsSingboxService), ex);
                    Reset();
                }
            }
        }
        catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
        {
        }
    }

    private void Reset()
    {
        _trafficSnapshot.Reset();
        _lastSnapshotTimestamp = 0;
    }

    private static long ToKilobytesPerSecond(long bytes, double elapsedSeconds) =>
        (long)Math.Round(Math.Max(0, bytes) / elapsedSeconds / 1024d, MidpointRounding.AwayFromZero);
}
