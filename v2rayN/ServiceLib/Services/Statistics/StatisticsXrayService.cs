namespace ServiceLib.Services.Statistics;

public class StatisticsXrayService
{
    private readonly TrafficCounterRateCalculator _rateCalculator = new();
    private readonly Func<ServerSpeedItem, Task>? _updateFunc;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _runTask;
    private long _lastSnapshotTimestamp;
    private string Url => $"{Global.HttpProtocol}{Global.Loopback}:{AppManager.Instance.StatePort}/debug/vars";

    public StatisticsXrayService(Config config, Func<ServerSpeedItem, Task> updateFunc)
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
                    if (!CoreManager.Instance.IsRunning || !AppManager.Instance.IsRunningCore(ECoreType.Xray))
                    {
                        Reset();
                        continue;
                    }

                    var generation = CoreManager.Instance.Generation;
                    var result = await HttpClientHelper.Instance.TryGetAsync(Url, _cancellation.Token);
                    var counters = result is null ? null : ParseCounters(result);
                    if (counters is null || generation != CoreManager.Instance.Generation)
                    {
                        Reset();
                        continue;
                    }

                    var now = Stopwatch.GetTimestamp();
                    var elapsedSeconds = _lastSnapshotTimestamp == 0
                        ? 0
                        : Stopwatch.GetElapsedTime(_lastSnapshotTimestamp, now).TotalSeconds;
                    _lastSnapshotTimestamp = now;
                    if (_rateCalculator.TryCalculate(counters, elapsedSeconds, out var rate)
                        && CoreManager.Instance.IsRunning
                        && AppManager.Instance.IsRunningCore(ECoreType.Xray)
                        && generation == CoreManager.Instance.Generation)
                    {
                        rate.CoreGeneration = generation;
                        await _updateFunc?.Invoke(rate);
                    }
                }
                catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Logging.SaveLog(nameof(StatisticsXrayService), ex);
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
        _rateCalculator.Reset();
        _lastSnapshotTimestamp = 0;
    }

    private static ServerSpeedItem? ParseCounters(string result)
    {
        try
        {
            var source = JsonUtils.Deserialize<V2rayMetricsVars>(result);
            if (source?.stats?.outbound == null)
            {
                return null;
            }

            ServerSpeedItem counters = new();
            foreach (var key in source.stats.outbound.Keys.Cast<string>())
            {
                var value = source.stats.outbound[key];
                if (value == null)
                {
                    continue;
                }
                var state = JsonUtils.Deserialize<V2rayMetricsVarsLink>(value.ToString());
                if (key.StartsWith(Global.ProxyTag))
                {
                    counters.ProxyUp += state.uplink;
                    counters.ProxyDown += state.downlink;
                }
                else if (key == Global.DirectTag)
                {
                    counters.DirectUp += state.uplink;
                    counters.DirectDown += state.downlink;
                }
            }
            return counters;
        }
        catch
        {
            return null;
        }
    }
}
