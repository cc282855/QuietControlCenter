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
    public void CoreTypeSettings_ExposeAutomaticCompatibilitySelectionWithoutDisablingPreferences()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "OptionSettingWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "OptionSettingWindow.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "ViewModels", "OptionSettingViewModel.cs"));

        Assert.Contains("x:Name=\"togEnableAutoCoreSelection\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"自动匹配合适的内核\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"tabCoreType\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtAutoCoreSelectionTitle\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"自动匹配合适的内核\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtAutoCoreSelectionState\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"已开启\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Value=\"已关闭\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"自动匹配合适的内核\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpText=", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsThreeState=\"False\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Focusable=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsTabStop=\"True\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ToolTip=\"自动选择与节点协议、传输方式和加密方式兼容的内核\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"840\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"560\"", xaml, StringComparison.Ordinal);
        Assert.Contains("HorizontalScrollBarVisibility=\"Disabled\"", xaml, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "优先使用下方设置；仅在协议、传输或 Shadowsocks 加密方式不兼容时自动切换 Xray / sing-box。关闭后完全按下方手动映射。",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding EnableAutoCoreSelection", xaml, StringComparison.Ordinal);
        foreach (var combo in new[] { 1, 2, 3, 4, 5, 6, 7, 9 })
        {
            Assert.Contains($"x:Name=\"cmbCoreType{combo}\"", xaml, StringComparison.Ordinal);
        }
        Assert.Contains(
            "vm => vm.EnableAutoCoreSelection, v => v.togEnableAutoCoreSelection.IsChecked",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("[Reactive] public bool EnableAutoCoreSelection", viewModel, StringComparison.Ordinal);
        Assert.Contains("EnableAutoCoreSelection = _config.CoreBasicItem.EnableAutoCoreSelection", viewModel, StringComparison.Ordinal);
        Assert.Contains("_config.CoreBasicItem.EnableAutoCoreSelection = EnableAutoCoreSelection", viewModel, StringComparison.Ordinal);
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
        Assert.Contains("ApplyStatisticsColumnVisibility();", restoreMethod, StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileTrafficColumns_AreRecoverableCompactAndHorizontallyScrollable()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));
        var profilesViewModel = File.ReadAllText(Path.Combine(root, "v2rayN", "ServiceLib", "ViewModels", "ProfilesViewModel.cs"));
        var mainWindowCode = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));

        Assert.Contains("ScrollViewer.HorizontalScrollBarVisibility=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ScrollViewer.PanningMode=\"Both\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Padding\" Value=\"8,0\" />", xaml, StringComparison.Ordinal);
        foreach (var column in new[] { "colTodayUp", "colTodayDown", "colTotalUp", "colTotalDown" })
        {
            Assert.Contains($"x:Name=\"{column}\" Width=\"104\" MinWidth=\"96\"", xaml, StringComparison.Ordinal);
        }
        Assert.Contains("private void ApplyStatisticsColumnVisibility()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("new[] { colTodayUp, colTodayDown, colTotalUp, colTotalDown }", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("(update.ProxyUp + update.ProxyDown) <= 0", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("ServerTrafficPeriod.GetTodayValues(t22, now)", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("ProfilesViewModel.RefreshTrafficPeriodDisplay();", mainWindowCode, StringComparison.Ordinal);
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

        Assert.Contains("x:Name=\"rowConnectionSummary\" Height=\"Auto\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"0\" MinHeight=\"96\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"contentStatusBarView\" Grid.Row=\"2\" MinHeight=\"72\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtHeroNodeName\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"{DynamicResource QccFontHero}\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"220\"", mainXaml, StringComparison.Ordinal);

        foreach (var metricName in new[] { "txtHeroProxySpeed", "txtHeroDirectSpeed", "txtHeroDelay", "txtHeroJitterLoss" })
        {
            var metricStart = mainXaml.IndexOf($"x:Name=\"{metricName}\"", StringComparison.Ordinal);
            Assert.True(metricStart >= 0);
            var metricElement = mainXaml[metricStart..mainXaml.IndexOf("/>", metricStart, StringComparison.Ordinal)];
            Assert.Contains("TextWrapping=\"Wrap\"", metricElement, StringComparison.Ordinal);
            Assert.DoesNotContain("TextTrimming=", metricElement, StringComparison.Ordinal);
        }
        Assert.Contains("x:Name=\"btnDisconnect\" Height=\"34\"", mainXaml, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"colProfileInspector\" Width=\"268\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorNodeName\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorAddress\"", profilesXaml, StringComparison.Ordinal);
        Assert.Equal(8, profilesXaml.Split("<RowDefinition Height=\"Auto\" MinHeight=\"20\" />", StringSplitOptions.None).Length - 1);
        Assert.Contains("QccCompactSecondaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("QccCompactPrimaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Height\" Value=\"34\" />", profilesXaml, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"contentStatusBarView\" Grid.Row=\"2\" MinHeight=\"72\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("ApplyResponsiveTypography(e.NewSize.Width, e.NewSize.Height)", mainCode, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(rawScale, 0.92d, 1.18d)", mainCode, StringComparison.Ordinal);
        Assert.Contains("Math.Round(scale * 20d", mainCode, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(scale, 0.92d, 1.18d)", mainCode, StringComparison.Ordinal);
        var quantizationIndex = mainCode.IndexOf("Math.Round(scale * 20d", StringComparison.Ordinal);
        var finalClampIndex = mainCode.IndexOf("Math.Clamp(scale, 0.92d, 1.18d)", StringComparison.Ordinal);
        Assert.True(quantizationIndex >= 0 && finalClampIndex > quantizationIndex);
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
        Assert.Contains("<Grid MinHeight=\"72\"", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\" MinHeight=\"48\" />", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"Auto\" MinHeight=\"24\" />", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("Text=\"系统代理与 TUN 实时同步\"", persistentStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("Grid.Column=\"1\"", persistentStatus, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"32\"", persistentStatus, StringComparison.Ordinal);
        Assert.DoesNotMatch("(?<!Min)Height=\"32\"", persistentStatus);

        var profileFilters = profilesXaml[profilesXaml.IndexOf("<!-- Search and filters -->", StringComparison.Ordinal)
            ..profilesXaml.IndexOf("<DataGrid", StringComparison.Ordinal)];
        Assert.Contains("<ColumnDefinition Width=\"160\" />", profileFilters, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"国家/地区\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.Name=\"订阅分组\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("Text=\"全部地区\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("Path=SelectedIndex", profileFilters, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint=\"国家/地区\"", profileFilters, StringComparison.Ordinal);
        Assert.DoesNotContain("HintAssist.Hint=\"所有分组\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"borderProfileToolbar\" Grid.Row=\"0\" MinHeight=\"56\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtServerFilter\"", profileFilters, StringComparison.Ordinal);
        Assert.Contains("MinHeight=\"34\"", profileFilters, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"borderProfileTestActions\" Grid.Row=\"2\" MinHeight=\"36\"", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<RowDefinition Height=\"56\" />", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<RowDefinition Height=\"36\" />", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnHeaderHeight=", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowHeight=", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"34\" />", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("EnableRowVirtualization=\"True\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"colSpeed\" Width=\"1*\" MinWidth=\"108\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("<Button Width=\"34\" Height=\"34\"", profilesXaml, StringComparison.Ordinal);

        var inspectorRows = profilesXaml[profilesXaml.IndexOf("<!-- Selected node inspector -->", StringComparison.Ordinal)..];
        var actionBorderIndex = inspectorRows.IndexOf("<Border Grid.Row=\"2\" Padding=\"10,7\"", StringComparison.Ordinal);
        var actionsIndex = inspectorRows.IndexOf("Text=\"节点操作\"", StringComparison.Ordinal);
        Assert.True(actionBorderIndex >= 0 && actionsIndex > actionBorderIndex);
        var inspectorGridRows = inspectorRows[..inspectorRows.IndexOf("</Grid.RowDefinitions>", StringComparison.Ordinal)];
        Assert.Equal(2, inspectorGridRows.Split("<RowDefinition Height=\"Auto\" />", StringSplitOptions.None).Length - 1);
        Assert.Equal(1, inspectorGridRows.Split("<RowDefinition Height=\"*\" />", StringSplitOptions.None).Length - 1);
        Assert.Contains("<ScrollViewer Grid.Row=\"1\"", inspectorRows, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"2\"", inspectorRows, StringComparison.Ordinal);
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
        Assert.Contains("x:Name=\"rowConnectionSummary\" Height=\"Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<Border Grid.Row=\"0\" MinHeight=\"96\"", xaml, StringComparison.Ordinal);
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
    public void XuantongExecutableName_IsConsistentAcrossUpdateAndReleaseChain()
    {
        var root = FindProjectRoot();
        var service = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Services", "QuietUpdateService.cs"));
        var helper = File.ReadAllText(Path.Combine(root, "v2rayN", "AmazTool", "UpgradeApp.cs"));
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "upstream-draft.yml"));

        Assert.Contains("BrandExecutableName = \"玄同.exe\"", service, StringComparison.Ordinal);
        Assert.Contains("mainExecutable = BrandExecutableName", service, StringComparison.Ordinal);
        Assert.Contains("instruction.MainExecutable", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("\"v2rayN.exe\", StringComparison.OrdinalIgnoreCase", helper, StringComparison.Ordinal);
        Assert.Contains("Join-Path $smokeRoot '玄同.exe'", workflow, StringComparison.Ordinal);
        Assert.Contains("Join-Path $qaRoot '玄同.exe'", workflow, StringComparison.Ordinal);
        Assert.Contains("product = 'QuietControlCenter'", workflow, StringComparison.Ordinal);
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
    public void MainWindow_UsesXuantongBrandAndLogoAssets()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var project = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "v2rayN.csproj"));
        var resources = Path.Combine(root, "v2rayN", "v2rayN", "Resources");

        Assert.Contains("Title=\"玄同\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"玄同\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Source=\"/Resources/MikaLogo.png\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Kind=\"ShieldCheck\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title = $\"玄同 -", codeBehind, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Resources\\v2rayN.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyName>玄同</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>v2rayN</RootNamespace>", project, StringComparison.Ordinal);
        Assert.Contains("<Title>玄同</Title>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyTitle>玄同</AssemblyTitle>", project, StringComparison.Ordinal);
        Assert.Contains("<Product>玄同</Product>", project, StringComparison.Ordinal);
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
    public void QaCapture_CoreSettingsTargetIsNarrowAndDoesNotSaveOrCreateTray()
    {
        var root = FindProjectRoot();
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));
        var captureScript = File.ReadAllText(Path.Combine(root, "tools", "capture-qcc-window.ps1"));

        Assert.Contains("--qcc-qa-open-core-settings", codeBehind, StringComparison.Ordinal);
        Assert.Contains("CaptureCoreSettingsQaFrameAsync", codeBehind, StringComparison.Ordinal);
        Assert.Contains("settingWindow.tabCoreType.IsSelected = true;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel = new OptionSettingViewModel()", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveCmd", codeBehind[codeBehind.IndexOf("private async Task CaptureCoreSettingsQaFrameAsync", StringComparison.Ordinal)..], StringComparison.Ordinal);
        Assert.Contains("if (!_coreSettingsQaMode)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("[ValidateSet('Main', 'CoreSettings')]", captureScript, StringComparison.Ordinal);
        Assert.Contains("$arguments += ' --qcc-qa-open-core-settings'", captureScript, StringComparison.Ordinal);
        Assert.Contains("ManagedAssembly is restricted to the CoreSettings QA target.", captureScript, StringComparison.Ordinal);
        Assert.Contains("ManagedAssembly must be an existing absolute DLL path.", captureScript, StringComparison.Ordinal);
        Assert.Contains("$mikaProcessNames = @(\"$([char]0x7C73)$([char]0x5361)\", 'v2rayN')", captureScript, StringComparison.Ordinal);
        Assert.Contains("PROTECTED_BEFORE=", captureScript, StringComparison.Ordinal);
        Assert.Contains("PROTECTED_AFTER=", captureScript, StringComparison.Ordinal);

        var activationStart = codeBehind.IndexOf("this.WhenActivated(disposables =>", StringComparison.Ordinal);
        var commandBindingsStart = codeBehind.IndexOf("//servers", activationStart, StringComparison.Ordinal);
        Assert.True(activationStart >= 0 && commandBindingsStart > activationStart);
        var activationPrelude = codeBehind[activationStart..commandBindingsStart];
        var runtimeGate = activationPrelude.IndexOf("if (!_coreSettingsQaMode)", StringComparison.Ordinal);
        var metricsTimer = activationPrelude.IndexOf("new DispatcherTimer", StringComparison.Ordinal);
        var quotaSchedule = activationPrelude.IndexOf("UpdateSubscriptionQuotaAgeAndSchedule();", StringComparison.Ordinal);
        Assert.True(runtimeGate >= 0 && runtimeGate < metricsTimer);
        Assert.True(runtimeGate < quotaSchedule);
        Assert.DoesNotContain("new DispatcherTimer", activationPrelude[..runtimeGate], StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateSubscriptionQuotaAgeAndSchedule();", activationPrelude[..runtimeGate], StringComparison.Ordinal);

        var onLoadedStart = codeBehind.IndexOf("protected override void OnLoaded", StringComparison.Ordinal);
        var updateLoopStart = codeBehind.IndexOf("_ = RefreshQuietUpdateStatusAsync();", onLoadedStart, StringComparison.Ordinal);
        Assert.True(onLoadedStart >= 0 && updateLoopStart > onLoadedStart);
        var onLoadedBeforeRuntime = codeBehind[onLoadedStart..updateLoopStart];
        Assert.Contains("if (_coreSettingsQaMode)", onLoadedBeforeRuntime, StringComparison.Ordinal);
        Assert.Contains("_ = CaptureQaFrameIfRequestedAsync();", onLoadedBeforeRuntime, StringComparison.Ordinal);
        Assert.Contains("return;", onLoadedBeforeRuntime, StringComparison.Ordinal);
    }

    [Fact]
    public void QaCaptureAndPackagingScripts_ProtectRunningCoresAndPrivateRuntimeState()
    {
        var root = FindProjectRoot();
        var captureScript = File.ReadAllText(Path.Combine(root, "tools", "capture-qcc-window.ps1"));
        var packageScript = File.ReadAllText(Path.Combine(root, "tools", "package-qcc.ps1"));

        Assert.Contains("if ($ReloadCore)", captureScript, StringComparison.Ordinal);
        Assert.Contains("ReloadCore is forbidden", captureScript, StringComparison.Ordinal);
        Assert.Contains("$outputPath = [IO.Path]::GetFullPath($Output)", captureScript, StringComparison.Ordinal);
        Assert.Contains("$errorPath = $outputPath + '.error.txt'", captureScript, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $outputPath -Force", captureScript, StringComparison.Ordinal);
        Assert.Contains("Remove-Item -LiteralPath $errorPath -Force", captureScript, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(captureScript, @"Remove-Item\b").Count == 2);
        Assert.Contains("$captureStartUtc = [DateTime]::UtcNow", captureScript, StringComparison.Ordinal);
        Assert.Contains("$captureFreshnessFloorUtc = $captureStartUtc.AddSeconds(-2)", captureScript, StringComparison.Ordinal);
        Assert.True(
            captureScript.IndexOf("$captureStartUtc = [DateTime]::UtcNow", StringComparison.Ordinal)
            < captureScript.IndexOf("Start-Process", StringComparison.Ordinal));
        Assert.Contains("if ($process.ExitCode -notin @(0, -1))", captureScript, StringComparison.Ordinal);
        Assert.Contains("App.OnExit terminates the WPF process with Process.Kill()", captureScript, StringComparison.Ordinal);
        Assert.Contains("Test-Path -LiteralPath $errorPath -PathType Leaf", captureScript, StringComparison.Ordinal);
        Assert.Contains("$outputFile.Length -le 0", captureScript, StringComparison.Ordinal);
        Assert.Contains("$outputFile.LastWriteTimeUtc -lt $captureFreshnessFloorUtc", captureScript, StringComparison.Ordinal);
        Assert.Contains("@('sing-box', 'mihomo', 'xray')", captureScript, StringComparison.Ordinal);
        Assert.Contains("$baselineCoreProcesses = @(Get-CoreProcessSnapshot)", captureScript, StringComparison.Ordinal);
        Assert.Contains("$finalCoreProcesses = @(Get-CoreProcessSnapshot)", captureScript, StringComparison.Ordinal);
        Assert.Contains("Compare-Object -ReferenceObject $baselineCoreProcesses -DifferenceObject $finalCoreProcesses", captureScript, StringComparison.Ordinal);
        Assert.Contains("if ($timedOut -and -not $process.HasExited)", captureScript, StringComparison.Ordinal);
        Assert.Contains("Stop-Process -Id $process.Id -Force", captureScript, StringComparison.Ordinal);
        Assert.True(System.Text.RegularExpressions.Regex.Matches(captureScript, @"Stop-Process\b").Count == 1);
        Assert.DoesNotContain("ForEach-Object { Stop-Process", captureScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Stop-Process -Name", captureScript, StringComparison.Ordinal);

        Assert.Contains("$artifactRootResolved.Equals($expectedArtifact, [StringComparison]::OrdinalIgnoreCase)", packageScript, StringComparison.Ordinal);
        foreach (var sensitiveName in new[] { "guiConfigs", "guiLogs", "guiTemps", "logs", "binConfigs" })
        {
            Assert.Contains($"'{sensitiveName}'", packageScript, StringComparison.Ordinal);
        }
        foreach (var sensitiveExtension in new[] { ".db", ".sqlite", ".sqlite3", ".log", ".wal", ".shm", ".journal", ".db-wal", ".db-shm", ".db-journal", ".key", ".pem", ".pfx", ".p12", ".pk8", ".pkcs8", ".ppk", ".snk" })
        {
            Assert.Contains($"'{sensitiveExtension}'", packageScript, StringComparison.Ordinal);
        }
        Assert.Contains("$sensitiveBaseNamePattern", packageScript, StringComparison.Ordinal);
        Assert.Contains("id_(?:rsa|dsa|ecdsa|ed25519)", packageScript, StringComparison.Ordinal);
        Assert.Contains("$privateKeyTextPattern", packageScript, StringComparison.Ordinal);
        Assert.Contains("BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY", packageScript, StringComparison.Ordinal);
        Assert.Contains("PuTTY-User-Key-File-", packageScript, StringComparison.Ordinal);
        Assert.Contains("Count=$($sensitivePayloads.Count)", packageScript, StringComparison.Ordinal);
        Assert.Contains("Count=$($unexpectedTextPayloads.Count)", packageScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$sensitivePayloads[0].Name", packageScript, StringComparison.Ordinal);
        Assert.DoesNotContain("$textPayload.Name", packageScript, StringComparison.Ordinal);
        Assert.Contains("$plausibleTextExtensions", packageScript, StringComparison.Ordinal);
        Assert.Contains("$plausibleTextExtensions -contains $_.Extension.ToLowerInvariant()", packageScript, StringComparison.Ordinal);
        foreach (var textExtension in new[] { ".toml", ".csv", ".url" })
        {
            Assert.Contains($"'{textExtension}'", packageScript, StringComparison.Ordinal);
        }
        Assert.Contains("[string]::IsNullOrEmpty($_.Extension)", packageScript, StringComparison.Ordinal);
        Assert.Contains("$bytes = [IO.File]::ReadAllBytes($textPayload.FullName)", packageScript, StringComparison.Ordinal);
        Assert.Contains("[Text.Encoding]::Unicode.GetString", packageScript, StringComparison.Ordinal);
        Assert.Contains("[Text.Encoding]::BigEndianUnicode.GetString", packageScript, StringComparison.Ordinal);
        Assert.Contains("$bytes[0] -eq 0xff -and $bytes[1] -eq 0xfe", packageScript, StringComparison.Ordinal);
        Assert.Contains("$bytes[0] -eq 0xfe -and $bytes[1] -eq 0xff", packageScript, StringComparison.Ordinal);
        Assert.Contains("$evenNullCount", packageScript, StringComparison.Ordinal);
        Assert.Contains("$oddNullCount", packageScript, StringComparison.Ordinal);
        Assert.Contains("if ($value -eq 0)", packageScript, StringComparison.Ordinal);
        Assert.Contains("$controlByteCount * 20 -ge $byteCount", packageScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Get-Content -LiteralPath $textPayload.FullName -Raw", packageScript, StringComparison.Ordinal);
        Assert.Contains("$subscriptionSchemePattern", packageScript, StringComparison.Ordinal);
        foreach (var scheme in new[] { "vmess", "vless", "ss", "ssr", "trojan", "hysteria", "hysteria2", "hy2", "tuic", "socks", "socks5", "wireguard", "anytls", "naive" })
        {
            Assert.Contains(scheme, packageScript, StringComparison.Ordinal);
        }
        var schemePatternLine = packageScript.Split('\n').Single(line => line.StartsWith("$subscriptionSchemePattern", StringComparison.Ordinal));
        Assert.DoesNotContain("|http|", schemePatternLine, StringComparison.Ordinal);
        Assert.DoesNotContain("|https", schemePatternLine, StringComparison.Ordinal);
        Assert.DoesNotContain("https?)://", schemePatternLine, StringComparison.Ordinal);
        Assert.Contains("function Assert-NoUnexpectedTextPayloads", packageScript, StringComparison.Ordinal);
        Assert.Contains("Unexpected text payload is forbidden", packageScript, StringComparison.Ordinal);
        Assert.True(
            packageScript.IndexOf("Assert-NoUnexpectedTextPayloads $artifactRootResolved", StringComparison.Ordinal)
            < packageScript.IndexOf("$files = [ordered]@{}", StringComparison.Ordinal));
        Assert.Contains("$files[$relative] = (Get-FileHash", packageScript, StringComparison.Ordinal);
        Assert.Contains("version=$Version; files=$files", packageScript, StringComparison.Ordinal);

        var firstSensitiveGuard = packageScript.IndexOf("Assert-NoSensitivePayload $artifactRootResolved", StringComparison.Ordinal);
        var artifactCleanup = packageScript.IndexOf("# Publish output is immutable", StringComparison.Ordinal);
        var markerWrite = packageScript.IndexOf("ConvertTo-Json -Depth 5", StringComparison.Ordinal);
        var finalSensitiveGuard = packageScript.LastIndexOf("Assert-NoSensitivePayload $artifactRootResolved", StringComparison.Ordinal);
        var finalUnexpectedTextGuard = packageScript.LastIndexOf("Assert-NoUnexpectedTextPayloads $artifactRootResolved", StringComparison.Ordinal);
        Assert.True(firstSensitiveGuard >= 0 && firstSensitiveGuard < artifactCleanup);
        Assert.True(markerWrite >= 0 && finalSensitiveGuard > markerWrite);
        Assert.True(markerWrite >= 0 && finalUnexpectedTextGuard > markerWrite);
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

    [Fact]
    public void MikaBrandAssets_AreFrozenMultiSizeAndCoverDesktopAndUpdaterSurfaces()
    {
        const string expectedChromaHash = "C4A7CBE53799F29077BEC13202C6D6C702327D9965F2F1D9B0A3378A2E02590B";
        const string expectedMasterHash = "DF739064E84E9F038923268D997CD0FB1D6FBDDCA51F0B38CFE96E9BF512C9F4";
        const string expectedLogoHash = "DA38A8F947350EE5F30D4521E57F9A6B7ABDA6EE665839C8145B65380B018F63";
        const string expectedIconHash = "D64BFDC8BF4FCA88F485A19BA65BF6F559AA68C33065FFBB79432FCEA9650B1D";
        var root = FindProjectRoot();
        var branding = Path.Combine(root, "branding");
        var wpfResources = Path.Combine(root, "v2rayN", "v2rayN", "Resources");
        var desktopAssets = Path.Combine(root, "v2rayN", "v2rayN.Desktop", "Assets");
        var appIcon = Path.Combine(wpfResources, "v2rayN.ico");

        static string Sha256(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
        static (int Width, int Height, byte ColorType) ReadPngHeader(string path)
        {
            var bytes = File.ReadAllBytes(path);
            Assert.True(bytes.Length >= 26 && bytes.AsSpan(0, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
            return (
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4)),
                System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4)),
                bytes[25]);
        }

        Assert.Equal(expectedChromaHash, Sha256(Path.Combine(branding, "source", "mika-wind-gate-chroma-source.png")));
        var master = Path.Combine(branding, "master", "mika-wind-gate-transparent-1024.png");
        Assert.Equal(expectedMasterHash, Sha256(master));
        Assert.Equal((1024, 1024, (byte)6), ReadPngHeader(master));
        Assert.Equal(expectedLogoHash, Sha256(Path.Combine(wpfResources, "MikaLogo.png")));
        Assert.Equal((512, 512, (byte)6), ReadPngHeader(Path.Combine(wpfResources, "MikaLogo.png")));
        Assert.Equal(expectedIconHash, Sha256(appIcon));

        var iconBytes = File.ReadAllBytes(appIcon);
        Assert.Equal((ushort)0, BitConverter.ToUInt16(iconBytes, 0));
        Assert.Equal((ushort)1, BitConverter.ToUInt16(iconBytes, 2));
        Assert.Equal((ushort)9, BitConverter.ToUInt16(iconBytes, 4));
        var expectedSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };
        for (var index = 0; index < expectedSizes.Length; index++)
        {
            var entry = 6 + index * 16;
            var width = iconBytes[entry] == 0 ? 256 : iconBytes[entry];
            var height = iconBytes[entry + 1] == 0 ? 256 : iconBytes[entry + 1];
            var offset = BitConverter.ToInt32(iconBytes, entry + 12);
            Assert.Equal(expectedSizes[index], width);
            Assert.Equal(expectedSizes[index], height);
            Assert.Equal((ushort)32, BitConverter.ToUInt16(iconBytes, entry + 6));
            Assert.True(iconBytes.AsSpan(offset, 8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }));
        }

        foreach (var path in new[]
                 {
                     Path.Combine(wpfResources, "NotifyIcon1.ico"),
                     Path.Combine(wpfResources, "NotifyIcon2.ico"),
                     Path.Combine(wpfResources, "NotifyIcon3.ico"),
                     Path.Combine(wpfResources, "NotifyIcon4.ico"),
                     Path.Combine(desktopAssets, "v2rayN.ico"),
                     Path.Combine(desktopAssets, "NotifyIcon1.ico"),
                     Path.Combine(desktopAssets, "NotifyIcon2.ico"),
                     Path.Combine(desktopAssets, "NotifyIcon3.ico"),
                     Path.Combine(desktopAssets, "NotifyIcon4.ico"),
                     Path.Combine(root, "v2rayN", "AmazTool", "Resources", "v2rayN.ico")
                 })
        {
            Assert.Equal(expectedIconHash, Sha256(path));
        }

        Assert.Equal((256, 256, (byte)6), ReadPngHeader(Path.Combine(root, "v2rayN", "v2rayN.Desktop", "v2rayN.png")));
        foreach (var name in new[] { "mika-brand-contact-light-1024.png", "mika-brand-contact-dark-1024.png" })
        {
            Assert.Equal((1024, 1024, (byte)2), ReadPngHeader(Path.Combine(branding, "evidence", name)));
        }

        var amazProject = File.ReadAllText(Path.Combine(root, "v2rayN", "AmazTool", "AmazTool.csproj"));
        Assert.Contains("<ApplicationIcon>Resources\\v2rayN.ico</ApplicationIcon>", amazProject, StringComparison.Ordinal);
        Assert.Contains("<EmbeddedResource Include=\"Resources\\v2rayN.ico\">", amazProject, StringComparison.Ordinal);
        Assert.Contains("<Product>玄同</Product>", amazProject, StringComparison.Ordinal);

        var desktopProject = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN.Desktop", "v2rayN.Desktop.csproj"));
        Assert.Contains("<AssemblyName>玄同</AssemblyName>", desktopProject, StringComparison.Ordinal);
        Assert.Contains("<RootNamespace>v2rayN</RootNamespace>", desktopProject, StringComparison.Ordinal);
        Assert.Contains("<Product>玄同</Product>", desktopProject, StringComparison.Ordinal);
    }

    [Fact]
    public void ActiveNodeDisplay_UsesRealConfigAndDoubleClickActivation()
    {
        var root = FindProjectRoot();
        var serviceRoot = Path.Combine(root, "v2rayN", "ServiceLib");
        var profilesViewModel = File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "ProfilesViewModel.cs"));
        var mainWindow = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var profilesView = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml"));
        var profilesCodeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "ProfilesView.xaml.cs"));

        Assert.Contains("public string ActiveProfileRemarks", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("public string ActiveSubscriptionDisplay", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("GetProfileItem(_config.IndexId)", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("GetSubItem(activeProfile.Subid)", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("SubscriptionSourceDisplay.Format(activeSubscription.Remarks)", profilesViewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("SubscriptionSourceDisplay.Format(activeSubscription.Remarks, activeSubscription.Url)", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("ActiveProfileRemarks = activeProfile?.Remarks ?? \"尚未连接\"", profilesViewModel, StringComparison.Ordinal);
        Assert.Contains("ProfilesViewModel.ActiveProfileRemarks", mainWindow, StringComparison.Ordinal);
        Assert.Contains("ProfilesViewModel.ActiveSubscriptionDisplay", mainWindow, StringComparison.Ordinal);
        Assert.Contains("StatusBarViewModel.ActiveNodeTrafficDisplay", mainWindow, StringComparison.Ordinal);
        Assert.Contains("今日 ↑", File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "StatusBarViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("本月 ↑", File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "StatusBarViewModel.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("ProfilesViewModel.SelectedProfile.Remarks", mainWindow, StringComparison.Ordinal);

        Assert.Contains("ActiveNodeMarkerConverter", profilesView, StringComparison.Ordinal);
        Assert.Contains("ExName=\"ActiveMarker\"", profilesView, StringComparison.Ordinal);
        Assert.Contains("QccDanger", profilesView, StringComparison.Ordinal);
        Assert.Contains("红色 ★ 表示当前活动节点", profilesView, StringComparison.Ordinal);
        Assert.Contains("private async void LstProfiles_MouseDoubleClick", profilesCodeBehind, StringComparison.Ordinal);
        var doubleClickStart = profilesCodeBehind.IndexOf("private async void LstProfiles_MouseDoubleClick", StringComparison.Ordinal);
        var doubleClickEnd = profilesCodeBehind.IndexOf("private void LstProfiles_ColumnHeader_Click", doubleClickStart, StringComparison.Ordinal);
        Assert.True(doubleClickStart >= 0 && doubleClickEnd > doubleClickStart);
        var doubleClickHandler = profilesCodeBehind[doubleClickStart..doubleClickEnd];
        Assert.Contains("await ViewModel.SetDefaultServer()", doubleClickHandler, StringComparison.Ordinal);
        Assert.Contains("e.Handled = true", doubleClickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("DoubleClick2Activate", doubleClickHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("EditServerAsync", doubleClickHandler, StringComparison.Ordinal);
    }

    [Fact]
    public void MikaExecutable_AlwaysRequestsAdministratorPrivileges()
    {
        var root = FindProjectRoot();
        var projectRoot = Path.Combine(root, "v2rayN", "v2rayN");
        var project = File.ReadAllText(Path.Combine(projectRoot, "v2rayN.csproj"));
        var manifest = File.ReadAllText(Path.Combine(projectRoot, "app.manifest"));

        Assert.Contains("<ApplicationManifest>app.manifest</ApplicationManifest>", project, StringComparison.Ordinal);
        Assert.Contains("<requestedPrivileges>", manifest, StringComparison.Ordinal);
        Assert.Contains("requestedExecutionLevel level=\"requireAdministrator\" uiAccess=\"false\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requestedExecutionLevel level=\"asInvoker\"", manifest, StringComparison.Ordinal);
        Assert.DoesNotContain("requestedExecutionLevel level=\"highestAvailable\"", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public void FirstSubscriptionUpdate_IsScopedAwaitedProxyOnlyAndPrivacyRedacted()
    {
        var root = FindProjectRoot();
        var serviceRoot = Path.Combine(root, "v2rayN", "ServiceLib");
        var main = File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "MainWindowViewModel.cs"));
        var profiles = File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "ProfilesViewModel.cs"));
        var settings = File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "SubSettingViewModel.cs"));
        var edit = File.ReadAllText(Path.Combine(serviceRoot, "ViewModels", "SubEditViewModel.cs"));
        var handler = File.ReadAllText(Path.Combine(serviceRoot, "Handler", "SubscriptionHandler.cs"));
        var download = File.ReadAllText(Path.Combine(serviceRoot, "Services", "DownloadService.cs"));
        var coordinator = File.ReadAllText(Path.Combine(serviceRoot, "Services", "SubscriptionUpdateCoordinator.cs"));

        Assert.Contains("new SubscriptionUpdateCoordinator(ExecuteSubscriptionUpdateAsync)", main, StringComparison.Ordinal);
        Assert.Contains("new ProfilesViewModel(UpdateNewSubscriptionAsync)", main, StringComparison.Ordinal);
        Assert.Contains("new SubSettingViewModel(UpdateNewSubscriptionAsync)", main, StringComparison.Ordinal);
        Assert.Contains("UseProxy: true", main, StringComparison.Ordinal);
        Assert.Contains("AllowDirectFallback: false", main, StringComparison.Ordinal);
        Assert.Contains("IsAutomatic: true", main, StringComparison.Ordinal);
        Assert.Contains("AllowDirectFallback: true", main, StringComparison.Ordinal);
        Assert.Contains("new SubEditViewModel(item, _firstUpdateAsync)", profiles, StringComparison.Ordinal);
        Assert.Contains("new SubEditViewModel(item, _firstUpdateAsync)", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Subscribe", settings, StringComparison.Ordinal);

        Assert.Contains("_wasNew = subItem.Id.IsNullOrEmpty()", edit, StringComparison.Ordinal);
        Assert.Contains("await AppManager.Instance.GetSubItem(persistedId)", edit, StringComparison.Ordinal);
        Assert.Contains("Interlocked.CompareExchange(ref _firstUpdateConsumed, 1, 0)", edit, StringComparison.Ordinal);
        Assert.Contains("result = await _firstUpdateAsync!(persistedId)", edit, StringComparison.Ordinal);
        Assert.Contains("FirstSubscriptionUpdatePolicy.SkippedFeedback", edit, StringComparison.Ordinal);
        Assert.Contains("FirstSubscriptionUpdatePolicy.FailedFeedback", edit, StringComparison.Ordinal);

        Assert.Contains("private readonly SemaphoreSlim _gate = new(1, 1)", coordinator, StringComparison.Ordinal);
        Assert.Contains("new RequestKey(", coordinator, StringComparison.Ordinal);
        Assert.Contains("request.UseProxy", coordinator, StringComparison.Ordinal);
        Assert.Contains("request.AllowDirectFallback", coordinator, StringComparison.Ordinal);
        Assert.Contains("request.IsAutomatic", coordinator, StringComparison.Ordinal);
        Assert.Contains("_inFlightByRequest.TryGetValue(key", coordinator, StringComparison.Ordinal);
        Assert.Contains("ReferenceEquals(current, owner)", coordinator, StringComparison.Ordinal);
        Assert.Contains("return await _updateAsync(request)", coordinator, StringComparison.Ordinal);
        Assert.Contains("return SubscriptionUpdateResult.Failed", coordinator, StringComparison.Ordinal);

        Assert.Contains("allowDirectFallback && blProxy", handler, StringComparison.Ordinal);
        Assert.Contains("requireProxy: blProxy && !allowDirectFallback", handler, StringComparison.Ordinal);
        Assert.Contains("StartsWith(Global.HttpsProtocol, StringComparison.OrdinalIgnoreCase)", handler, StringComparison.Ordinal);
        Assert.Contains("StartsWith(Global.HttpProtocol, StringComparison.OrdinalIgnoreCase)", handler, StringComparison.Ordinal);
        Assert.Contains("item.Id != subId", handler, StringComparison.Ordinal);
        Assert.Contains("? AutomaticSubscriptionUpdateTaskHandler", main, StringComparison.Ordinal);
        Assert.Contains(": UpdateTaskHandler", main, StringComparison.Ordinal);
        Assert.Contains("preserveActiveSelection: request.IsAutomatic", main, StringComparison.Ordinal);
        Assert.Contains("bool preserveActiveSelection = false", handler, StringComparison.Ordinal);
        Assert.Contains("preserveActiveSelection))", handler, StringComparison.Ordinal);
        var processResultStart = handler.IndexOf(
            "private static async Task<bool> ProcessDownloadResult",
            StringComparison.Ordinal);
        Assert.True(processResultStart >= 0);
        var restoreProfilesStart = handler.IndexOf(
            "private static async Task RestoreProfilesAsync",
            processResultStart,
            StringComparison.Ordinal);
        Assert.True(restoreProfilesStart > processResultStart);
        var processResult = handler[processResultStart..restoreProfilesStart];
        Assert.Contains("else if (preserveActiveSelection && config.IndexId != originalIndexId)", processResult, StringComparison.Ordinal);
        Assert.Contains("config.IndexId = originalIndexId;", processResult, StringComparison.Ordinal);
        Assert.Contains("await ConfigHandler.SaveConfig(config);", processResult, StringComparison.Ordinal);
        var automaticCallbackStart = main.IndexOf(
            "private async Task AutomaticSubscriptionUpdateTaskHandler",
            StringComparison.Ordinal);
        Assert.True(automaticCallbackStart >= 0);
        var automaticCallbackEnd = main.IndexOf(
            "private async Task UpdateStatisticsHandler",
            automaticCallbackStart,
            StringComparison.Ordinal);
        Assert.True(automaticCallbackEnd > automaticCallbackStart);
        var automaticCallback = main[automaticCallbackStart..automaticCallbackEnd];
        Assert.Contains("await RefreshServersDispatcherAsync()", automaticCallback, StringComparison.Ordinal);
        Assert.Contains("ProfilesViewModel.AdjustMainLvColWidth()", automaticCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("Reload(", automaticCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("CoreManager", automaticCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadCore", automaticCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("SysProxy", automaticCallback, StringComparison.Ordinal);
        Assert.DoesNotContain("IndexId =", automaticCallback, StringComparison.Ordinal);
        var refreshStart = main.IndexOf("private async Task RefreshServers()", StringComparison.Ordinal);
        Assert.True(refreshStart >= 0);
        var refreshEnd = main.IndexOf(
            "private async Task RefreshServersDispatcherAsync()",
            refreshStart,
            StringComparison.Ordinal);
        Assert.True(refreshEnd > refreshStart);
        var refresh = main[refreshStart..refreshEnd];
        Assert.Contains("ProfilesViewModel.RefreshServersBiz()", refresh, StringComparison.Ordinal);
        Assert.Contains("StatusBarViewModel.RefreshServersBiz()", refresh, StringComparison.Ordinal);
        Assert.Contains("new DownloadService(redactSensitiveErrors: true)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Length <", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Logging.SaveLog(result)", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("GetException().Message", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ex.Message", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("Logging.SaveLog(\"UpdateSubscription\", ex)", handler, StringComparison.Ordinal);
        Assert.Contains("if (blProxy && requireProxy && webProxy is null)", download, StringComparison.Ordinal);
        Assert.Contains("Logging.SaveLog(\"Subscription request failed.\")", download, StringComparison.Ordinal);
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
