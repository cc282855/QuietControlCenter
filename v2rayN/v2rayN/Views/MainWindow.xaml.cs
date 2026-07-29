using System.Reactive.Disposables;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using v2rayN.Base;
using v2rayN.Manager;
using v2rayN.Services;

namespace v2rayN.Views;

public partial class MainWindow
{
    private static Config _config;
    private readonly SerialDisposable _layoutBindingsDisposable = new();
    private CheckUpdateView? _checkUpdateView;
    private BackupAndRestoreView? _backupAndRestoreView;

    public MainWindow()
    {
        InitializeComponent();

        txtAppVersion.Text = Utils.GetVersion();

        _config = AppManager.Instance.Config;
        ThreadPool.RegisterWaitForSingleObject(App.ProgramStarted, OnProgramStarted, null, -1, false);

        App.Current.SessionEnding += Current_SessionEnding;
        Closing += MainWindow_Closing;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        menuSettingsSetUWP.Click += MenuSettingsSetUWP_Click;
        menuPromotion.Click += MenuPromotion_Click;
        menuClose.Click += MenuClose_Click;
        menuCheckUpdate.Click += MenuCheckUpdate_Click;
        btnNewUpdate.Click += MenuCheckUpdate_Click;
        menuBackupAndRestore.Click += MenuBackupAndRestore_Click;

        pbTheme.Content ??= new ThemeSettingView();

        this.WhenActivated(disposables =>
        {
            // ReactiveWindow command bindings target ViewModel directly, while
            // the dashboard's XAML status bindings resolve through DataContext.
            // Keep both surfaces on the same live view model.
            DataContext = ViewModel;
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SpeedProxyDisplay)
                .Select(value => value.IsNullOrEmpty() ? "↑ 0 B/s" : value)
                .BindTo(this, v => v.txtHeroProxySpeed.Text)
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.SpeedDirectDisplay)
                .Select(value => value.IsNullOrEmpty() ? "↓ 0 B/s" : value)
                .BindTo(this, v => v.txtHeroDirectSpeed.Text)
                .DisposeWith(disposables);
            this.WhenAnyValue(v => v.ViewModel.StatusBarViewModel.RunningInfoDisplay)
                .Select(value => value.IsNullOrEmpty() ? "未连接" : value)
                .BindTo(this, v => v.txtHeroRunningStatus.Text)
                .DisposeWith(disposables);
            var connectionStateTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            connectionStateTimer.Tick += (_, _) => UpdateConnectionStateBadge();
            connectionStateTimer.Start();
            UpdateConnectionStateBadge();
            Disposable.Create(connectionStateTimer.Stop).DisposeWith(disposables);
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
              .Subscribe(_ => StorageUI())
              .DisposeWith(disposables);

            AppEvents.ShutdownRequested
             .AsObservable()
             .ObserveOn(RxSchedulers.MainThreadScheduler)
             .Subscribe(Shutdown)
             .DisposeWith(disposables);
        });

        Title = $"{Utils.GetVersion()} - {(Utils.IsAdministrator() ? ResUI.RunAsAdmin : ResUI.NotRunAsAdmin)}";
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
        Logging.SaveLog("Current_SessionEnding");
        StorageUI();
        await AppManager.Instance.AppExitAsync(false);
    }

    private void Shutdown(bool obj)
    {
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
        _checkUpdateView ??= new CheckUpdateView();
        _checkUpdateView.ViewModel = ViewModel?.CheckUpdateViewModel;
        CheckUpdateView.RemoveOfficialGuiUpdate(_checkUpdateView.ViewModel);
        DialogHost.Show(_checkUpdateView, "RootDialog");

        AppEvents.HasUpdateNotified.Publish(false);
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
        _ = CheckQuietUpdatesAsync();
    }

    private async Task CheckQuietUpdatesAsync()
    {
        var notices = await new QuietUpdateService().CheckAsync(Utils.GetVersion());
        foreach (var notice in notices)
        {
            MainSnackbar.MessageQueue?.Enqueue(notice, null, null, null, false, true, TimeSpan.FromSeconds(12));
        }
    }

    private void UpdateConnectionStateBadge()
    {
        var connected = Enum.GetValues<ECoreType>()
            .Where(coreType => coreType != ECoreType.v2rayN)
            .Any(AppManager.Instance.IsRunningCore);
        txtHeroConnectionState.Text = connected ? "已连接" : "未连接";
        txtHeroConnectionState.Foreground = connected
            ? (Brush)FindResource("QccSuccess")
            : (Brush)FindResource("QccMuted");
        borderHeroConnectionState.Background = new SolidColorBrush(
            connected ? Color.FromRgb(234, 248, 239) : Color.FromRgb(242, 244, 247));
        borderHeroConnectionState.ToolTip = ViewModel?.StatusBarViewModel?.RunningInfoDisplay;
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
