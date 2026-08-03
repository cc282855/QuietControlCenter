using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using v2rayN.Services;
using Xunit;

namespace v2rayN.Tests;

public sealed class QuietUpdateTests
{
    [Fact] public void DailyBoundaryIsInclusive()
    {
        var now = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
        Assert.False(QuietUpdateService.ShouldCheck(now.AddHours(-24).AddTicks(1), now));
        Assert.True(QuietUpdateService.ShouldCheck(now.AddHours(-24), now));
    }

    [Fact] public void FutureStateRecovers()
    {
        var now = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
        Assert.True(QuietUpdateService.ShouldCheck(now.AddHours(1), now));
    }

    [Fact] public async Task CorruptStateRecoversAsMissing()
    {
        var dir = Temp(); await File.WriteAllTextAsync(Path.Combine(dir, "update-state.json"), "{");
        Assert.Null(await new FileQuietStateStore(dir).ReadStateAsync(default));
    }

    [Fact] public async Task LegacyOnlyStateFeedsSnapshotAndDailySchedule()
    {
        var dir = Temp();
        var last = DateTimeOffset.Parse("2026-07-28T00:00:00Z");
        await File.WriteAllTextAsync(Path.Combine(dir, "update-state.json"),
            "{\"lastCheckedUtc\":\"2026-07-28T00:00:00Z\",\"latestSeenTag\":\"7.24.4\"}");
        var clock = new FakeClock { UtcNow = last.AddHours(23) };
        var service = new QuietUpdateService(clock, state: new FileQuietStateStore(dir));
        try
        {
            var status = await service.GetStatusAsync();
            Assert.Equal(last, status.LastAttemptUtc);
            Assert.Equal("7.24.4", status.LatestOfficial);
            Assert.Null(status.LastCompletedUtc);
            Assert.Equal(status, service.Snapshot);
            Assert.Equal(TimeSpan.FromHours(1), await service.GetDelayUntilDueAsync());
            clock.UtcNow = last.AddHours(24);
            Assert.Equal(TimeSpan.Zero, await service.GetDelayUntilDueAsync());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact] public async Task MissingChannelUsesPinnedProductionChannel()
    {
        var channel = await new FileQuietStateStore(Temp()).ReadChannelAsync(default);
        Assert.NotNull(channel);
        Assert.Equal("cc282855", channel.ExpectedOwner);
        Assert.Equal("QuietControlCenter", channel.ExpectedRepository);
        Assert.Equal("https://github.com/cc282855/QuietControlCenter/releases/latest/download/quiet-update-manifest.json", channel.ManifestUrl);
        Assert.True(QuietUpdateService.IsConfigured(channel, out _));
    }

    [Fact] public void ChannelIsDormantWithoutPinnedKey()
    {
        var c = new QuietChannelConfig { ManifestUrl = "https://github.com/a/b/releases/x", ExpectedOwner = "a", ExpectedRepository = "b" };
        Assert.False(QuietUpdateService.IsConfigured(c, out _));
    }

    [Theory]
    [InlineData("https://user:pass@github.com/a/b/releases/x")]
    [InlineData("http://github.com/a/b/releases/x")]
    [InlineData("https://github.com/evil/repo/releases/x")]
    public void UnsafeManifestUrlsFailClosed(string url)
    {
        var c = Config(url, "key"); Assert.False(QuietUpdateService.IsConfigured(c, out _));
    }

    [Fact] public void ValidSignatureAndProvenancePass()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var c = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem());
        var m = Manifest(); m.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(m), HashAlgorithmName.SHA256));
        Assert.True(QuietUpdateService.ValidateManifest(m, c, out _));
    }

    [Fact] public void ChangedHashBreaksSignature()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var c = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem());
        var m = Manifest(); m.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(m), HashAlgorithmName.SHA256));
        m.Sha256 = new string('B', 64);
        Assert.False(QuietUpdateService.ValidateManifest(m, c, out var error)); Assert.Equal("signature", error);
    }

    [Fact] public async Task OfficialReleaseIsNoticeOnly()
    {
        var http = new FakeHttp(JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }));
        var service = new QuietUpdateService(new FakeClock(), http, new MemoryStore());
        var result = await service.CheckAsync("7.24.3");
        Assert.False(result.UpgradeStarted); Assert.Single(result.Notices);
    }

    [Fact] public async Task ConcurrentCallsAreCoalesced()
    {
        var http = new BlockingHttp(); var service = new QuietUpdateService(new FakeClock(), http, new MemoryStore());
        var a = service.CheckAsync("7.24.3"); var b = service.CheckNowAsync("7.24.3");
        http.Release();
        var results = await Task.WhenAll(a, b);
        Assert.All(results, result => Assert.True(result.CheckPerformed));
        Assert.Equal(1, http.Calls);
    }

    [Fact] public async Task LateManualCheckUpgradesThrottledInflightWithoutMissingNetwork()
    {
        var now = DateTimeOffset.Parse("2026-07-29T00:00:00Z");
        var clock = new LateForceClock(now);
        var store = new MemoryStore { State = new QuietUpdateState { LastCheckedUtc = now } };
        var http = new TrackingHttp(_ => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.24.3" }));
        var service = new QuietUpdateService(clock, http, store);

        var scheduled = service.CheckAsync("7.24.3");
        Assert.True(clock.WaitUntilLateForceWindow(TimeSpan.FromSeconds(5)));
        var manual = service.CheckNowAsync("7.24.3");
        clock.Release();
        var results = await Task.WhenAll(scheduled, manual);

        Assert.False(results[0].CheckPerformed);
        Assert.True(results[1].CheckPerformed);
        Assert.Single(http.Uris);
    }

    [Fact] public async Task CheckNowBypassesDailyThrottleOnly()
    {
        var clock = new FakeClock();
        var store = new MemoryStore { State = new QuietUpdateState { LastCheckedUtc = clock.UtcNow } };
        var http = new TrackingHttp(_ => JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.24.3" }));
        var service = new QuietUpdateService(clock, http, store);

        await service.CheckAsync("7.24.3");
        Assert.Empty(http.Uris);
        await service.CheckNowAsync("7.24.3");

        Assert.Single(http.Uris);
        Assert.Equal("api.github.com", http.Uris[0].Host);
    }

    [Fact] public async Task SuccessfulSegmentsPersistBackwardCompatibleStatus()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Manifest();
        manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        var clock = new FakeClock();
        var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var service = new QuietUpdateService(clock, new RouteHttp(
            JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }),
            JsonSerializer.SerializeToUtf8Bytes(manifest)), store);

        await service.CheckAsync("7.25.0");
        var status = await service.GetStatusAsync();

        Assert.Equal(clock.UtcNow, store.State!.LastCheckedUtc);
        Assert.Equal("7.25.0", store.State.LatestSeenTag);
        Assert.Equal(clock.UtcNow, status.LastAttemptUtc);
        Assert.Equal(clock.UtcNow, status.LastSuccessUtc);
        Assert.Equal("7.25.0", status.LatestOfficial);
        Assert.Equal("7.25.0", status.LatestCustom);
        Assert.Null(status.LastError);
        Assert.Equal(status, service.Snapshot);
    }

    [Fact] public async Task OfficialFailureDoesNotDiscardValidatedCustomVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Manifest();
        manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var http = new TrackingHttp(uri => uri.Host == "api.github.com"
            ? throw new HttpRequestException("secret diagnostic must not persist")
            : JsonSerializer.SerializeToUtf8Bytes(manifest));

        await new QuietUpdateService(new FakeClock(), http, store).CheckAsync("7.25.0");

        Assert.Equal("7.25.0", store.State!.LatestCustom);
        Assert.Equal("官方版本查询失败", store.State.LastError);
        Assert.DoesNotContain("secret", store.State.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task CustomFailureDoesNotDiscardOfficialVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var http = new TrackingHttp(uri => uri.Host == "api.github.com"
            ? JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" })
            : throw new HttpRequestException("sensitive transport details"));

        await new QuietUpdateService(new FakeClock(), http, store).CheckAsync("7.25.0");

        Assert.Equal("7.25.0", store.State!.LatestOfficial);
        Assert.Equal("7.25.0", store.State.LatestSeenTag);
        Assert.Null(store.State.LatestCustom);
        Assert.Equal("定制版查询失败", store.State.LastError);
    }

    [Fact] public async Task UnvalidatedManifestCannotBecomeLatestCustomVersion()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var manifest = Manifest();
        manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        manifest.AppVersion = "98.0.0";
        var store = new MemoryStore
        {
            State = new QuietUpdateState { LatestCustom = "7.24.3" },
            Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem())
        };
        var http = new RouteHttp(
            JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }),
            JsonSerializer.SerializeToUtf8Bytes(manifest));

        await new QuietUpdateService(new FakeClock(), http, store).CheckAsync("7.24.3");

        Assert.Equal("7.24.3", store.State!.LatestCustom);
        Assert.Equal("定制版验证失败", store.State.LastError);
    }

    [Fact] public async Task OfficialReleaseAssetIsNeverDownloaded()
    {
        var http = new TrackingHttp(_ => JsonSerializer.SerializeToUtf8Bytes(new
        {
            tag_name = "99.0.0",
            assets = new[] { new { browser_download_url = "https://example.invalid/official-gui.zip" } }
        }));

        var result = await new QuietUpdateService(new FakeClock(), http, new MemoryStore()).CheckAsync("7.24.3");

        Assert.False(result.UpgradeStarted);
        Assert.Single(http.Uris);
        Assert.Equal("https://api.github.com/repos/2dust/v2rayN/releases/latest", http.Uris[0].AbsoluteUri);
    }

    [Fact] public async Task NetworkFailureIsSilent()
    {
        var service = new QuietUpdateService(new FakeClock(), new ThrowHttp(), new MemoryStore());
        var result = await service.CheckAsync("7.24.3"); Assert.Empty(result.Notices); Assert.False(result.UpgradeStarted);
    }

    [Fact] public async Task StateWriteFailureIsSanitizedAndDoesNotEscape()
    {
        var service = new QuietUpdateService(new FakeClock(),
            new FakeHttp(JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.24.3" })), new ThrowingStore(throwOnWrite: true));

        var result = await service.CheckNowAsync("7.24.3");
        var status = await service.GetStatusAsync();

        Assert.True(result.CheckPerformed);
        Assert.Equal("更新状态读写失败", status.LastError);
        Assert.DoesNotContain("fixture secret", status.LastError, StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public async Task StateReadFailureIsSanitizedAndDoesNotEscape()
    {
        var service = new QuietUpdateService(new FakeClock(), state: new ThrowingStore(throwOnRead: true));

        var result = await service.CheckNowAsync("7.24.3");
        var status = await service.GetStatusAsync();

        Assert.True(result.CheckPerformed);
        Assert.Equal("更新状态读写失败", status.LastError);
    }

    [Fact] public async Task ValidSignedPackageReachesExternalHelper()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes("fixture executable");
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                var exe = zip.CreateEntry("v2rayN.exe"); await using (var s = exe.Open()) await s.WriteAsync(payload);
                var marker = zip.CreateEntry("qcc-package.json"); await using var ms = marker.Open();
                await JsonSerializer.SerializeAsync(ms, new { product = "QuietControlCenter", platform = "win-x64", version = "7.25.0", files = new Dictionary<string, string> { ["v2rayN.exe"] = payloadHash } });
            }
            zipBytes = buffer.ToArray();
        }
        var manifest = Manifest(); manifest.Sha256 = Convert.ToHexString(SHA256.HashData(zipBytes));
        manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var http = new RouteHttp(JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }), JsonSerializer.SerializeToUtf8Bytes(manifest), zipBytes);
        var helper = Path.Combine(AppContext.BaseDirectory, "AmazTool.exe");
        var origin = Path.Combine(AppContext.BaseDirectory, "v2rayN.exe");
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), helper, true);
        File.Copy(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe"), origin, true);
        var launcher = new FakeLauncher();
        try
        {
            var result = await new QuietUpdateService(new FakeClock(), http, store, launcher).CheckAsync("7.24.3");
            Assert.True(result.UpgradeStarted);
        }
        finally
        {
            File.Delete(helper); File.Delete(origin);
            foreach (var root in launcher.WorkRoots) if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact] public async Task ReadyTimeoutIsCleanedAndSchedulerRetriesNextDay()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes("fixture"); var payloadHash = Convert.ToHexString(SHA256.HashData(payload)); byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                var exe = zip.CreateEntry("v2rayN.exe"); using (var s = exe.Open()) s.Write(payload);
                var marker = zip.CreateEntry("qcc-package.json"); using var ms = marker.Open(); JsonSerializer.Serialize(ms, new { product = "QuietControlCenter", platform = "win-x64", version = "7.25.0", files = new Dictionary<string, string> { ["v2rayN.exe"] = payloadHash } });
            }
            zipBytes = buffer.ToArray();
        }
        var manifest = Manifest(); manifest.Sha256 = Convert.ToHexString(SHA256.HashData(zipBytes)); manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        var responses = Enumerable.Range(0, 2).SelectMany(_ => new[] { JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }), JsonSerializer.SerializeToUtf8Bytes(manifest), zipBytes }).ToArray();
        var clock = new FakeClock(); var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var launcher = new FlakyLauncher(); var delay = new AdvancingDelay(clock); using var cts = new CancellationTokenSource(); delay.OnSecond = cts.Cancel;
        var helper = Path.Combine(AppContext.BaseDirectory, "AmazTool.exe"); var origin = Path.Combine(AppContext.BaseDirectory, "v2rayN.exe");
        File.Copy(SystemExe("where.exe"), helper, true); File.Copy(SystemExe("where.exe"), origin, true);
        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QuietUpdateScheduler(new QuietUpdateService(clock, new RouteHttp(responses), store, launcher, TimeSpan.FromMilliseconds(150)), delay).RunAsync("7.24.3", _ => Task.CompletedTask, cts.Token));
            Assert.Equal(2, launcher.Calls); Assert.False(Directory.Exists(launcher.WorkRoots[0]));
        }
        finally { File.Delete(helper); File.Delete(origin); foreach (var root in launcher.WorkRoots) if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact] public async Task LongRunningSchedulerPerformsSecondCheck()
    {
        var clock = new FakeClock(); var store = new MemoryStore(); var delay = new AdvancingDelay(clock);
        var service = new QuietUpdateService(clock, new FakeHttp(JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.24.3" })), store);
        var published = 0;
        using var cts = new CancellationTokenSource(); delay.OnSecond = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QuietUpdateScheduler(service, delay).RunAsync("7.24.3", _ => { published++; return Task.CompletedTask; }, cts.Token));
        Assert.Equal(2, store.Attempts);
        Assert.Equal(2, published);
    }

    [Theory]
    [InlineData(ReadyMode.Timeout)]
    [InlineData(ReadyMode.WrongToken)]
    [InlineData(ReadyMode.WrongHelperPid)]
    [InlineData(ReadyMode.WrongParent)]
    [InlineData(ReadyMode.WrongParentStart)]
    [InlineData(ReadyMode.WrongParentPath)]
    [InlineData(ReadyMode.WrongParentHash)]
    [InlineData(ReadyMode.ExitEarly)]
    public async Task InvalidOrMissingReadyKeepsGuiRunningAndCleansWork(ReadyMode mode)
    {
        var launcher = new ProtocolLauncher(mode);

        var result = await RunSignedUpdateAsync(launcher, TimeSpan.FromMilliseconds(150));

        Assert.False(result.UpgradeStarted);
        Assert.Single(launcher.WorkRoots);
        Assert.False(Directory.Exists(launcher.WorkRoots[0]));
        Assert.All(launcher.StagePaths, stage => Assert.False(Directory.Exists(stage)));
        Assert.False(IsProcessAlive(launcher.ProcessId));
    }

    [Fact]
    public async Task CancellationDuringReadyWaitTerminatesHelperAndCleansStageAndWork()
    {
        var launcher = new ProtocolLauncher(ReadyMode.Timeout);
        using var cancellation = new CancellationTokenSource();
        var check = RunSignedUpdateAsync(launcher, TimeSpan.FromSeconds(5), cancellation.Token);
        Assert.True(SpinWait.SpinUntil(() => launcher.StagePaths.Count == 1, TimeSpan.FromSeconds(5)));
        cancellation.Cancel();

        try
        {
            var result = await check;
            Assert.False(result.UpgradeStarted);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }

        Assert.Single(launcher.WorkRoots);
        Assert.False(Directory.Exists(launcher.WorkRoots[0]));
        Assert.All(launcher.StagePaths, stage => Assert.False(Directory.Exists(stage)));
        Assert.False(IsProcessAlive(launcher.ProcessId));
    }

    [Fact] public async Task CancellationStopsNetwork()
    {
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QuietUpdateService(new FakeClock(), new FakeHttp([]), new MemoryStore()).CheckAsync("7.24.3", cts.Token));
    }

    private static QuietChannelConfig Config(string url, string key) => new() { ManifestUrl = url, PublicKeyPem = key, ExpectedOwner = "owner", ExpectedRepository = "repo" };
    private static QuietUpdateManifest Manifest() => new() { Schema = 1, Product = "QuietControlCenter", AppVersion = "7.25.0", Platform = "win-x64", AssetUrl = "https://github.com/owner/repo/releases/download/v/qcc.zip", Sha256 = new string('A', 64), ProvenanceUrl = "https://github.com/owner/repo/actions/runs/1" };
    private static string Temp() { var p = Path.Combine(Path.GetTempPath(), "qcc-tests", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(p); return p; }
    private static string SystemExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    private static bool IsProcessAlive(int processId)
    {
        if (processId <= 0) return false;
        try { using var process = Process.GetProcessById(processId); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }

    private static async Task<QuietUpdateResult> RunSignedUpdateAsync(IQuietProcessLauncher launcher, TimeSpan readyTimeout, CancellationToken cancellationToken = default)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var payload = Encoding.UTF8.GetBytes("fixture executable");
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload));
        byte[] zipBytes;
        using (var buffer = new MemoryStream())
        {
            using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
            {
                var exe = zip.CreateEntry("v2rayN.exe"); await using (var stream = exe.Open()) await stream.WriteAsync(payload);
                var marker = zip.CreateEntry("qcc-package.json"); await using var markerStream = marker.Open();
                await JsonSerializer.SerializeAsync(markerStream, new { product = "QuietControlCenter", platform = "win-x64", version = "7.25.0", files = new Dictionary<string, string> { ["v2rayN.exe"] = payloadHash } });
            }
            zipBytes = buffer.ToArray();
        }

        var manifest = Manifest();
        manifest.Sha256 = Convert.ToHexString(SHA256.HashData(zipBytes));
        manifest.Signature = Convert.ToBase64String(key.SignData(QuietUpdateService.CanonicalBytes(manifest), HashAlgorithmName.SHA256));
        var store = new MemoryStore { Channel = Config("https://github.com/owner/repo/releases/download/v/manifest.json", key.ExportSubjectPublicKeyInfoPem()) };
        var http = new RouteHttp(JsonSerializer.SerializeToUtf8Bytes(new { tag_name = "7.25.0" }), JsonSerializer.SerializeToUtf8Bytes(manifest), zipBytes);
        var helper = Path.Combine(AppContext.BaseDirectory, "AmazTool.exe");
        var origin = Path.Combine(AppContext.BaseDirectory, "v2rayN.exe");
        File.Copy(SystemExe("where.exe"), helper, true);
        File.Copy(SystemExe("where.exe"), origin, true);
        try { return await new QuietUpdateService(new FakeClock(), http, store, launcher, readyTimeout).CheckAsync("7.24.3", cancellationToken); }
        finally { File.Delete(helper); File.Delete(origin); }
    }

    private sealed class FakeClock : IQuietClock { public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.Parse("2026-07-29T00:00:00Z"); }
    private sealed class LateForceClock(DateTimeOffset now) : IQuietClock
    {
        private readonly ManualResetEventSlim _entered = new();
        private readonly ManualResetEventSlim _release = new();
        private int _reads;
        public DateTimeOffset UtcNow
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 2)
                {
                    _entered.Set();
                    _release.Wait(TimeSpan.FromSeconds(10));
                }
                return now;
            }
        }
        public bool WaitUntilLateForceWindow(TimeSpan timeout) => _entered.Wait(timeout);
        public void Release() => _release.Set();
    }
    private sealed class MemoryStore : IQuietStateStore
    {
        public QuietUpdateState? State; public QuietChannelConfig? Channel; public int Writes; public int Attempts;
        private DateTimeOffset? _lastPersistedAttempt;
        public Task<QuietUpdateState?> ReadStateAsync(CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult(State); }
        public Task WriteStateAsync(QuietUpdateState s, CancellationToken t)
        {
            t.ThrowIfCancellationRequested();
            if (_lastPersistedAttempt != s.LastAttemptUtc && s.LastAttemptUtc is not null) Attempts++;
            _lastPersistedAttempt = s.LastAttemptUtc;
            State = new()
            {
                LastCheckedUtc = s.LastCheckedUtc,
                LatestSeenTag = s.LatestSeenTag,
                LastAttemptUtc = s.LastAttemptUtc,
                LastSuccessUtc = s.LastSuccessUtc,
                LastCompletedUtc = s.LastCompletedUtc,
                LastError = s.LastError,
                LatestOfficial = s.LatestOfficial,
                LatestCustom = s.LatestCustom
            };
            Writes++;
            return Task.CompletedTask;
        }
        public Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken t) => Task.FromResult(Channel);
    }
    private sealed class ThrowingStore(bool throwOnRead = false, bool throwOnWrite = false) : IQuietStateStore
    {
        public Task<QuietUpdateState?> ReadStateAsync(CancellationToken token)
            => throwOnRead ? throw new IOException("fixture secret read path") : Task.FromResult<QuietUpdateState?>(null);
        public Task WriteStateAsync(QuietUpdateState state, CancellationToken token)
            => throwOnWrite ? throw new UnauthorizedAccessException("fixture secret write path") : Task.CompletedTask;
        public Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken token) => Task.FromResult<QuietChannelConfig?>(null);
    }
    private sealed class FakeHttp(byte[] bytes) : IQuietHttp { public Task<Stream> GetAsync(Uri u, CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(bytes)); } }
    private sealed class TrackingHttp(Func<Uri, byte[]> response) : IQuietHttp
    {
        public List<Uri> Uris { get; } = [];
        public Task<Stream> GetAsync(Uri uri, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Uris.Add(uri);
            return Task.FromResult<Stream>(new MemoryStream(response(uri)));
        }
    }
    private sealed class ThrowHttp : IQuietHttp { public Task<Stream> GetAsync(Uri u, CancellationToken t) => throw new HttpRequestException(); }
    private sealed class FakeLauncher : IQuietProcessLauncher
    {
        public List<string> WorkRoots { get; } = [];
        public Process Launch(string f, string a, string w) { WorkRoots.Add(w); return LaunchReadyProcess(a); }
    }
    private sealed class FlakyLauncher : IQuietProcessLauncher
    {
        public int Calls; public List<string> WorkRoots { get; } = [];
        public Process Launch(string f, string a, string w)
        {
            Calls++; WorkRoots.Add(w);
            if (Calls == 1) return LaunchTimeoutProcess(a);
            return LaunchReadyProcess(a);
        }
    }

    public enum ReadyMode { Timeout, WrongToken, WrongHelperPid, WrongParent, WrongParentStart, WrongParentPath, WrongParentHash, ExitEarly }

    private sealed class ProtocolLauncher(ReadyMode mode) : IQuietProcessLauncher
    {
        public List<string> WorkRoots { get; } = [];
        public List<string> StagePaths { get; } = [];
        public Process? Process { get; private set; }
        public int ProcessId { get; private set; }

        public Process Launch(string f, string a, string w)
        {
            WorkRoots.Add(w);
            var (instructionPath, pipeHandle) = ParseUpgradeArguments(a);
            if (mode == ReadyMode.ExitEarly)
            {
                Process = System.Diagnostics.Process.Start(new ProcessStartInfo("cmd.exe", "/c exit 0") { UseShellExecute = false, CreateNoWindow = true })!;
                ProcessId = Process.Id;
                return Process;
            }

            Process = System.Diagnostics.Process.Start(new ProcessStartInfo(SystemExe("ping.exe"), "-n 5 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
            ProcessId = Process.Id;
            var pipe = new AnonymousPipeClientStream(PipeDirection.Out,
                mode == ReadyMode.Timeout ? DuplicatePipeHandle(pipeHandle) : pipeHandle);
            if (mode == ReadyMode.Timeout)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(instructionPath));
                var instruction = document.RootElement;
                var stage = QuietUpdateService.GetCanonicalStagePath(
                    instruction.GetProperty("installDirectory").GetString()!,
                    instruction.GetProperty("token").GetString()!);
                Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(stage, "partial-payload.bin"), "partial");
                StagePaths.Add(stage);
                _ = Task.Run(async () => { using (pipe) await Task.Delay(1000); });
                return Process;
            }

            using (pipe)
            using (var writer = new StreamWriter(pipe) { AutoFlush = true })
            using (var document = JsonDocument.Parse(File.ReadAllText(instructionPath)))
            {
                var instruction = document.RootElement;
                writer.WriteLine(JsonSerializer.Serialize(new QuietUpgradeReady
                {
                    Token = mode == ReadyMode.WrongToken ? "forged-token" : instruction.GetProperty("token").GetString()!,
                    HelperProcessId = mode == ReadyMode.WrongHelperPid ? Process.Id + 1 : Process.Id,
                    ParentProcessId = mode == ReadyMode.WrongParent ? instruction.GetProperty("processId").GetInt32() + 1 : instruction.GetProperty("processId").GetInt32(),
                    ParentStartTimeUtc = mode == ReadyMode.WrongParentStart ? instruction.GetProperty("processStartTimeUtc").GetDateTimeOffset().AddMinutes(1) : instruction.GetProperty("processStartTimeUtc").GetDateTimeOffset(),
                    ParentExecutablePath = mode == ReadyMode.WrongParentPath ? SystemExe("cmd.exe") : instruction.GetProperty("originExecutablePath").GetString()!,
                    ParentExecutableSha256 = mode == ReadyMode.WrongParentHash ? new string('0', 64) : instruction.GetProperty("originExecutableSha256").GetString()!
                }));
            }
            return Process;
        }
    }

    private static Process LaunchReadyProcess(string arguments)
    {
        var (instructionPath, pipeHandle) = ParseUpgradeArguments(arguments);
        using var document = JsonDocument.Parse(File.ReadAllText(instructionPath));
        var instruction = document.RootElement;
        var process = Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul")
        { UseShellExecute = false, CreateNoWindow = true })!;
        using var pipe = new AnonymousPipeClientStream(PipeDirection.Out, pipeHandle);
        using var writer = new StreamWriter(pipe) { AutoFlush = true };
        writer.WriteLine(JsonSerializer.Serialize(new QuietUpgradeReady
        {
            Token = instruction.GetProperty("token").GetString()!,
            HelperProcessId = process.Id,
            ParentProcessId = instruction.GetProperty("processId").GetInt32(),
            ParentStartTimeUtc = instruction.GetProperty("processStartTimeUtc").GetDateTimeOffset(),
            ParentExecutablePath = instruction.GetProperty("originExecutablePath").GetString()!,
            ParentExecutableSha256 = instruction.GetProperty("originExecutableSha256").GetString()!
        }));
        return process;
    }

    private static Process LaunchTimeoutProcess(string arguments)
    {
        var (_, pipeHandle) = ParseUpgradeArguments(arguments);
        var process = Process.Start(new ProcessStartInfo(SystemExe("ping.exe"), "-n 5 127.0.0.1")
        { UseShellExecute = false, CreateNoWindow = true })!;
        var pipe = new AnonymousPipeClientStream(PipeDirection.Out, DuplicatePipeHandle(pipeHandle));
        _ = Task.Run(async () => { using (pipe) await Task.Delay(1000); });
        return process;
    }

    private static string DuplicatePipeHandle(string handle)
    {
        var current = GetCurrentProcess();
        if (!DuplicateHandle(current, new IntPtr(long.Parse(handle)), current, out var duplicate, 0, false, 2))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        return duplicate.ToInt64().ToString();
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DuplicateHandle(IntPtr sourceProcess, IntPtr sourceHandle, IntPtr targetProcess,
        out IntPtr targetHandle, uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint options);

    private static (string InstructionPath, string PipeHandle) ParseUpgradeArguments(string arguments)
    {
        const string prefix = "qcc-upgrade \"";
        Assert.StartsWith(prefix, arguments);
        var endQuote = arguments.IndexOf('\"', prefix.Length);
        Assert.True(endQuote > prefix.Length);
        return (arguments[prefix.Length..endQuote], arguments[(endQuote + 1)..].Trim());
    }
    private sealed class RouteHttp(params byte[][] responses) : IQuietHttp
    {
        private int _index;
        public Task<Stream> GetAsync(Uri u, CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(responses[_index++])); }
    }
    private sealed class BlockingHttp : IQuietHttp
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously); public int Calls;
        public void Release() => _release.SetResult();
        public async Task<Stream> GetAsync(Uri u, CancellationToken t) { Calls++; await _release.Task.WaitAsync(t); return new MemoryStream(Encoding.UTF8.GetBytes("{\"tag_name\":\"7.24.3\"}")); }
    }
    private sealed class AdvancingDelay(FakeClock clock) : IQuietDelay
    {
        private int _calls; public Action? OnSecond;
        public Task Delay(TimeSpan d, CancellationToken t) { clock.UtcNow += d; if (++_calls == 2) OnSecond?.Invoke(); t.ThrowIfCancellationRequested(); return Task.CompletedTask; }
    }
}
