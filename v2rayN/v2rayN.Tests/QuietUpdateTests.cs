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

    [Fact] public async Task MissingChannelUsesPinnedProductionChannel()
    {
        var channel = await new FileQuietStateStore(Temp()).ReadChannelAsync(default);
        Assert.NotNull(channel);
        Assert.Equal("cc282855", channel.ExpectedOwner);
        Assert.Equal("v2rayN", channel.ExpectedRepository);
        Assert.Equal("https://github.com/cc282855/v2rayN/releases/latest/download/quiet-update-manifest.json", channel.ManifestUrl);
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
        var a = service.CheckAsync("7.24.3"); var b = service.CheckAsync("7.24.3");
        Assert.Same(a, b); http.Release(); await Task.WhenAll(a, b); Assert.Equal(1, http.Calls);
    }

    [Fact] public async Task NetworkFailureIsSilent()
    {
        var service = new QuietUpdateService(new FakeClock(), new ThrowHttp(), new MemoryStore());
        var result = await service.CheckAsync("7.24.3"); Assert.Empty(result.Notices); Assert.False(result.UpgradeStarted);
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
        using var cts = new CancellationTokenSource(); delay.OnSecond = cts.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new QuietUpdateScheduler(service, delay).RunAsync("7.24.3", _ => Task.CompletedTask, cts.Token));
        Assert.Equal(2, store.Writes);
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
    private sealed class MemoryStore : IQuietStateStore
    {
        public QuietUpdateState? State; public QuietChannelConfig? Channel; public int Writes;
        public Task<QuietUpdateState?> ReadStateAsync(CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult(State); }
        public Task WriteStateAsync(QuietUpdateState s, CancellationToken t) { t.ThrowIfCancellationRequested(); State = new() { LastCheckedUtc = s.LastCheckedUtc, LatestSeenTag = s.LatestSeenTag }; Writes++; return Task.CompletedTask; }
        public Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken t) => Task.FromResult(Channel);
    }
    private sealed class FakeHttp(byte[] bytes) : IQuietHttp { public Task<Stream> GetAsync(Uri u, CancellationToken t) { t.ThrowIfCancellationRequested(); return Task.FromResult<Stream>(new MemoryStream(bytes)); } }
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
