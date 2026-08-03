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

        Assert.Contains("x:Name=\"rowConnectionSummary\" Height=\"120\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtHeroNodeName\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("FontSize=\"18\"", mainXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", mainXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"220\"", mainXaml, StringComparison.Ordinal);

        Assert.Contains("x:Name=\"colProfileInspector\" Width=\"320\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorNodeName\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"txtInspectorAddress\"", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("<RowDefinition Height=\"22\" />", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("QccCompactSecondaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.Contains("QccCompactPrimaryButton", profilesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MaxWidth=\"205\"", profilesXaml, StringComparison.Ordinal);
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
        Assert.DoesNotContain("StatusBarViewModel.RunningInfoDisplay)", codeBehind, StringComparison.Ordinal);
        Assert.Contains("FormatLiveTraffic(update.ProxyUp, update.ProxyDown)", statusViewModel, StringComparison.Ordinal);
        Assert.Contains("FormatLiveTraffic(update.DirectUp, update.DirectDown)", statusViewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void SoftwareUpdatePopup_HasPersistentStatusContractAndSharedService()
    {
        var root = FindProjectRoot();
        var xaml = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml"));
        var codeBehind = File.ReadAllText(Path.Combine(root, "v2rayN", "v2rayN", "Views", "MainWindow.xaml.cs"));

        var softwareUpdateIndex = xaml.IndexOf("x:Name=\"pbQuietUpdate\"", StringComparison.Ordinal);
        var coreUpdateIndex = xaml.IndexOf("x:Name=\"menuCheckUpdate\"", StringComparison.Ordinal);
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
        var clickHandler = codeBehind[codeBehind.IndexOf("private async void QuietUpdateCheckNow_Click", StringComparison.Ordinal)..];
        Assert.True(clickHandler.Split("pbQuietUpdate.IsPopupOpen = true;", StringSplitOptions.None).Length >= 3);
        Assert.Contains("更新检查失败，请稍后重试", clickHandler, StringComparison.Ordinal);
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
