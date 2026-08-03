namespace ServiceLib.Manager;

public class StatisticsManager
{
    private static readonly Lazy<StatisticsManager> instance = new(() => new());
    public static StatisticsManager Instance => instance.Value;

    private Config _config;
    private ServerStatItem? _serverStatItem;
    private List<ServerStatItem> _lstServerStat;
    private Func<ServerSpeedItem, Task>? _updateFunc;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly Dictionary<string, TrafficByteAccumulator> _historyAccumulators = new(StringComparer.Ordinal);

    private StatisticsXrayService? _statisticsXray;
    private StatisticsSingboxService? _statisticsSingbox;
    private static readonly string _tag = "StatisticsHandler";
    public List<ServerStatItem> ServerStat => _lstServerStat;

    public async Task Init(Config config, Func<ServerSpeedItem, Task> updateFunc)
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopCollectorsAsync();
            _config = config;
            _updateFunc = updateFunc;
            _historyAccumulators.Clear();
            if (config.GuiItem.EnableStatistics)
            {
                await InitData();
            }
            else
            {
                _lstServerStat = [];
            }

            _statisticsXray = new StatisticsXrayService(config, UpdateServerStatHandler);
            _statisticsSingbox = new StatisticsSingboxService(config, UpdateServerStatHandler);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task CloseAsync()
    {
        await _lifecycleLock.WaitAsync();
        try
        {
            await StopCollectorsAsync();
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public async Task ClearAllServerStatistics()
    {
        await _updateLock.WaitAsync();
        try
        {
            await SQLiteHelper.Instance.ExecuteAsync($"delete from ServerStatItem ");
            _serverStatItem = null;
            _lstServerStat = [];
            _historyAccumulators.Clear();
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task SaveTo()
    {
        await _updateLock.WaitAsync();
        try
        {
            if (_lstServerStat != null)
            {
                await SQLiteHelper.Instance.UpdateAllAsync(_lstServerStat);
            }
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    public async Task CloneServerStatItem(string indexId, string toIndexId)
    {
        if (_lstServerStat == null)
        {
            return;
        }

        if (indexId == toIndexId)
        {
            return;
        }

        var stat = _lstServerStat.FirstOrDefault(t => t.IndexId == indexId);
        if (stat == null)
        {
            return;
        }

        var toStat = JsonUtils.DeepCopy(stat);
        toStat.IndexId = toIndexId;
        await SQLiteHelper.Instance.ReplaceAsync(toStat);
        _lstServerStat.Add(toStat);
    }

    private async Task InitData()
    {
        await SQLiteHelper.Instance.ExecuteAsync($"delete from ServerStatItem where indexId not in ( select indexId from ProfileItem )");

        var ticks = DateTime.Now.Date.Ticks;
        await SQLiteHelper.Instance.ExecuteAsync($"update ServerStatItem set todayUp = 0,todayDown=0,dateNow={ticks} where dateNow<>{ticks}");

        _lstServerStat = await SQLiteHelper.Instance.TableAsync<ServerStatItem>().ToListAsync();
    }

    private async Task UpdateServerStatHandler(ServerSpeedItem server)
    {
        await _updateLock.WaitAsync();
        try
        {
            if (server.CoreGeneration != CoreManager.Instance.Generation)
            {
                return;
            }
            await UpdateServerStat(server);
        }
        catch (Exception ex)
        {
            Logging.SaveLog(_tag, ex);
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private async Task UpdateServerStat(ServerSpeedItem server)
    {
        if (_config.GuiItem.EnableStatistics)
        {
            await GetServerStatItem(_config.IndexId);
            if (_serverStatItem is not null)
            {
                if (server.ProxyUpBytes != 0 || server.ProxyDownBytes != 0)
                {
                    if (!_historyAccumulators.TryGetValue(_config.IndexId, out var accumulator))
                    {
                        accumulator = new TrafficByteAccumulator();
                        _historyAccumulators[_config.IndexId] = accumulator;
                    }
                    var transferred = accumulator.Add(server.ProxyUpBytes, server.ProxyDownBytes);
                    _serverStatItem.TodayUp += transferred.UpKilobytes;
                    _serverStatItem.TodayDown += transferred.DownKilobytes;
                    _serverStatItem.TotalUp += transferred.UpKilobytes;
                    _serverStatItem.TotalDown += transferred.DownKilobytes;
                }
                server.TodayUp = _serverStatItem.TodayUp;
                server.TodayDown = _serverStatItem.TodayDown;
                server.TotalUp = _serverStatItem.TotalUp;
                server.TotalDown = _serverStatItem.TotalDown;
            }
        }

        server.IndexId = _config.IndexId;
        await _updateFunc?.Invoke(server);
    }

    private async Task StopCollectorsAsync()
    {
        var xray = _statisticsXray;
        var singbox = _statisticsSingbox;
        _statisticsXray = null;
        _statisticsSingbox = null;

        if (xray is not null)
        {
            await xray.CloseAsync();
        }
        if (singbox is not null)
        {
            await singbox.CloseAsync();
        }
    }

    private async Task GetServerStatItem(string indexId)
    {
        var ticks = DateTime.Now.Date.Ticks;
        if (_serverStatItem != null && _serverStatItem.IndexId != indexId)
        {
            _serverStatItem = null;
        }

        if (_serverStatItem == null)
        {
            _serverStatItem = _lstServerStat.FirstOrDefault(t => t.IndexId == indexId);
            if (_serverStatItem == null)
            {
                _serverStatItem = new ServerStatItem
                {
                    IndexId = indexId,
                    TotalUp = 0,
                    TotalDown = 0,
                    TodayUp = 0,
                    TodayDown = 0,
                    DateNow = ticks
                };
                await SQLiteHelper.Instance.ReplaceAsync(_serverStatItem);
                _lstServerStat.Add(_serverStatItem);
            }
        }

        if (_serverStatItem.DateNow != ticks)
        {
            _serverStatItem.TodayUp = 0;
            _serverStatItem.TodayDown = 0;
            _serverStatItem.DateNow = ticks;
        }
    }
}
