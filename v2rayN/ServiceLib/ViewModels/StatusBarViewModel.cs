namespace ServiceLib.ViewModels;

public class StatusBarViewModel : MyReactiveObject
{
    private DateTime _lastStatisticsAtUtc = DateTime.MinValue;
    public Interaction<string, Unit> SetClipboardDataInteraction { get; } = new();
    public Interaction<Unit, string?> PasswordInputInteraction { get; } = new();
    public Interaction<Unit, Unit> DispatcherRefreshIconInteraction { get; } = new();
    public EventChannel<bool> SubscriptionsUpdateRequested { get; } = new();
    public EventChannel<bool?> ShowHideWindowRequested { get; } = new();

    private static readonly Lazy<StatusBarViewModel> _instance = new(() => new());
    public static StatusBarViewModel Instance => _instance.Value;

    public EventChannel<string> SetDefaultServerRequested { get; } = new();
    public EventChannel<Unit> ReloadRequested { get; } = new();
    public EventChannel<Unit> AddServerViaScanRequested { get; } = new();
    public EventChannel<Unit> AddServerViaClipboardRequested { get; } = new();

    #region ObservableCollection

    public IObservableCollection<RoutingItem> RoutingItems { get; } = new ObservableCollectionExtended<RoutingItem>();

    public IObservableCollection<ComboItem> Servers { get; } = new ObservableCollectionExtended<ComboItem>();

    [Reactive]
    public RoutingItem SelectedRouting { get; set; }

    [Reactive]
    public ComboItem SelectedServer { get; set; }

    [Reactive]
    public bool BlServers { get; set; }

    #endregion ObservableCollection

    public ReactiveCommand<Unit, Unit> AddServerViaClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> AddServerViaScanCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateCmd { get; }
    public ReactiveCommand<Unit, Unit> SubUpdateViaProxyCmd { get; }
    public ReactiveCommand<Unit, Unit> CopyProxyCmdToClipboardCmd { get; }
    public ReactiveCommand<Unit, Unit> NotifyLeftClickCmd { get; }
    public ReactiveCommand<Unit, Unit> ShowWindowCmd { get; }
    public ReactiveCommand<Unit, Unit> HideWindowCmd { get; }

    #region System Proxy

    [Reactive]
    public bool BlSystemProxyClear { get; set; }

    [Reactive]
    public bool BlSystemProxySet { get; set; }

    [Reactive]
    public bool BlSystemProxyNothing { get; set; }

    [Reactive]
    public bool BlSystemProxyPac { get; set; }

    public ReactiveCommand<Unit, Unit> SystemProxyClearCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxySetCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyNothingCmd { get; }
    public ReactiveCommand<Unit, Unit> SystemProxyPacCmd { get; }

    [Reactive]
    public bool BlRouting { get; set; }

    [Reactive]
    public int SystemProxySelected { get; set; }

    [Reactive]
    public bool BlSystemProxyPacVisible { get; set; }

    #endregion System Proxy

    #region UI

    [Reactive]
    public string InboundDisplay { get; set; }

    [Reactive]
    public string InboundLanDisplay { get; set; }

    [Reactive]
    public string RunningServerDisplay { get; set; }

    [Reactive]
    public string RunningServerToolTipText { get; set; }

    [Reactive]
    public string RunningInfoDisplay { get; set; }

    [Reactive]
    public string SpeedProxyDisplay { get; set; }

    [Reactive]
    public string SpeedDirectDisplay { get; set; }

    [Reactive]
    public string ActiveNodeTrafficDisplay { get; set; }

    [Reactive]
    public bool EnableTun { get; set; }

    [Reactive]
    public bool BlIsNonWindows { get; set; }

    #endregion UI

