using System.Reactive.Disposables;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;
using v2rayN.Manager;
using v2rayN.Services;
using ServiceLib.Services;

namespace v2rayN.Views;

public partial class MainWindow
{
    private static Config _config;
    private readonly SerialDisposable _layoutBindingsDisposable = new();
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;
    private readonly CancellationTokenSource _quietUpdateCancellation = new();
    private readonly QuietUpdateService _quietUpdateService = new();
    private readonly CancellationTokenSource _liveMetricsCancellation = new();
    private readonly ProxyPingClient _livePingClient;
    private readonly ConnectionQualityMonitor _connectionQualityMonitor;
    private readonly SubscriptionQuotaService _subscriptionQuotaService = new();
    private readonly SemaphoreSlim _subscriptionQuotaSingleFlight = new(1, 1);
    private Task? _quietUpdateLoop;
    private QuietUpdateResult? _lastHandledQuietUpdateResult;
    private CancellationTokenSource? _subscriptionQuotaRequestCancellation;
    private Task? _subscriptionQuotaRefreshTask;
    private SubscriptionQuotaResult? _subscriptionQuotaResult;
    private DateTimeOffset? _subscriptionQuotaLastCompletedUtc;
    private string _subscriptionQuotaProfileId = string.Empty;
    private string _subscriptionQuotaSubId = string.Empty;
    private long _subscriptionQuotaGeneration;
    private double _responsiveFontScale = -1;
    private bool _subscriptionQuotaQaMode = Environment.GetCommandLineArgs()
        .Contains("--qcc-qa-quota-sample", StringComparer.Ordinal);
    private static readonly TimeSpan SubscriptionQuotaRefreshInterval = TimeSpan.FromMinutes(5);
    private static readonly DateTimeOffset SubscriptionQuotaQaRenderTime =
        new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero);

    public MainWindow()
    {
        InitializeComponent();
        ApplyResponsiveTypography(Width, Height);

        txtAppVersion.Text = Utils.GetVersion();

        _config = AppManager.Instance.Config;
        _livePingClient = new ProxyPingClient();
        _connectionQualityMonitor = new ConnectionQualityMonitor(_livePingClient, 10);
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuClose.Click += MenuClose_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;
        Loaded += MainWindow_Loaded;

        pbTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            // ReactiveWindow command bindings target ViewModel directly, while
            // the dashboard's XAML status bindings resolve through DataContext.
            // Keep both surfaces on the same live view model.
            DataContext = ViewModel;
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SpeedProxyDisplay)
                .Select(value => value.IsNullOrEmpty() ? "↑ 0.0 B/s  ↓ 0.0 B/s" : value)
                .BindTo(this, v => v.txtHeroProxySpeed.Text)
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SpeedDirectDisplay)
                .Select(value => value.IsNullOrEmpty() ? "↑ 0.0 B/s  ↓ 0.0 B/s" : value)
                .BindTo(this, v => v.txtHeroDirectSpeed.Text)
                .DisposeWith(disposables);
            var connectionStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            connectionStateTimer.Tick += LiveMetricsTimer_Tick;
            connectionStateTimer.Start();
            UpdateConnectionStateBadge();
            UpdateSubscriptionQuotaAgeAndSchedule();
            Disposable.Create(() =>
            {
                connectionStateTimer.Stop();
                connectionStateTimer.Tick -= LiveMetricsTimer_Tick;
            }).DisposeWith(disposables);
            //servers
            this.BindCommand(ViewModel, vm => vm.AddVmessServerCmd, v => v.menuAddVmessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddVlessServerCmd, v => v.menuAddVlessServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddShadowsocksServerCmd, v => v.menuAddShadowsocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddSocksServerCmd, v => v.menuAddSocksServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHttpServerCmd, v => v.menuAddHttpServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTrojanServerCmd, v => v.menuAddTrojanServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddHysteria2ServerCmd, v => v.menuAddHysteria2Server).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddTuicServerCmd, v => v.menuAddTuicServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddWireguardServerCmd, v => v.menuAddWireguardServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddAnytlsServerCmd, v => v.menuAddAnytlsServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddNaiveServerCmd, v => v.menuAddNaiveServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddCustomServerCmd, v => v.menuAddCustomServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddPolicyGroupServerCmd, v => v.menuAddPolicyGroupServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddProxyChainServerCmd, v => v.menuAddProxyChainServer).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaClipboardCmd, v => v.menuAddServerViaClipboard).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaScanCmd, v => v.menuAddServerViaScan).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.AddServerViaImageCmd, v => v.menuAddServerViaImage).DisposeWith(disposables);

            //sub
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.menuSubSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubSettingCmd, v => v.btnNavSubscription).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.btnNavSubscriptionUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateCmd, v => v.menuSubUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubUpdateViaProxyCmd, v => v.menuSubUpdateViaProxy).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateCmd, v => v.menuSubGroupUpdate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.SubGroupUpdateViaProxyCmd, v => v.menuSubGroupUpdateViaProxy).DisposeWith(disposables);

            //setting
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.menuOptionSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OptionSettingCmd, v => v.btnNavSettings).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.menuRoutingSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RoutingSettingCmd, v => v.btnNavRouting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.DNSSettingCmd, v => v.menuDNSSetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.FullConfigTemplateCmd, v => v.menuFullConfigTemplate).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.GlobalHotkeySettingCmd, v => v.menuGlobalHotkeySetting).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RebootAsAdminCmd, v => v.menuRebootAsAdmin).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.ClearServerStatisticsCmd, v => v.menuClearServerStatistics).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.OpenTheFileLocationCmd, v => v.menuOpenTheFileLocation).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetDefaultCmd, v => v.menuRegionalPresetsDefault).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetRussiaCmd, v => v.menuRegionalPresetsRussia).DisposeWith(disposables);
            this.BindCommand(ViewModel, vm => vm.RegionalPresetIranCmd, v => v.menuRegionalPresetsIran).DisposeWith(disposables);

            this.BindCommand(ViewModel, vm => vm.ReloadCmd, v => v.menuReload).DisposeWith(disposables);
            this.OneWayBind(ViewModel, vm => vm.BlReloadEnabled, v => v.menuReload.IsEnabled).DisposeWith(disposables);

            this.OneWayBind(ViewModel, vm => vm.BlNewUpdate, v => v.btnNewUpdate.Visibility).DisposeWith(disposables);

            _layoutBindingsDisposable.DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.MainGirdOrientation)
                .ObserveOn(RxSchedulers.MainThreadScheduler)
                .Subscribe(UpdateLayout)
                .DisposeWith(disposables);

            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel)
                .Subscribe(vm => ViewHost.Show(contentStatusBarView, vm))
                .DisposeWith(disposables);

            ViewModel.ReadTextFromClipboardInteraction.RegisterHandler(interaction =>
            {
                var clipboardData = WindowsUtils.GetClipboardData();
                interaction.SetOutput(clipboardData);
            }).DisposeWith(disposables);

            ViewModel.ScanScreenInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(false);
                if (Application.Current?.MainWindow is { } window)
                {
                    var bytes = QRCodeWindowsUtils.CaptureScreen(window);
                    interaction.SetOutput(bytes);
                }
                ShowHideWindow(true);
            }).DisposeWith(disposables);

            ViewModel.BrowseImageFileInteraction.RegisterHandler(interaction =>
            {
                if (UI.OpenFileDialog(out var fileName, "PNG|*.png|All|*.*") != true)
                {
                    interaction.SetOutput(null);
                    return;
                }
                interaction.SetOutput(fileName);
            }).DisposeWith(disposables);

            ViewModel.ShowHideWindowInteraction.RegisterHandler(interaction =>
            {
                ShowHideWindow(interaction.Input);
                interaction.SetOutput(Unit.Default);
            }).DisposeWith(disposables);

            AppEvents.SendSnackMsgRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(async content => await DelegateSnackMsg(content))
              .DisposeWith(disposables);

            AppEvents.AppExitRequested
              .AsObservable()
              .ObserveOn(RxSchedulers.MainThreadScheduler)
              .Subscribe(_ =>
              {
                  StopLiveMetrics();
                  StorageUI();
              })
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
             .AsObservable()
             .ObserveOn(RxSchedulers.MainThreadScheduler)
             .Subscribe(Shutdown)
             .DisposeWith(disposables);
        });

        Title = $"米卡 - {Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";
        if (_config.UiItem.AutoHideStartup)
        {
            WindowState = WindowState.Minimized;
        }

        if (!_config.GuiItem.EnableHWA)
        {
            RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        }

        AddHelpMenuItem();
        WindowsManager.Instance.RegisterGlobalHotkey(_config, OnHotkeyHandler, null);
    }

    #region Event

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveTypography(e.NewSize.Width, e.NewSize.Height);
    }

    private void ApplyResponsiveTypography(double width, double height)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0)
        {
            return;
        }

        var rawScale = Math.Min(width / 1120d, height / 720d);
        var scale = Math.Clamp(rawScale, 0.92d, 1.18d);
        scale = Math.Round(scale * 20d, MidpointRounding.AwayFromZero) / 20d;
        scale = Math.Clamp(scale, 0.92d, 1.18d);
        if (Math.Abs(scale - _responsiveFontScale) < 0.001d)
        {
            return;
        }

        _responsiveFontScale = scale;
        Resources["QccFontTiny"] = 10.5d * scale;
        Resources["QccFontSmall"] = 11d * scale;
        Resources["QccFontBody"] = 12d * scale;
        Resources["QccFontStrong"] = 13d * scale;
        Resources["QccFontTitle"] = 14d * scale;
        Resources["QccFontHero"] = 16d * scale;
        Resources["QccFontLarge"] = 18d * scale;
        Resources["QccLineTitle"] = 17d * scale;
        Resources["QccLineHero"] = 19d * scale;
        Resources["StdFontSize-1"] = 11d * scale;
        Resources["StdFontSize"] = 12d * scale;
        Resources["StdFontSize1"] = 13d * scale;
    }

    private void OnProgramStarted(object state, bool timeout)
    {
        Application.Current?.Dispatcher.Invoke(() =>
        {
            ShowHideWindow(true);
        });
    }

    private async Task DelegateSnackMsg(string content)
    {
        MainSnackbar.MessageQueue?.Enqueue(content);
        await Task.CompletedTask;
    }

    private void OnHotkeyHandler(EGlobalHotkey e)
    {
        switch (e)
        {
            case EGlobalHotkey.ShowForm:
                ShowHideWindow(null);
                break;

            case EGlobalHotkey.SystemProxyClear:
            case EGlobalHotkey.SystemProxySet:
            case EGlobalHotkey.SystemProxyUnchanged:
            case EGlobalHotkey.SystemProxyPac:
                AppEvents.SysProxyChangeRequested.Publish((ESysProxyType)((int)e - 1));
                break;
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        e.Cancel = true;
        ShowHideWindow(false);
    }

    private async void Current_SessionEnding(object sender, SessionEndingCancelEventArgs e)
    {
        _quietUpdateCancellation.Cancel();
        CancelSubscriptionQuotaRequest();
        StopLiveMetrics();
        Logging.SaveLog("Current_SessionEnding");
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
        _quietUpdateCancellation.Cancel();
        CancelSubscriptionQuotaRequest();
        StopLiveMetrics();
        Application.Current.Shutdown();
    }

    private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
        {
            switch (e.Key)
            {
                case Key.V:
                    if (Keyboard.FocusedElement is TextBox)
                    {
                        return;
                    }
                    AddServerViaClipboardAsync().ContinueWith(_ => { });

                    break;

                case Key.S:
                    ScanScreenTaskAsync().ContinueWith(_ => { });
                    break;
            }
        }
        else
        {
            if (e.Key == Key.F5)
            {
                ViewModel?.Reload();
            }
        }
    }

    private void MenuClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleWindowState();
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void WindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void WindowMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleWindowState();
    }

    private void WindowClose_Click(object sender, RoutedEventArgs e)
    {
        StorageUI();
        ShowHideWindow(false);
    }

    private void ToggleWindowState()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        btnWindowMaximize.ToolTip = WindowState == WindowState.Maximized ? "还原" : "最大化";
    }

    private void ShowProfiles_Click(object sender, RoutedEventArgs e)
    {
        tabMain.Visibility = Visibility.Collapsed;
        tabProfiles.Visibility = Visibility.Visible;
        SetActiveNavigation(sender as Button ?? btnNavHome);
    }

    private void ShowLogs_Click(object sender, RoutedEventArgs e)
    {
        tabProfiles.Visibility = Visibility.Collapsed;
        tabMain.Visibility = Visibility.Visible;
        tabMain.SelectedIndex = 0;
        SetActiveNavigation(btnNavLogs);
    }

    private void ShowConnections_Click(object sender, RoutedEventArgs e)
    {
        tabProfiles.Visibility = Visibility.Collapsed;
        tabMain.Visibility = Visibility.Visible;
        tabMain.SelectedIndex = 2;
        SetActiveNavigation(btnNavConnections);
    }

    private void ShowSubscription_Click(object sender, RoutedEventArgs e) => SetActiveNavigation(btnNavSubscription);

    private void ShowRouting_Click(object sender, RoutedEventArgs e) => SetActiveNavigation(btnNavRouting);

    private void ShowSettings_Click(object sender, RoutedEventArgs e) => SetActiveNavigation(btnNavSettings);

    private void SetActiveNavigation(Button activeButton)
    {
        var normalStyle = (Style)FindResource("QccNavButton");
        var activeStyle = (Style)FindResource("QccNavButtonActive");
        foreach (var button in new[] { btnNavHome, btnNavNodes, btnNavSubscription, btnNavRouting, btnNavConnections, btnNavLogs, btnNavSettings })
        {
            button.Style = ReferenceEquals(button, activeButton) ? activeStyle : normalStyle;
        }
    }

    private void MenuPromotion_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart($"{Utils.Base64Decode(Global.PromotionUrl)}?t={DateTime.Now.Ticks}");
    }

    private void MenuSettingsSetUWP_Click(object sender, RoutedEventArgs e)
    {
        ProcUtils.ProcessStart(Utils.GetBinPath("EnableLoopback.exe"));
    }

    public async Task AddServerViaClipboardAsync()
    {
        var clipboardData = WindowsUtils.GetClipboardData();
        if (clipboardData.IsNotEmpty() && ViewModel != null)
        {
            await ViewModel.AddServerViaClipboardAsync(clipboardData);
        }
    }

    private async Task ScanScreenTaskAsync()
    {
        ShowHideWindow(false);

        if (Application.Current?.MainWindow is Window window)
        {
            var bytes = QRCodeWindowsUtils.CaptureScreen(window);
            await ViewModel?.ScanScreenResult(bytes);
        }

        ShowHideWindow(true);
    }

    private void MenuCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        PrepareCoreUpdatePopup();
        pbCoreUpdate.IsPopupOpen = true;
        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Build the core-update view while the application is idle. Opening the
        // compact popup then only changes visibility; it never builds a dialog
        // overlay or initializes its binding tree on the click path.
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
            new Action(PrepareCoreUpdatePopup));
    }

    private void CoreUpdatePopup_Opened(object sender, RoutedEventArgs e)
    {
        PrepareCoreUpdatePopup();
        AppEvents.HasUpdateNotified.Publish(false);
    }

    private void PrepareCoreUpdatePopup()
    {
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        CheckUpdateView.RemoveOfficialGuiUpdate(_checkUpdateView.ViewModel);
        contentCoreUpdate.Content ??= _checkUpdateView;
    }

    private async void Disconnect_Click(object sender, RoutedEventArgs e)
    {
        btnDisconnect.IsEnabled = false;
        try
        {
            await CoreManager.Instance.CoreStop();
            MainSnackbar.MessageQueue?.Enqueue("代理核心已停止；重新载入配置可再次连接");
        }
        catch (Exception ex)
        {
            Logging.SaveLog("DisconnectCore", ex);
            MainSnackbar.MessageQueue?.Enqueue("断开连接失败，请查看日志");
        }
        finally
        {
            btnDisconnect.IsEnabled = true;
        }
    }

    private void MenuBackupAndRestore_Click(object sender, RoutedEventArgs e)
    {
        _backupAndRestoreView ??= new BackupAndRestoreView();
        _backupAndRestoreView.ViewModel = ViewModel?.BackupAndRestoreViewModel;
        DialogHost.Show(_backupAndRestoreView, "RootDialog");
    }

    #endregion Event

    #region UI

    public void ShowHideWindow(bool? blShow)
    {
        var bl = blShow ?? !AppManager.Instance.ShowInTaskbar;
        if (bl)
        {
            this?.Show();
            if (this?.WindowState == WindowState.Minimized)
            {
                WindowState = WindowState.Normal;
            }
            this?.Activate();
            this?.Focus();
        }
        else
        {
            this?.Hide();
        }
        AppManager.Instance.ShowInTaskbar = bl;
    }

    protected override void OnLoaded(object? sender, RoutedEventArgs e)
    {
        base.OnLoaded(sender, e);
        if (_config.UiItem.AutoHideStartup)
        {
            ShowHideWindow(false);
        }
        RestoreUI();
        WriteStartupAcknowledgement();
        _ = RefreshQuietUpdateStatusAsync();
        _quietUpdateLoop ??= RunQuietUpdateLoopAsync();
        _subscriptionQuotaQaMode = ApplyQaSubscriptionQuotaSampleIfRequested(Environment.GetCommandLineArgs());
        if (!_subscriptionQuotaQaMode)
        {
            ScheduleSubscriptionQuotaRefresh(force: false);
        }
        _ = CaptureQaFrameIfRequestedAsync();
    }

    private async Task RunQuietUpdateLoopAsync()
    {
        var scheduler = new QuietUpdateScheduler(_quietUpdateService, new SystemQuietDelay());
        try
        {
            await scheduler.RunAsync(Utils.GetVersion(), HandleQuietUpdateResultAsync, _quietUpdateCancellation.Token);
        }
        catch (OperationCanceledException) { }
    }

    private async void QuietUpdatePopup_Opened(object sender, RoutedEventArgs e)
    {
        await RefreshQuietUpdateStatusAsync();
    }

    private async void QuietUpdateCheckNow_Click(object sender, RoutedEventArgs e)
    {
        pbQuietUpdate.IsPopupOpen = true;
        btnQuietUpdateCheckNow.IsEnabled = false;
        txtQuietUpdateStatus.Text = "正在检查…";
        string? fallbackMessage = null;
        try
        {
            var result = await _quietUpdateService.CheckNowAsync(Utils.GetVersion(), _quietUpdateCancellation.Token);
            await HandleQuietUpdateResultAsync(result);
        }
        catch (OperationCanceledException) when (_quietUpdateCancellation.IsCancellationRequested) { }
        catch
        {
            fallbackMessage = "更新检查失败，请稍后重试";
        }
        finally
        {
            if (!_quietUpdateCancellation.IsCancellationRequested)
            {
                btnQuietUpdateCheckNow.IsEnabled = true;
                await RefreshQuietUpdateStatusAsync();
                if (!string.IsNullOrWhiteSpace(fallbackMessage))
                {
                    txtQuietUpdateStatus.Text = fallbackMessage;
                }
                pbQuietUpdate.IsPopupOpen = true;
            }
        }
    }

    private async Task HandleQuietUpdateResultAsync(QuietUpdateResult result)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (ReferenceEquals(_lastHandledQuietUpdateResult, result)) return;
            _lastHandledQuietUpdateResult = result;
            foreach (var notice in result.Notices)
                MainSnackbar.MessageQueue?.Enqueue(notice, null, null, null, false, true, TimeSpan.FromSeconds(12));
            if (result.UpgradeStarted)
            {
                StorageUI();
                _quietUpdateCancellation.Cancel();
                CancelSubscriptionQuotaRequest();
                StopLiveMetrics();
                Application.Current.Shutdown();
            }
        });
        if (!result.UpgradeStarted) await RefreshQuietUpdateStatusAsync();
    }

    private async Task RefreshQuietUpdateStatusAsync()
    {
        QuietUpdateStatus status;
        try { status = await _quietUpdateService.GetStatusAsync(_quietUpdateCancellation.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_quietUpdateCancellation.IsCancellationRequested) { return; }
        catch { status = _quietUpdateService.Snapshot with { LastError = "更新状态读取失败", IsChecking = false }; }

        await Dispatcher.InvokeAsync(() =>
        {
            txtQuietUpdateCurrentVersion.Text = Utils.GetVersion();
            txtQuietUpdateOfficialVersion.Text = DisplayVersion(status.LatestOfficial);
            txtQuietUpdateCustomVersion.Text = DisplayVersion(status.LatestCustom);
            txtQuietUpdateLastAttempt.Text = DisplayUpdateTime(status.LastAttemptUtc);
            txtQuietUpdateLastSuccess.Text = DisplayUpdateTime(status.LastSuccessUtc);
            txtQuietUpdateStatus.Text = QuietUpdateService.GetStatusMessage(status, Utils.GetVersion());
        });
    }

    private static string DisplayVersion(string? version) => string.IsNullOrWhiteSpace(version) ? "尚未发现" : version;
    private static string DisplayUpdateTime(DateTimeOffset? value)
        => value is null ? "尚未检查" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");

    private static void WriteStartupAcknowledgement()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--qcc-startup-ack");
        if (index < 0 || index + 2 >= args.Length || !Path.IsPathFullyQualified(args[index + 1])) return;
        try
        {
            var ack = Path.GetFullPath(args[index + 1]);
            var tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QuietControlCenter"));
            var workRoot = Path.GetDirectoryName(ack);
            if (workRoot is not null
                && string.Equals(Directory.GetParent(workRoot)?.FullName, tempRoot, StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(Path.GetFileName(workRoot), "N", out _)
                && string.Equals(Path.GetFileName(ack), "startup.ack", StringComparison.Ordinal)
                && args[index + 2].Length == 48
                && args[index + 2].All(Uri.IsHexDigit))
                File.WriteAllText(ack, args[index + 2]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task CaptureQaFrameIfRequestedAsync()
    {
        var args = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(args, "--qcc-qa-capture");
        if (index < 0 || index + 3 >= args.Length || !Path.IsPathFullyQualified(args[index + 1])
            || !int.TryParse(args[index + 2], out var width) || !int.TryParse(args[index + 3], out var height)
            || width is < 900 or > 2000 || height is < 600 or > 1400) return;
        try
        {
            Width = width; Height = height; WindowState = WindowState.Normal;
            ApplyResponsiveTypography(width, height);
            UpdateLayout();
            if (args.Contains("--qcc-qa-reload", StringComparer.Ordinal)) await ViewModel.Reload();
            await Task.Delay(8000);
            UpdateConnectionStateBadge();
            ApplyQaQualitySampleIfRequested(args);
            if (args.Contains("--qcc-qa-open-update", StringComparer.Ordinal))
            {
                await RefreshQuietUpdateStatusAsync();
                pbQuietUpdate.IsPopupOpen = true;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
            }
            if (args.Contains("--qcc-qa-open-core-update", StringComparer.Ordinal))
            {
                PrepareCoreUpdatePopup();
                pbCoreUpdate.IsPopupOpen = true;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
            }
            UpdateLayout();
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using var stream = new FileStream(Path.GetFullPath(args[index + 1]), FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);
        }
        catch (Exception ex)
        {
            var errorPath = Path.GetFullPath(args[index + 1]) + ".error.txt";
            await File.WriteAllTextAsync(errorPath, ex.ToString());
        }
        finally
        {
            _quietUpdateCancellation.Cancel();
            CancelSubscriptionQuotaRequest();
            StopLiveMetrics();
            Application.Current.Shutdown();
        }
    }

    private void SubscriptionQuotaRefresh_Click(object sender, RoutedEventArgs e)
    {
        ScheduleSubscriptionQuotaRefresh(force: true);
    }

    private void UpdateSubscriptionQuotaAgeAndSchedule()
    {
        if (_subscriptionQuotaQaMode)
        {
            RenderSubscriptionQuota(DateTimeOffset.UtcNow);
            return;
        }

        var currentProfileId = _config.IndexId ?? string.Empty;
        if (!string.Equals(currentProfileId, _subscriptionQuotaProfileId, StringComparison.Ordinal))
        {
            CancelSubscriptionQuotaRequest();
            _subscriptionQuotaProfileId = currentProfileId;
            _subscriptionQuotaSubId = string.Empty;
            _subscriptionQuotaResult = null;
            _subscriptionQuotaLastCompletedUtc = null;
            RenderSubscriptionQuota(DateTimeOffset.UtcNow);
            ScheduleSubscriptionQuotaRefresh(force: true);
            return;
        }

        RenderSubscriptionQuota(DateTimeOffset.UtcNow);
        if (currentProfileId.Length == 0 || !CoreManager.Instance.IsRunning)
        {
            if (_subscriptionQuotaRefreshTask is { IsCompleted: false })
            {
                CancelSubscriptionQuotaRequest();
            }
            return;
        }
        if (!_subscriptionQuotaLastCompletedUtc.HasValue
            || DateTimeOffset.UtcNow - _subscriptionQuotaLastCompletedUtc.Value >= SubscriptionQuotaRefreshInterval)
        {
            ScheduleSubscriptionQuotaRefresh(force: false);
        }
    }

    private void ScheduleSubscriptionQuotaRefresh(bool force)
    {
        if (_subscriptionQuotaQaMode)
        {
            return;
        }

        var currentProfileId = _config.IndexId ?? string.Empty;
        if (currentProfileId.Length == 0)
        {
            _subscriptionQuotaProfileId = string.Empty;
            _subscriptionQuotaSubId = string.Empty;
            RenderSubscriptionQuota(DateTimeOffset.UtcNow);
            return;
        }
        if (!CoreManager.Instance.IsRunning)
        {
            RenderSubscriptionQuota(DateTimeOffset.UtcNow);
            return;
        }
        if (!string.Equals(currentProfileId, _subscriptionQuotaProfileId, StringComparison.Ordinal))
        {
            CancelSubscriptionQuotaRequest();
            _subscriptionQuotaProfileId = currentProfileId;
            _subscriptionQuotaSubId = string.Empty;
            _subscriptionQuotaResult = null;
            _subscriptionQuotaLastCompletedUtc = null;
            force = true;
        }
        if (!force && _subscriptionQuotaLastCompletedUtc.HasValue
            && DateTimeOffset.UtcNow - _subscriptionQuotaLastCompletedUtc.Value < SubscriptionQuotaRefreshInterval)
        {
            return;
        }
        if (_subscriptionQuotaRefreshTask is { IsCompleted: false })
        {
            if (!force)
            {
                return;
            }
            CancelSubscriptionQuotaRequest();
        }

        var generation = ++_subscriptionQuotaGeneration;
        var requestCancellation = new CancellationTokenSource();
        _subscriptionQuotaRequestCancellation = requestCancellation;
        _subscriptionQuotaLastCompletedUtc = null;
        _subscriptionQuotaRefreshTask = RefreshSubscriptionQuotaAsync(
            currentProfileId,
            generation,
            requestCancellation);
        RenderSubscriptionQuota(DateTimeOffset.UtcNow);
    }

    private async Task RefreshSubscriptionQuotaAsync(
        string profileId,
        long generation,
        CancellationTokenSource requestCancellation)
    {
        var entered = false;
        var subId = string.Empty;
        try
        {
            await _subscriptionQuotaSingleFlight.WaitAsync(requestCancellation.Token);
            entered = true;
            if (generation != _subscriptionQuotaGeneration
                || !string.Equals(profileId, _config.IndexId, StringComparison.Ordinal)
                || !CoreManager.Instance.IsRunning)
            {
                return;
            }

            var activeProfile = await AppManager.Instance.GetProfileItem(profileId);
            if (generation != _subscriptionQuotaGeneration
                || !string.Equals(profileId, _config.IndexId, StringComparison.Ordinal))
            {
                return;
            }
            subId = activeProfile?.Subid ?? string.Empty;
            if (subId.Length == 0)
            {
                await ApplySubscriptionQuotaResultAsync(
                    profileId,
                    subId,
                    generation,
                    new(SubscriptionQuotaStatusCode.InvalidRequest));
                return;
            }

            var subscription = await AppManager.Instance.GetSubItem(subId);
            if (generation != _subscriptionQuotaGeneration
                || !string.Equals(profileId, _config.IndexId, StringComparison.Ordinal)
                || !CoreManager.Instance.IsRunning)
            {
                return;
            }
            if (subscription is null || !subscription.Enabled || string.IsNullOrWhiteSpace(subscription.Url))
            {
                await ApplySubscriptionQuotaResultAsync(
                    profileId,
                    subId,
                    generation,
                    new(SubscriptionQuotaStatusCode.InvalidRequest));
                return;
            }

            var result = await _subscriptionQuotaService.FetchAsync(
                subscription.Url,
                useLocalSocksProxy: true,
                AppManager.Instance.GetLocalPort(EInboundProtocol.socks),
                subscription.UserAgent,
                requestCancellation.Token);
            await ApplySubscriptionQuotaResultAsync(profileId, subId, generation, result);
        }
        catch (OperationCanceledException) when (requestCancellation.IsCancellationRequested)
        {
        }
        catch
        {
            await ApplySubscriptionQuotaResultAsync(
                profileId,
                subId,
                generation,
                new(SubscriptionQuotaStatusCode.NetworkError));
        }
        finally
        {
            if (entered)
            {
                _subscriptionQuotaSingleFlight.Release();
            }
            if (ReferenceEquals(_subscriptionQuotaRequestCancellation, requestCancellation))
            {
                _subscriptionQuotaRequestCancellation = null;
                _subscriptionQuotaRefreshTask = null;
            }
            requestCancellation.Dispose();
        }
    }

    private async Task ApplySubscriptionQuotaResultAsync(
        string profileId,
        string subId,
        long generation,
        SubscriptionQuotaResult result)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (generation != _subscriptionQuotaGeneration
                || !string.Equals(profileId, _subscriptionQuotaProfileId, StringComparison.Ordinal)
                || !string.Equals(profileId, _config.IndexId, StringComparison.Ordinal))
            {
                return;
            }
            _subscriptionQuotaSubId = subId;
            _subscriptionQuotaResult = result;
            _subscriptionQuotaLastCompletedUtc = DateTimeOffset.UtcNow;
            RenderSubscriptionQuota(DateTimeOffset.UtcNow);
        });
    }

    private void CancelSubscriptionQuotaRequest()
    {
        _subscriptionQuotaGeneration++;
        var cancellation = _subscriptionQuotaRequestCancellation;
        if (cancellation is not null && !cancellation.IsCancellationRequested)
        {
            cancellation.Cancel();
        }
    }

    private void RenderSubscriptionQuota(DateTimeOffset now)
    {
        btnSubscriptionQuotaRefresh.IsEnabled = _subscriptionQuotaRefreshTask is not { IsCompleted: false };
        if (_subscriptionQuotaQaMode)
        {
            if (_subscriptionQuotaResult is not null)
            {
                RenderSubscriptionQuotaResult(_subscriptionQuotaResult, SubscriptionQuotaQaRenderTime);
            }
            else
            {
                txtSubscriptionQuotaPrimary.Text = "QA 样例待加载";
                txtSubscriptionQuotaSecondary.Text = "不读取配置或网络";
            }
            return;
        }
        if (string.IsNullOrEmpty(_config.IndexId))
        {
            txtSubscriptionQuotaPrimary.Text = "未选择活动节点";
            txtSubscriptionQuotaSecondary.Text = "连接订阅节点后显示";
            return;
        }
        if (!CoreManager.Instance.IsRunning)
        {
            txtSubscriptionQuotaPrimary.Text = "代理未连接";
            txtSubscriptionQuotaSecondary.Text = "连接后通过本地代理查询";
            return;
        }
        if (_subscriptionQuotaResult is null)
        {
            txtSubscriptionQuotaPrimary.Text = _subscriptionQuotaRefreshTask is { IsCompleted: false } ? "正在查询…" : "尚未查询";
            txtSubscriptionQuotaSecondary.Text = _subscriptionQuotaLastCompletedUtc.HasValue
                ? $"更新 {FormatSubscriptionQuotaAge(now - _subscriptionQuotaLastCompletedUtc.Value)}"
                : "等待安全代理查询";
            return;
        }
        RenderSubscriptionQuotaResult(_subscriptionQuotaResult, now);
    }

    private void RenderSubscriptionQuotaResult(SubscriptionQuotaResult result, DateTimeOffset now)
    {
        if (!result.IsSuccess)
        {
            txtSubscriptionQuotaPrimary.Text = SubscriptionQuotaService.GetFixedChineseMessage(result.Status);
            txtSubscriptionQuotaSecondary.Text = _subscriptionQuotaLastCompletedUtc.HasValue
                ? $"更新 {FormatSubscriptionQuotaAge(now - _subscriptionQuotaLastCompletedUtc.Value)}"
                : "未保存查询内容";
            return;
        }

        var snapshot = result.Snapshot!;
        var expired = snapshot.ExpiresAtUtc.HasValue && snapshot.ExpiresAtUtc.Value <= now;
        txtSubscriptionQuotaPrimary.Text = expired ? "订阅已过期" : FormatSubscriptionQuotaBytes(snapshot.RemainingBytes);
        var age = FormatSubscriptionQuotaAge(now - snapshot.RetrievedAtUtc);
        if (snapshot.TotalBytes.HasValue)
        {
            var used = snapshot.UploadBytes + snapshot.DownloadBytes;
            txtSubscriptionQuotaSecondary.Text = $"已用 {FormatSubscriptionQuotaBytes(used)} / {FormatSubscriptionQuotaBytes(snapshot.TotalBytes.Value)} · {age}";
        }
        else if (snapshot.ExpiresAtUtc.HasValue)
        {
            txtSubscriptionQuotaSecondary.Text = $"到期 {snapshot.ExpiresAtUtc.Value.ToLocalTime():yyyy-MM-dd} · {age}";
        }
        else
        {
            txtSubscriptionQuotaSecondary.Text = $"更新 {age}";
        }
    }

    private static string FormatSubscriptionQuotaBytes(ulong bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private static string FormatSubscriptionQuotaAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero || age < TimeSpan.FromSeconds(5)) return "刚刚";
        if (age < TimeSpan.FromMinutes(1)) return $"{(int)age.TotalSeconds} 秒前";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} 分钟前";
        return $"{(int)age.TotalHours} 小时前";
    }

    private bool ApplyQaSubscriptionQuotaSampleIfRequested(string[] args)
    {
        var index = Array.IndexOf(args, "--qcc-qa-quota-sample");
        if (index < 0 || index + 1 >= args.Length)
        {
            return false;
        }

        var now = SubscriptionQuotaQaRenderTime;
        _subscriptionQuotaProfileId = "qa-profile";
        _subscriptionQuotaSubId = "qa-sample";
        _subscriptionQuotaLastCompletedUtc = now;
        _subscriptionQuotaResult = args[index + 1] switch
        {
            "success" => new(
                SubscriptionQuotaStatusCode.Success,
                new(10UL * 1024 * 1024 * 1024, 20UL * 1024 * 1024 * 1024,
                    100UL * 1024 * 1024 * 1024, 70UL * 1024 * 1024 * 1024,
                    new DateTimeOffset(2027, 12, 31, 0, 0, 0, TimeSpan.Zero), now,
                    SubscriptionQuotaSource.Header)),
            "unsupported" => new(SubscriptionQuotaStatusCode.Unsupported),
            "expired" => new(
                SubscriptionQuotaStatusCode.Success,
                new(0, 0, null, 0, new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero), now,
                    SubscriptionQuotaSource.ResponseBody)),
            _ => null
        };
        if (_subscriptionQuotaResult is null)
        {
            return false;
        }
        RenderSubscriptionQuota(now);
        return true;
    }

    private void ApplyQaQualitySampleIfRequested(string[] args)
    {
        var index = Array.IndexOf(args, "--qcc-qa-quality-sample");
        if (index < 0 || index + 3 >= args.Length
            || !int.TryParse(args[index + 1], out var delayMs)
            || !int.TryParse(args[index + 2], out var jitterMs)
            || !int.TryParse(args[index + 3], out var lossPercent)
            || delayMs < 0 || jitterMs < 0 || lossPercent is < 0 or > 100)
        {
            return;
        }

        txtHeroConnectionState.Text = "已连接";
        txtHeroConnectionState.Foreground = (Brush)FindResource("QccSuccess");
        borderHeroConnectionState.Background = new SolidColorBrush(Color.FromRgb(234, 248, 239));
        txtHeroRunningStatus.Text = "sing-box 运行中";
        txtHeroDelay.Text = $"{delayMs} ms";
        txtHeroJitterLoss.Text = $"{jitterMs} ms / {lossPercent}%";
        txtHeroDelay.Foreground = GetMetricBrush(ConnectionQualitySeverityCalculator.GetDelaySeverity(delayMs));
        txtHeroJitterLoss.Foreground = GetMetricBrush(
            ConnectionQualitySeverityCalculator.GetJitterLossSeverity(jitterMs, lossPercent));
    }

    private async void LiveMetricsTimer_Tick(object? sender, EventArgs e)
    {
        var connected = UpdateConnectionStateBadge();
        UpdateSubscriptionQuotaAgeAndSchedule();
        ViewModel?.StatusBarViewModel.RefreshLiveTrafficState(connected);
        if (!connected)
        {
            _connectionQualityMonitor.Reset();
            ResetHeroQualityMetrics();
            return;
        }

        try
        {
            var snapshot = await _connectionQualityMonitor.SampleAsync(_liveMetricsCancellation.Token);
            if (snapshot is null)
            {
                return;
            }
            if (!CoreManager.Instance.IsRunning)
            {
                _connectionQualityMonitor.Reset();
                ResetHeroQualityMetrics();
                return;
            }
            txtHeroDelay.Text = snapshot.DelayMs is { } currentDelay ? $"{currentDelay} ms" : "超时";
            txtHeroJitterLoss.Text = snapshot.JitterMs is { } jitter
                ? $"{jitter} ms / {snapshot.LossPercent}%"
                : $"— / {snapshot.LossPercent}%";
            txtHeroDelay.Foreground = GetMetricBrush(ConnectionQualitySeverityCalculator.GetDelaySeverity(snapshot.DelayMs));
            txtHeroJitterLoss.Foreground = GetMetricBrush(
                ConnectionQualitySeverityCalculator.GetJitterLossSeverity(snapshot.JitterMs, snapshot.LossPercent));
        }
        catch (OperationCanceledException) when (_liveMetricsCancellation.IsCancellationRequested)
        {
        }
    }

    private void ResetHeroQualityMetrics()
    {
        txtHeroDelay.Text = "—";
        txtHeroJitterLoss.Text = "— / —";
        txtHeroDelay.Foreground = GetMetricBrush(ConnectionQualitySeverity.None);
        txtHeroJitterLoss.Foreground = GetMetricBrush(ConnectionQualitySeverity.None);
    }

    private Brush GetMetricBrush(ConnectionQualitySeverity severity)
    {
        var resourceKey = severity switch
        {
            ConnectionQualitySeverity.Good => "QccSuccess",
            ConnectionQualitySeverity.Warning => "QccWarning",
            ConnectionQualitySeverity.Danger => "QccDanger",
            _ => "QccMuted"
        };
        return (Brush)FindResource(resourceKey);
    }

    private void StopLiveMetrics()
    {
        if (!_liveMetricsCancellation.IsCancellationRequested)
        {
            _liveMetricsCancellation.Cancel();
        }
        _livePingClient.Dispose();
    }

    private bool UpdateConnectionStateBadge()
    {
        var connected = CoreManager.Instance.IsRunning;
        var coreName = AppManager.Instance.RunningCoreType switch
        {
            ECoreType.sing_box => "sing-box",
            ECoreType.mihomo => "Mihomo",
            ECoreType.Xray => "Xray",
            ECoreType.v2fly or ECoreType.v2fly_v5 => "v2fly",
            _ => AppManager.Instance.RunningCoreType.ToString()
        };
        txtHeroConnectionState.Text = connected ? "已连接" : "未连接";
        txtHeroRunningStatus.Text = connected ? $"{coreName} 运行中" : "未连接";
        txtHeroConnectionState.Foreground = connected
            ? (Brush)FindResource("QccSuccess")
            : (Brush)FindResource("QccMuted");
        borderHeroConnectionState.Background = new SolidColorBrush(
            connected ? Color.FromRgb(234, 248, 239) : Color.FromRgb(242, 244, 247));
        borderHeroConnectionState.ToolTip = txtHeroRunningStatus.Text;
        return connected;
    }

    private void RestoreUI()
    {
        // Quiet Control Center uses one stable workspace layout. Upstream layout
        // orientation settings are intentionally ignored so updates cannot alter it.
    }

    private void StorageUI()
    {
        ConfigHandler.SaveWindowSizeItem(_config, GetType().Name, Width, Height);
    }

    private void UpdateLayout(EGirdOrientation orientation)
    {
        var currentLayoutDisposables = new CompositeDisposable();
        _layoutBindingsDisposable.Disposable = currentLayoutDisposables;

        this.WhenAnyValue(v => v.ViewModel.ProfilesViewModel)
            .Subscribe(vm => ViewHost.Show(tabProfiles, vm))
            .DisposeWith(currentLayoutDisposables);
        this.WhenAnyValue(v => v.ViewModel.MsgViewModel)
            .Subscribe(vm => ViewHost.Show(tabMsgView, vm))
            .DisposeWith(currentLayoutDisposables);
        this.WhenAnyValue(v => v.ViewModel.ClashProxiesViewModel)
            .Subscribe(vm => ViewHost.Show(tabClashProxies, vm))
            .DisposeWith(currentLayoutDisposables);
        this.WhenAnyValue(v => v.ViewModel.ClashConnectionsViewModel)
            .Subscribe(vm => ViewHost.Show(tabClashConnections, vm))
            .DisposeWith(currentLayoutDisposables);

        RestoreUI();
    }

    private void AddHelpMenuItem()
    {
        var coreInfo = CoreInfoManager.Instance.GetCoreInfo();
        foreach (var it in coreInfo
            .Where(t => t.CoreType is not ECoreType.v2fly
                        and not ECoreType.hysteria))
        {
            var item = new MenuItem()
            {
                Tag = it.Url.Replace(@"/releases", ""),
                Header = string.Format(ResUI.menuWebsiteItem, it.CoreType.ToString().Replace("_", " ")).UpperFirstChar()
            };
            item.Click += MenuItem_Click;
            menuHelp.Items.Add(item);
        }
    }

    private void MenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem item)
        {
            ProcUtils.ProcessStart(item.Tag.ToString());
        }
    }

    #endregion UI
}
