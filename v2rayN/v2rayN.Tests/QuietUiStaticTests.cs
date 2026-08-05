using Xunit;

namespace v2rayN.Tests;

public sealed class QuietUiStaticTests
{
    [Fact]
    public void ProfileFooter_UsesExpectedLabelsAndCommands()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));

        Assert.Contains("x:Name=\"btnFastRealPing\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"测延迟\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"btnMixedTest\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"延迟+速度\"", xaml, StringComparison.Ordinal);
        Assert.Contains("vm => vm.FastRealPingCmd, v => v.btnFastRealPing", codeBehind, StringComparison.Ordinal);
        Assert.Contains("vm => vm.MixedTestServerCmd, v => v.btnMixedTest", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsEntryRemainsAndAdvancedSettingsButtonStaysRemoved()
    {
        var root = FindProjectRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var statusXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "StatusBarView.xaml"));

        Assert.Contains("x:Name=\"btnNavSettings\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("vm => vm.OptionSettingCmd, v => v.btnNavSettings", mainCode, StringComparison.Ordinal);
        Assert.DoesNotContain("btnAdvancedSettings", statusXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileColumnEventOnlyAppliesVisibilityWithoutRestoringLayout()
    {
        var root = FindProjectRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));

        Assert.Contains(".Subscribe(_ => RefreshProfileColumnControls())", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain(".Subscribe(_ => RestoreUI())", codeBehind, StringComparison.Ordinal);
        Assert.Contains("private void ApplyProfileColumnVisibility()", codeBehind, StringComparison.Ordinal);
        Assert.Contains(": Visibility.Collapsed;", codeBehind, StringComparison.Ordinal);

        var restoreStart = codeBehind.IndexOf("private void RestoreUI()", StringComparison.Ordinal);
        var visibilityMethodStart = codeBehind.IndexOf("private void ApplyProfileColumnVisibility()", StringComparison.Ordinal);
        Assert.True(restoreStart >= 0 && visibilityMethodStart > restoreStart);
        var restoreMethod = codeBehind[restoreStart..visibilityMethodStart];
        Assert.Contains("ApplyProfileColumnVisibility();", restoreMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileColumnPicker_PrecedesSearchAndSettingsTabIsRemoved()
    {
        var root = FindProjectRoot();
        var profilesXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var profilesCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));
        var optionXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "OptionSettingWindow.xaml"));
        var optionCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "OptionSettingWindow.xaml.cs"));
        var optionViewModel = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "ViewModels", "OptionSettingViewModel.cs"));

        var pickerIndex = profilesXaml.IndexOf("x:Name=\"btnProfileColumns\"", StringComparison.Ordinal);
        var searchIndex = profilesXaml.IndexOf("x:Name=\"txtServerFilter\"", StringComparison.Ordinal);
        Assert.True(pickerIndex >= 0 && searchIndex > pickerIndex);
        Assert.Contains("Kind=\"ViewColumnOutline\"", profilesXaml, StringComparison.Ordinal);

        foreach (var menuName in new[]
                 {
                     "menuColumnConfigType", "menuColumnRemarks", "menuColumnAddress", "menuColumnPort",
                     "menuColumnNetwork", "menuColumnStreamSecurity", "menuColumnDelay", "menuColumnSpeed"
                 })
        {
            Assert.Contains($"x:Name=\"{menuName}\"", profilesXaml, StringComparison.Ordinal);
        }

        Assert.Contains("ProfileColumnMenuItem_Click", profilesCode, StringComparison.Ordinal);
        Assert.Contains("await ConfigHandler.SaveConfig(_config)", profilesCode, StringComparison.Ordinal);
        Assert.Contains("ProfileColumnsChanged.Publish()", profilesCode, StringComparison.Ordinal);

        Assert.DoesNotContain("Header=\"界面\"", optionXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("chkShowProfile", optionXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("chkShowProfile", optionCode, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowProfile", optionViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void CompactSummaryAndInspector_KeepProvidedDataReadableAtMinimumViewport()
    {
        var root = FindProjectRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var profilesXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));

        Assert.Contains("x:Name=\"rowConnectionSummary\" Height=\"96\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtHeroNodeName\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{DynamicResource QccFontHero}\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"220\"", mainXaml, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"colProfileInspector\" Width=\"268\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorNodeName\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorAddress\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"20\" />", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("QccCompactSecondaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("QccCompactPrimaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"205\"", profilesXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesResponsiveTypographyAndACompleteCompactStatusBar()
    {
        var root = FindProjectRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var profilesXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var statusXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "StatusBarView.xaml"));
        var themeXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Resources", "QuietControlTheme.xaml"));

        Assert.Contains("SizeChanged=\"MainWindow_SizeChanged\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"132\" />", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"72\" />", mainXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveTypography(e.NewSize.Width, e.NewSize.Height)", mainCode, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(rawScale, 0.92d, 1.18d)", mainCode, StringComparison.Ordinal);
        Assert.Contains("Math.Round(scale * 20d", mainCode, StringComparison.Ordinal);
        Assert.Contains("Resources[\"StdFontSize\"]", mainCode, StringComparison.Ordinal);

        foreach (var key in new[]
                 {
                     "QccFontTiny", "QccFontSmall", "QccFontBody", "QccFontStrong",
                     "QccFontTitle", "QccFontHero", "QccFontLarge", "QccLineTitle", "QccLineHero"
                 })
        {
            Assert.Contains($"x:Key=\"{key}\"", themeXaml, StringComparison.Ordinal);
        }

        var persistentStatus = statusXaml[..statusXaml.IndexOf("<tb:TaskbarIcon", StringComparison.Ordinal)];
        Assert.Contains("<RowDefinition Height=\"24\" />", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("Text=\"系统代理与 TUN 实时同步\"", persistentStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("Height=\"32\"", persistentStatus, StringComparison.Ordinal);

        var profileFilters = profilesXaml[profilesXaml.IndexOf("<!-- Search and filters -->", StringComparison.Ordinal)
            ..profilesXaml.IndexOf("<DataGrid", StringComparison.Ordinal)];
        Assert.Contains("<ColumnDefinition Width=\"160\" />", profileFilters, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"国家/地区\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"订阅分组\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("Text=\"全部地区\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("Path=SelectedIndex", profileFilters, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint=\"国家/地区\"", profileFilters, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint=\"所有分组\"", profileFilters, StringComparison.Ordinal);

        var inspectorRows = profilesXaml[profilesXaml.IndexOf("<!-- Selected node inspector -->", StringComparison.Ordinal)..];
        var actionBorderIndex = inspectorRows.IndexOf("<Border Grid.Row=\"2\" Padding=\"10,7\"", StringComparison.Ordinal);
        var actionsIndex = inspectorRows.IndexOf("Text=\"节点操作\"", StringComparison.Ordinal);
        Assert.True(actionBorderIndex >= 0 && actionsIndex > actionBorderIndex);
        Assert.True(inspectorRows.Split("<RowDefinition Height=\"Auto\" />", StringSplitOptions.None).Length >= 4);
        Assert.Contains("<RowDefinition Height=\"*\" />", inspectorRows, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSummary_UsesLiveMetricsAndRealCoreState()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var statusViewModel = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "ViewModels", "StatusBarViewModel.cs"));

        Assert.Contains("x:Name=\"txtHeroDelay\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtHeroJitterLoss\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ProfilesViewModel.SelectedProfile.DelayVal", xaml, StringComparison.Ordinal);
        Assert.Contains("ConnectionQualityMonitor", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ProxyPingClient", codeBehind, StringComparison.Ordinal);
        Assert.Contains("Interval = TimeSpan.FromSeconds(1)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CoreManager.Instance.IsRunning", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ConnectionQualitySeverityCalculator.GetDelaySeverity", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ConnectionQualitySeverityCalculator.GetJitterLossSeverity", codeBehind, StringComparison.Ordinal);
        Assert.Contains("txtHeroDelay.Foreground", codeBehind, StringComparison.Ordinal);
        Assert.Contains("txtHeroJitterLoss.Foreground", codeBehind, StringComparison.Ordinal);
        Assert.Contains("--qcc-qa-quality-sample", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("StatusBarViewModel.RunningInfoDisplay)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatLiveTraffic(update.ProxyUp, update.ProxyDown)", statusViewModel, StringComparison.Ordinal);
        Assert.Contains("FormatLiveTraffic(update.DirectUp, update.DirectDown)", statusViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SubscriptionNavigation_ExposesAProxiedUpdateButton()
    {
        var root = FindProjectRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var mainCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));

        var subscriptionIndex = mainXaml.IndexOf("x:Name=\"btnNavSubscription\"", StringComparison.Ordinal);
        var updateIndex = mainXaml.IndexOf("x:Name=\"btnNavSubscriptionUpdate\"", StringComparison.Ordinal);
        var routingIndex = mainXaml.IndexOf("x:Name=\"btnNavRouting\"", StringComparison.Ordinal);
        Assert.True(subscriptionIndex >= 0 && updateIndex > subscriptionIndex && routingIndex > updateIndex);
        Assert.Contains("Text=\"更新订阅\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"通过本地代理更新全部已启用订阅\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("vm => vm.SubUpdateViaProxyCmd, v => v.btnNavSubscriptionUpdate", mainCode, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionSummary_SubscriptionQuotaIsBoundedThrottledAndQaDeterministic()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));

        var nodeIndex = xaml.IndexOf("x:Name=\"txtHeroNodeName\"", StringComparison.Ordinal);
        var quotaIndex = xaml.IndexOf("x:Name=\"cardSubscriptionQuota\"", StringComparison.Ordinal);
        var metricsIndex = xaml.IndexOf("x:Name=\"txtHeroProxySpeed\"", StringComparison.Ordinal);
        Assert.True(nodeIndex >= 0 && quotaIndex > nodeIndex && metricsIndex > quotaIndex);
        Assert.Contains("x:Name=\"rowConnectionSummary\" Height=\"96\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"148\" />", xaml, StringComparison.Ordinal);
        Assert.Contains("<ColumnDefinition Width=\"194\" />", xaml, StringComparison.Ordinal);
        foreach (var name in new[]
                 {
                     "cardSubscriptionQuota", "txtSubscriptionQuotaPrimary",
                     "txtSubscriptionQuotaSecondary", "btnSubscriptionQuotaRefresh"
                 })
        {
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        }
        Assert.Contains("Text=\"订阅余量\"", xaml, StringComparison.Ordinal);

        Assert.Contains("SubscriptionQuotaRefreshInterval = TimeSpan.FromMinutes(5)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_subscriptionQuotaSingleFlight", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CancelSubscriptionQuotaRequest", codeBehind, StringComparison.Ordinal);
        Assert.Contains("var currentProfileId = _config.IndexId", codeBehind, StringComparison.Ordinal);
        Assert.Contains("GetProfileItem(profileId)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("activeProfile?.Subid", codeBehind, StringComparison.Ordinal);
        Assert.Contains("subscription.Enabled", codeBehind, StringComparison.Ordinal);
        Assert.Contains("subscription.Url", codeBehind, StringComparison.Ordinal);
        Assert.Contains("subscription.UserAgent", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("subscription.MoreUrl", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("subscription.Convert", codeBehind, StringComparison.Ordinal);
        Assert.Contains("useLocalSocksProxy: true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CoreManager.Instance.IsRunning", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_subscriptionQuotaLastCompletedUtc = null", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_subscriptionQuotaLastCompletedUtc = DateTimeOffset.UtcNow", codeBehind, StringComparison.Ordinal);

        var quotaCodeStart = codeBehind.IndexOf("private void SubscriptionQuotaRefresh_Click", StringComparison.Ordinal);
        var quotaCodeEnd = codeBehind.IndexOf("private void ApplyQaQualitySampleIfRequested", quotaCodeStart, StringComparison.Ordinal);
        var quotaCode = codeBehind[quotaCodeStart..quotaCodeEnd];
        Assert.DoesNotContain("SubIndexId", quotaCode, StringComparison.Ordinal);

        var timerStart = codeBehind.IndexOf("private async void LiveMetricsTimer_Tick", StringComparison.Ordinal);
        var timerEnd = codeBehind.IndexOf("private void ResetHeroQualityMetrics", timerStart, StringComparison.Ordinal);
        var timerMethod = codeBehind[timerStart..timerEnd];
        Assert.Contains("UpdateSubscriptionQuotaAgeAndSchedule()", timerMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("FetchAsync", timerMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("GetSubItem", timerMethod, StringComparison.Ordinal);

        Assert.Contains("--qcc-qa-quota-sample", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"success\" =>", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"unsupported\" =>", codeBehind, StringComparison.Ordinal);
        Assert.Contains("\"expired\" =>", codeBehind, StringComparison.Ordinal);
        Assert.Contains("不读取配置或网络", codeBehind, StringComparison.Ordinal);
        Assert.Contains("SubscriptionQuotaQaRenderTime", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new(2026, 8, 4, 8, 0, 0, TimeSpan.Zero)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("RenderSubscriptionQuotaResult(_subscriptionQuotaResult, SubscriptionQuotaQaRenderTime)", codeBehind, StringComparison.Ordinal);
        var qaMethodStart = codeBehind.IndexOf("private bool ApplyQaSubscriptionQuotaSampleIfRequested", StringComparison.Ordinal);
        var qaMethodEnd = codeBehind.IndexOf("private void ApplyQaQualitySampleIfRequested", qaMethodStart, StringComparison.Ordinal);
        var qaMethod = codeBehind[qaMethodStart..qaMethodEnd];
        Assert.Contains("var now = SubscriptionQuotaQaRenderTime", qaMethod, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTimeOffset.UtcNow", qaMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void SoftwareUpdatePopup_HasPersistentStatusContractAndSharedService()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));

        var softwareUpdateIndex = xaml.IndexOf("x:Name=\"pbQuietUpdate\"", StringComparison.Ordinal);
        var coreUpdateIndex = xaml.IndexOf("x:Name=\"pbCoreUpdate\"", StringComparison.Ordinal);
        Assert.True(softwareUpdateIndex >= 0 && coreUpdateIndex > softwareUpdateIndex);
        Assert.Contains("Text=\"软件更新\"", xaml, StringComparison.Ordinal);
        foreach (var name in new[]
                 {
                     "txtQuietUpdateCurrentVersion", "txtQuietUpdateOfficialVersion", "txtQuietUpdateCustomVersion",
                     "txtQuietUpdateLastAttempt", "txtQuietUpdateLastSuccess", "txtQuietUpdateStatus",
                     "btnQuietUpdateCheckNow"
                 })
        {
            Assert.Contains($"x:Name=\"{name}\"", xaml, StringComparison.Ordinal);
        }
        Assert.Contains("Content=\"立即检查\"", xaml, StringComparison.Ordinal);
        Assert.Contains("StaysOpen=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private readonly QuietUpdateService _quietUpdateService = new();", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("new QuietUpdateScheduler(new QuietUpdateService()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_quietUpdateService.CheckNowAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new QuietUpdateScheduler(_quietUpdateService", codeBehind, StringComparison.Ordinal);
        Assert.Contains("HandleQuietUpdateResultAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("QuietUpdatePopup_Opened", codeBehind, StringComparison.Ordinal);
        Assert.Contains("QuietUpdateService.GetStatusMessage", codeBehind, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"contentCoreUpdate\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CoreUpdatePopup_Opened", codeBehind, StringComparison.Ordinal);
        Assert.Contains("PrepareCoreUpdatePopup", codeBehind, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.ApplicationIdle", codeBehind, StringComparison.Ordinal);
        Assert.Contains("pbCoreUpdate.IsPopupOpen = true", codeBehind, StringComparison.Ordinal);
        Assert.Contains("--qcc-qa-open-core-update", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("DialogHost.Show(_checkUpdateView", codeBehind, StringComparison.Ordinal);
        var clickHandler = codeBehind[codeBehind.IndexOf("private async void QuietUpdateCheckNow_Click", StringComparison.Ordinal)..];
        Assert.True(clickHandler.Split("pbQuietUpdate.IsPopupOpen = true;", StringSplitOptions.None).Length >= 3);
        Assert.Contains("更新检查失败，请稍后重试", clickHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_UsesRoundedLayeredOuterFrame()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var theme = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Resources", "QuietControlTheme.xaml"));
        Assert.Contains("x:Name=\"windowOutline\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderBrush=\"#FFD8DCE2\"", xaml, StringComparison.Ordinal);
        Assert.Contains("BorderThickness=\"1\"", xaml, StringComparison.Ordinal);
        Assert.Contains("CornerRadius=\"16\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"CornerRadius\" Value=\"12\" />", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("DropShadowEffect", theme, StringComparison.Ordinal);
        Assert.Contains("UseLayoutRounding", theme, StringComparison.Ordinal);
        Assert.Contains("QccSurfaceRaisedColor", theme, StringComparison.Ordinal);
        Assert.DoesNotContain("txtQuietUpdateEvidence", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MixedSpeedTest_DisplaysLiveStatusAndMeasuredSpeed()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "ViewModels", "ProfilesViewModel.cs"));
        var speedtest = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "Services", "SpeedtestService.cs"));

        Assert.Contains("Binding=\"{Binding SpeedVal, Converter={StaticResource SpeedDisplayConverter}}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedProfile.SpeedVal, Converter={StaticResource SpeedDisplayConverter}", xaml, StringComparison.Ordinal);
        Assert.Contains("return text;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("MB/s", codeBehind, StringComparison.Ordinal);
        Assert.Contains("item.SpeedVal = result.Speed", viewModel, StringComparison.Ordinal);
        Assert.Contains("item.Speed = speed;", viewModel, StringComparison.Ordinal);
        Assert.Contains("concurrencyCount = Math.Max(1, concurrencyCount);", speedtest, StringComparison.Ordinal);
        Assert.Contains("Global.SpeedTestUrls.First()", speedtest, StringComparison.Ordinal);
    }

    [Fact]
    public void SignedUpdateWorkflow_SupportsAHotfixVersionAboveTheUpstreamTag()
    {
        var root = FindProjectRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "upstream-draft.yml"));

        Assert.Contains("release_version:", workflow, StringComparison.Ordinal);
        Assert.Contains("version=$version", workflow, StringComparison.Ordinal);
        Assert.Contains("-p:Version=$version", workflow, StringComparison.Ordinal);
        Assert.Contains("quiet-${{ steps.prepare.outputs.version }}", workflow, StringComparison.Ordinal);
        Assert.Contains("QuietControlCenter-$version-win-x64.zip", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindow_AllQccStaticResourcesAreDefinedAtApplicationScope()
    {
        var root = FindProjectRoot();
        var mainXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var appXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "App.xaml"));
        var themeXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Resources", "QuietControlTheme.xaml"));

        var referenced = System.Text.RegularExpressions.Regex
            .Matches(mainXaml, @"\{StaticResource\s+(Qcc[\w.-]+)\}")
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var defined = System.Text.RegularExpressions.Regex
            .Matches(appXaml + themeXaml, "x:Key=\"(Qcc[\\w.-]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var missing = referenced.Where(resource => !defined.Contains(resource)).ToArray();

        Assert.Contains("QccCompactPrimaryButton", referenced, StringComparer.Ordinal);
        Assert.Contains("QccCompactPrimaryButton", defined);
        Assert.True(missing.Length == 0, $"Undefined MainWindow Qcc resources: {string.Join(", ", missing)}");
    }

    [Fact]
    public void MainWindow_UsesMikaBrandAndLogoAssets()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var project = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "v2rayN.csproj"));
        var resources = Path.Combine(root, "v2rayN", "v2rayN", "Resources");

        Assert.Contains("Title=\"米卡\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"米卡\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"/Resources/MikaLogo.png\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Kind=\"ShieldCheck\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title = $\"米卡 -", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Resources\\v2rayN.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<Resource Include=\"Resources\\MikaLogo.png\" />", project, StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(resources, "MikaLogo.png")));
        Assert.True(File.Exists(Path.Combine(resources, "v2rayN.ico")));
    }

    [Fact]
    public void QaCapture_CanOpenSoftwareUpdatePopupWithoutRuntimeReload()
    {
        var root = FindProjectRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var captureMethod = codeBehind[codeBehind.IndexOf("private async Task CaptureQaFrameIfRequestedAsync", StringComparison.Ordinal)..];

        Assert.Contains("--qcc-qa-open-update", captureMethod, StringComparison.Ordinal);
        Assert.Contains("pbQuietUpdate.IsPopupOpen = true;", captureMethod, StringComparison.Ordinal);
        Assert.Contains("await Dispatcher.Yield(DispatcherPriority.Loaded);", captureMethod, StringComparison.Ordinal);
        Assert.True(
            captureMethod.IndexOf("pbQuietUpdate.IsPopupOpen = true;", StringComparison.Ordinal)
            < captureMethod.IndexOf("UpdateLayout();", captureMethod.IndexOf("pbQuietUpdate.IsPopupOpen = true;", StringComparison.Ordinal), StringComparison.Ordinal));
    }

    [Fact]
    public void MikaTaskbarAndTrayIconPolicy_LeavesMenuIconsUnchanged()
    {
        var root = FindProjectRoot();
        var appXaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "App.xaml"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var windowsManager = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Manager", "WindowsManager.cs"));
        var resources = Path.Combine(root, "v2rayN", "v2rayN", "Resources");

        Assert.Contains("Icon=\"/Resources/v2rayN.ico\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("pack://application:,,,/Resources/v2rayN.ico", windowsManager, StringComparison.Ordinal);
        Assert.Contains("Task.FromResult(Properties.Resources.NotifyIcon1)", windowsManager, StringComparison.Ordinal);
        Assert.DoesNotContain("GetNotifyIcon4Routing", windowsManager, StringComparison.Ordinal);
        Assert.DoesNotContain("CustomIcon", windowsManager, StringComparison.Ordinal);
        Assert.DoesNotContain("Utils.GetPath", windowsManager, StringComparison.Ordinal);
        Assert.DoesNotContain("Source=\"/Resources/MikaLogo.png\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<Style TargetType=\"{x:Type materialDesign:PackIcon}\">", appXaml, StringComparison.Ordinal);

        var appIcon = File.ReadAllBytes(Path.Combine(resources, "v2rayN.ico"));
        foreach (var name in new[] { "NotifyIcon1.ico", "NotifyIcon2.ico", "NotifyIcon3.ico", "NotifyIcon4.ico" })
        {
            Assert.True(appIcon.SequenceEqual(File.ReadAllBytes(Path.Combine(resources, name))), $"{name} must use the Mika tray icon.");
        }
    }

    private static string FindProjectRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Quiet Control Center project root was not found.");
    }

}