    public StatusBarViewModel()
    {
        _config = AppManager.Instance.Config;
        SelectedRouting = new();
        SelectedServer = new();
        RunningServerToolTipText = GetRunningServerToolTipText("-");
        ActiveNodeTrafficDisplay = GetUnavailableTrafficDisplay();
        ResetLiveTrafficDisplay();
        BlSystemProxyPacVisible = Utils.IsWindows();
        BlIsNonWindows = Utils.IsNonWindows();

        if (_config.TunModeItem.EnableTun && AllowEnableTun())
        {
            EnableTun = true;
        }
        else
        {
            _config.TunModeItem.EnableTun = EnableTun = false;
        }

        #region WhenAnyValue && ReactiveCommand

        this.WhenAnyValue(
                x => x.SelectedRouting,
                y => y != null && !y.Remarks.IsNullOrEmpty())
            .Subscribe(async c => await RoutingSelectedChangedAsync(c));

        this.WhenAnyValue(
                x => x.SelectedServer,
                y => y != null && !y.Text.IsNullOrEmpty())
            .Subscribe(ServerSelectedChanged);

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        this.WhenAnyValue(
                x => x.SystemProxySelected,
                y => y >= 0)
            .Subscribe(async c => await DoSystemProxySelected(c));

        this.WhenAnyValue(
                x => x.EnableTun,
                y => y == true)
            .Subscribe(async c => await DoEnableTun(c));

        CopyProxyCmdToClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await CopyProxyCmdToClipboard();
        });

        NotifyLeftClickCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(null);
            await Task.CompletedTask;
        });
        ShowWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(true);
            await Task.CompletedTask;
        });
        HideWindowCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            ShowHideWindowRequested.Publish(false);
            await Task.CompletedTask;
        });

        AddServerViaClipboardCmd = ReactiveCommand.CreateFromTask(async () =>
            {
                await AddServerViaClipboard();
            });
        AddServerViaScanCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await AddServerViaScan();
        });
        SubUpdateCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(false);
        });
        SubUpdateViaProxyCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await UpdateSubscriptionProcess(true);
        });

        //System proxy
        SystemProxyClearCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedClear);
        });
        SystemProxySetCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.ForcedChange);
        });
        SystemProxyNothingCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Unchanged);
        });
        SystemProxyPacCmd = ReactiveCommand.CreateFromTask(async () =>
        {
            await SetListenerType(ESysProxyType.Pac);
        });

        #endregion WhenAnyValue && ReactiveCommand

        #region AppEvents

        AppEvents.DispatcherStatisticsRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await UpdateStatistics(result));

        AppEvents.SysProxyChangeRequested
            .AsObservable()
            .ObserveOn(RxSchedulers.MainThreadScheduler)
            .Subscribe(async result => await SetListenerType(result));

        #endregion AppEvents

        _ = Init();
    }

    private async Task Init()
    {
        await ConfigHandler.InitBuiltinRouting(_config);
        await RefreshRoutingsMenu();
        await InboundDisplayStatus();
        await ChangeSystemProxyAsync(_config.SystemProxyItem.SysProxyType, true);

        BlRouting = true;
    }

    private async Task CopyProxyCmdToClipboard()
    {
        var cmd = Utils.IsWindows() ? "set" : "export";
        var address = $"{Global.Loopback}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}";

        var sb = new StringBuilder();
        sb.AppendLine($"{cmd} http_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} https_proxy={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} all_proxy={Global.Socks5Protocol}{address}");
        sb.AppendLine("");
        sb.AppendLine($"{cmd} HTTP_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} HTTPS_PROXY={Global.HttpProtocol}{address}");
        sb.AppendLine($"{cmd} ALL_PROXY={Global.Socks5Protocol}{address}");

        await SetClipboardDataInteraction.Handle(sb.ToString());
    }

    private async Task AddServerViaClipboard()
    {
        AddServerViaClipboardRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task AddServerViaScan()
    {
        AddServerViaScanRequested.Publish();
        await Task.Delay(1000);
    }

    private async Task UpdateSubscriptionProcess(bool blProxy)
    {
        SubscriptionsUpdateRequested.Publish(blProxy);
        await Task.Delay(1000);
    }

    public async Task RefreshServersBiz()
    {
        await RefreshServersMenu();

        //display running server
        var running = await ConfigHandler.GetDefaultServer(_config);
        if (running != null)
        {
            RunningServerDisplay = running.GetSummary();
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
        else
        {
            RunningServerDisplay = ResUI.CheckServerSettings;
            RunningServerToolTipText = GetRunningServerToolTipText(RunningServerDisplay);
        }
        RefreshActiveNodeTrafficDisplay();
    }

    private string GetRunningServerToolTipText(string serverInfo)
    {
        return Utils.IsLinux() ? Global.AppName : serverInfo;
    }

    private async Task RefreshServersMenu()
    {
        var lstModel = await AppManager.Instance.ProfileModels(_config.SubIndexId, "");

        if (lstModel?.Count > _config.GuiItem.TrayMenuServersLimit)
        {
            BlServers = false;
            return;
        }

        var models = new List<ComboItem>();
        BlServers = true;
        foreach (var it in lstModel)
        {
            var name = it.GetSummary();

            var item = new ComboItem() { ID = it.IndexId, Text = name };
            models.Add(item);
            if (_config.IndexId == it.IndexId)
            {
                SelectedServer = item;
            }
        }
        Servers.Clear();
        Servers.AddRange(models);
    }

    private void ServerSelectedChanged(bool c)
    {
        if (!c)
        {
            return;
        }
        if (SelectedServer == null)
        {
            return;
        }
        if (SelectedServer.ID.IsNullOrEmpty())
        {
            return;
        }
        SetDefaultServerRequested.Publish(SelectedServer.ID);
    }

    public async Task TestServerAvailability()
    {
        var item = await ConfigHandler.GetDefaultServer(_config);
        if (item == null)
        {
            return;
        }

        await TestServerAvailabilitySub(ResUI.Speedtesting);

        var msg = await Task.Run(ConnectionHandler.RunAvailabilityCheck);

        NoticeManager.Instance.SendMessageEx(msg);
        await TestServerAvailabilitySub(msg);
    }

    private async Task TestServerAvailabilitySub(string msg)
    {
        RxSchedulers.MainThreadScheduler.Schedule(msg, (scheduler, msg) =>
        {
            _ = TestServerAvailabilityResult(msg);
            return Disposable.Empty;
        });
        await Task.CompletedTask;
    }

    public async Task TestServerAvailabilityResult(string msg)
    {
        RunningInfoDisplay = msg;
        await Task.CompletedTask;
    }

    #region System proxy and Routings

    private async Task SetListenerType(ESysProxyType type)
    {
        if (_config.SystemProxyItem.SysProxyType == type)
        {
            return;
        }
        _config.SystemProxyItem.SysProxyType = type;
        await ChangeSystemProxyAsync(type, true);
        NoticeManager.Instance.SendMessageEx($"{ResUI.TipChangeSystemProxy} - {_config.SystemProxyItem.SysProxyType}");

        SystemProxySelected = (int)_config.SystemProxyItem.SysProxyType;
        await ConfigHandler.SaveConfig(_config);
    }

    public async Task ChangeSystemProxyAsync(ESysProxyType type, bool blChange)
    {
        await SysProxyHandler.UpdateSysProxy(_config, false);

        BlSystemProxyClear = type == ESysProxyType.ForcedClear;
        BlSystemProxySet = type == ESysProxyType.ForcedChange;
        BlSystemProxyNothing = type == ESysProxyType.Unchanged;
        BlSystemProxyPac = type == ESysProxyType.Pac;

        if (blChange)
        {
            try
            {
                await DispatcherRefreshIconInteraction.Handle(Unit.Default);
            }
            catch (UnhandledInteractionException<Unit, Unit>)
            {
                // Ignore
            }
        }
    }

    public async Task RefreshRoutingsMenu()
    {
        var routings = await AppManager.Instance.RoutingItems();

        RoutingItems.Clear();
        RoutingItems.AddRange(routings);

        SelectedRouting = routings.FirstOrDefault(t => t.IsActive == true);
    }

    private async Task RoutingSelectedChangedAsync(bool c)
    {
        if (!c)
        {
            return;
        }

        if (SelectedRouting == null)
        {
            return;
        }

        var item = await AppManager.Instance.GetRoutingItem(SelectedRouting?.Id);
        if (item is null)
        {
            return;
        }

        if (await ConfigHandler.SetDefaultRouting(_config, item) == 0)
        {
            NoticeManager.Instance.SendMessageEx(ResUI.TipChangeRouting);
            ReloadRequested.Publish();
            await DispatcherRefreshIconInteraction.Handle(Unit.Default);
        }
    }

    private async Task DoSystemProxySelected(bool c)
    {
        if (!c)
        {
            return;
        }
        if (_config.SystemProxyItem.SysProxyType == (ESysProxyType)SystemProxySelected)
        {
            return;
        }
        await SetListenerType((ESysProxyType)SystemProxySelected);
    }

    private async Task DoEnableTun(bool c)
    {
        if (_config.TunModeItem.EnableTun == EnableTun)
        {
            return;
        }

        _config.TunModeItem.EnableTun = EnableTun;

        if (EnableTun && AllowEnableTun() == false)
        {
            // When running as a non-administrator, reboot to administrator mode
            if (Utils.IsWindows())
            {
                _config.TunModeItem.EnableTun = false;
                await AppManager.Instance.RebootAsAdmin();
                return;
            }
            else
            {
                var password = await PasswordInputInteraction.Handle(Unit.Default);
                if (password.IsNullOrEmpty())
                {
                    _config.TunModeItem.EnableTun = false;
                    return;
                }
            }
        }

        await ConfigHandler.SaveConfig(_config);
        ReloadRequested.Publish();
    }

    private bool AllowEnableTun()
    {
        if (Utils.IsWindows())
        {
            return Utils.IsAdministrator();
        }
        else if (Utils.IsLinux())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        else if (Utils.IsMacOS())
        {
            return AppManager.Instance.LinuxSudoPwd.IsNotEmpty();
        }
        return false;
    }

    #endregion System proxy and Routings

    #region UI

    public async Task InboundDisplayStatus()
    {
        StringBuilder sb = new();
        sb.Append($"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}");
        if (_config.Inbound.First().SecondLocalPortEnabled)
        {
            sb.Append($",{AppManager.Instance.GetLocalPort(EInboundProtocol.socks2)}");
        }
        sb.Append(']');
        InboundDisplay = $"{ResUI.LabLocal}:{sb}";

        if (_config.Inbound.First().AllowLANConn)
        {
            var lan = _config.Inbound.First().NewPort4LAN
                ? $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks3)}]"
                : $"[{EInboundProtocol.mixed}:{AppManager.Instance.GetLocalPort(EInboundProtocol.socks)}]";
            InboundLanDisplay = $"{ResUI.LabLAN}:{lan}";
        }
        else
        {
            InboundLanDisplay = $"{ResUI.LabLAN}:{Global.None}";
        }
        await Task.CompletedTask;
    }

    public async Task UpdateStatistics(ServerSpeedItem update)
    {
        try
        {
            if (!CoreManager.Instance.IsRunning)
            {
                _lastStatisticsAtUtc = DateTime.MinValue;
                ResetLiveTrafficDisplay();
                return;
            }
            SpeedProxyDisplay = FormatLiveTraffic(update.ProxyUp, update.ProxyDown);
            SpeedDirectDisplay = FormatLiveTraffic(update.DirectUp, update.DirectDown);
            ActiveNodeTrafficDisplay = _config.GuiItem.EnableStatistics
                ? FormatPeriodTraffic(update.TodayUp, update.TodayDown, update.MonthUp, update.MonthDown)
                : GetUnavailableTrafficDisplay();
            _lastStatisticsAtUtc = DateTime.UtcNow;
        }
        catch
        {
        }
        await Task.CompletedTask;
    }

    public void RefreshLiveTrafficState(bool connected)
    {
        RefreshActiveNodeTrafficDisplay();
        if (!connected)
        {
            _lastStatisticsAtUtc = DateTime.MinValue;
            ResetLiveTrafficDisplay();
        }
        else if (DateTime.UtcNow - _lastStatisticsAtUtc > TimeSpan.FromSeconds(2.5))
        {
            SpeedProxyDisplay = "统计不可用";
            SpeedDirectDisplay = "统计不可用";
        }
    }

    private void ResetLiveTrafficDisplay()
    {
        SpeedProxyDisplay = FormatLiveTraffic(0, 0);
        SpeedDirectDisplay = FormatLiveTraffic(0, 0);
    }

    private static string FormatLiveTraffic(long up, long down) =>
        $"↑ {Utils.HumanFy(Math.Max(0, up))}/s  ↓ {Utils.HumanFy(Math.Max(0, down))}/s";

    private void RefreshActiveNodeTrafficDisplay()
    {
        if (!_config.GuiItem.EnableStatistics)
        {
            ActiveNodeTrafficDisplay = GetUnavailableTrafficDisplay();
            return;
        }

        if (_config.IndexId.IsNullOrEmpty())
        {
            ActiveNodeTrafficDisplay = "未选择活动节点";
            return;
        }

        var stat = StatisticsManager.Instance.ServerStat?.FirstOrDefault(item => item.IndexId == _config.IndexId);
        if (stat is null)
        {
            ActiveNodeTrafficDisplay = FormatPeriodTraffic(0, 0, 0, 0);
            return;
        }

        var now = DateTime.Now;
        var today = ServerTrafficPeriod.GetTodayValues(stat, now);
        var monthUp = stat.MonthNow == ServerTrafficPeriod.GetMonthKey(now) ? stat.MonthUp : 0;
        var monthDown = stat.MonthNow == ServerTrafficPeriod.GetMonthKey(now) ? stat.MonthDown : 0;
        ActiveNodeTrafficDisplay = FormatPeriodTraffic(today.Up, today.Down, monthUp, monthDown);
    }

    private static string GetUnavailableTrafficDisplay() => "流量统计未启用";

    private static string FormatPeriodTraffic(long todayUp, long todayDown, long monthUp, long monthDown) =>
        $"今日 ↑ {FormatStoredTraffic(todayUp)}  ↓ {FormatStoredTraffic(todayDown)}  ·  "
        + $"本月 ↑ {FormatStoredTraffic(monthUp)}  ↓ {FormatStoredTraffic(monthDown)}";

    private static string FormatStoredTraffic(long kilobytes)
    {
        return Utils.HumanFy(Math.Max(0, kilobytes));
    }

    #endregion UI
}
