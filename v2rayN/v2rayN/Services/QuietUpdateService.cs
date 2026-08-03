using System.Diagnostics;
using System.ComponentModel;
using System.IO.Compression;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace v2rayN.Services;

public interface IQuietClock { DateTimeOffset UtcNow { get; } }
public interface IQuietDelay { Task Delay(TimeSpan delay, CancellationToken token); }
public interface IQuietHttp { Task<Stream> GetAsync(Uri uri, CancellationToken token); }
public interface IQuietStateStore
{
    Task<QuietUpdateState?> ReadStateAsync(CancellationToken token);
    Task WriteStateAsync(QuietUpdateState state, CancellationToken token);
    Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken token);
}
public interface IQuietProcessLauncher { Process Launch(string fileName, string arguments, string workingDirectory); }
public sealed class QuietProcessLauncher : IQuietProcessLauncher
{
    public Process Launch(string fileName, string arguments, string workingDirectory)
    {
        return Process.Start(new ProcessStartInfo(fileName, arguments) { UseShellExecute = false, WorkingDirectory = workingDirectory })
            ?? throw new Win32Exception("Updater helper did not start.");
    }
}

public sealed class SystemQuietClock : IQuietClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class SystemQuietDelay : IQuietDelay { public Task Delay(TimeSpan delay, CancellationToken token) => Task.Delay(delay, token); }

public sealed class QuietHttp : IQuietHttp
{
    private readonly HttpClient _client;
    public QuietHttp()
    {
        _client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuietControlCenter", "7.24.3"));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }
    public async Task<Stream> GetAsync(Uri uri, CancellationToken token)
    {
        var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new ResponseStream(await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false), response);
    }
    private sealed class ResponseStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => false;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush(); public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin so) => inner.Seek(o, so); public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        public override ValueTask<int> ReadAsync(Memory<byte> b, CancellationToken t = default) => inner.ReadAsync(b, t);
        protected override void Dispose(bool disposing) { if (disposing) { inner.Dispose(); owner.Dispose(); } base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync(); owner.Dispose(); GC.SuppressFinalize(this); }
    }
}

public sealed class FileQuietStateStore : IQuietStateStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly string _directory;
    public FileQuietStateStore(string? directory = null) => _directory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QuietControlCenter");
    public Task<QuietUpdateState?> ReadStateAsync(CancellationToken token) => ReadAsync<QuietUpdateState>("update-state.json", token);
    public async Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken token)
        => await ReadAsync<QuietChannelConfig>("update-channel.json", token).ConfigureAwait(false)
           ?? QuietUpdateDefaults.CreateChannel();
    public async Task WriteStateAsync(QuietUpdateState state, CancellationToken token)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "update-state.json");
        var temp = path + ".tmp";
        await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            await JsonSerializer.SerializeAsync(stream, state, Options, token).ConfigureAwait(false);
        File.Move(temp, path, true);
    }
    private async Task<T?> ReadAsync<T>(string name, CancellationToken token)
    {
        try
        {
            var path = Path.Combine(_directory, name);
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token).ConfigureAwait(false);
        }
        catch (JsonException) { return default; }
    }
}

internal static class QuietUpdateDefaults
{
    public static QuietChannelConfig CreateChannel() => new()
    {
        ManifestUrl = "https://github.com/cc282855/v2rayN/releases/latest/download/quiet-update-manifest.json",
        ExpectedOwner = "cc282855",
        ExpectedRepository = "v2rayN",
        PublicKeyPem = """
            -----BEGIN PUBLIC KEY-----
            MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAE/OpybzxwVVABpIeDTDZsiGPw54IS
            k37qebQxgsfEhQ+f+smVwDPp5jgr+kp7WLkUbOks21X9d/0P4bBAQHwLiQ==
            -----END PUBLIC KEY-----
            """
    };
}

public sealed class QuietUpdateScheduler
{
    private readonly QuietUpdateService _service; private readonly IQuietDelay _delay;
    public QuietUpdateScheduler(QuietUpdateService service, IQuietDelay delay) { _service = service; _delay = delay; }
    public async Task RunAsync(string currentVersion, Func<QuietUpdateResult, Task> publish, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            QuietUpdateResult result;
            try { result = await _service.CheckAsync(currentVersion, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch { result = new QuietUpdateResult([], false); }
            if (result.CheckPerformed || result.Notices.Count > 0 || result.UpgradeStarted) await publish(result).ConfigureAwait(false);
            var untilDue = await _service.GetDelayUntilDueAsync(token).ConfigureAwait(false);
            await _delay.Delay(untilDue < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : untilDue, token).ConfigureAwait(false);
        }
    }
}

public sealed partial class QuietUpdateService
{
    private const string OfficialQueryError = "官方版本查询失败";
    private const string CustomQueryError = "定制版查询失败";
    private const string CustomValidationError = "定制版验证失败";
    private const string CustomInstallError = "定制版安装失败";
    private const string StateAccessError = "更新状态读写失败";
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/2dust/v2rayN/releases/latest");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly IQuietClock _clock; private readonly IQuietHttp _http; private readonly IQuietStateStore _state;
    private readonly IQuietProcessLauncher _processLauncher;
    private readonly TimeSpan _readyTimeout;
    private readonly object _sync = new(); private Task<QuietUpdateResult>? _inflight;
    private bool _forceRequested;
    private bool _stateWriteFailed;
    private QuietUpdateStatus _snapshot = QuietUpdateStatus.Empty;

    public QuietUpdateService(IQuietClock? clock = null, IQuietHttp? http = null, IQuietStateStore? state = null, IQuietProcessLauncher? processLauncher = null, TimeSpan? readyTimeout = null)
    { _clock = clock ?? new SystemQuietClock(); _http = http ?? new QuietHttp(); _state = state ?? new FileQuietStateStore(); _processLauncher = processLauncher ?? new QuietProcessLauncher(); _readyTimeout = readyTimeout ?? TimeSpan.FromMinutes(2); }

    public Task<QuietUpdateResult> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        return StartCheck(currentVersion, false, token);
    }
    public Task<QuietUpdateResult> CheckNowAsync(string currentVersion, CancellationToken token = default)
        => StartCheck(currentVersion, true, token);
    public QuietUpdateStatus Snapshot { get { lock (_sync) return _snapshot; } }
    public async Task<QuietUpdateStatus> GetStatusAsync(CancellationToken token = default)
    {
        try
        {
            var persisted = ToStatus(await _state.ReadStateAsync(token).ConfigureAwait(false), IsChecking);
            QuietUpdateStatus status;
            lock (_sync) status = _stateWriteFailed ? _snapshot with { IsChecking = _inflight is not null } : persisted;
            SetSnapshot(status);
            return status;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            var status = Snapshot with { LastError = StateAccessError };
            SetSnapshot(status);
            return status;
        }
    }
    public async Task<TimeSpan> GetDelayUntilDueAsync(CancellationToken token = default)
    {
        try
        {
            var state = await _state.ReadStateAsync(token).ConfigureAwait(false);
            var last = state?.LastAttemptUtc ?? state?.LastCheckedUtc;
            if (last is null || last > _clock.UtcNow.AddMinutes(5)) return TimeSpan.Zero;
            var remaining = CheckInterval - (_clock.UtcNow - last.Value);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return TimeSpan.Zero; }
    }
    private Task<QuietUpdateResult> StartCheck(string currentVersion, bool force, CancellationToken token)
    {
        lock (_sync)
        {
            if (_inflight is not null)
            {
                if (!force) return _inflight;
                _forceRequested = true;
                return EnsureForcedAfterAsync(_inflight, currentVersion, token);
            }
            _forceRequested = force;
            return _inflight = CompleteAsync(CheckCoreAsync(currentVersion, token));
        }
    }
    private async Task<QuietUpdateResult> EnsureForcedAfterAsync(Task<QuietUpdateResult> joined, string currentVersion, CancellationToken token)
    {
        var result = await joined.ConfigureAwait(false);
        if (result.CheckPerformed) return result;
        return await StartCheck(currentVersion, true, token).ConfigureAwait(false);
    }
    private async Task<QuietUpdateResult> CompleteAsync(Task<QuietUpdateResult> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally { lock (_sync) { _inflight = null; _forceRequested = false; } }
    }

    private async Task<QuietUpdateResult> CheckCoreAsync(string currentVersion, CancellationToken token)
    {
        // Ensure the coalesced task is published before even fully synchronous test doubles complete.
        await Task.Yield();
        var notices = new List<string>();
        QuietUpdateState state;
        try { state = await _state.ReadStateAsync(token).ConfigureAwait(false) ?? new(); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch
        {
            SetSnapshot(Snapshot with { LastError = StateAccessError, IsChecking = false });
            return new(notices, false, true);
        }

        var now = _clock.UtcNow;
        var lastAttempt = state.LastAttemptUtc ?? state.LastCheckedUtc;
        bool force;
        lock (_sync) force = _forceRequested;
        var due = ShouldCheck(lastAttempt, now);
        if (!force && !due)
        {
            // Re-evaluate the time boundary immediately before returning. A manual request
            // that joins after the force decision awaits this no-op and then starts one
            // serialized forced check in EnsureForcedAfterAsync.
            if (!ShouldCheck(lastAttempt, _clock.UtcNow))
            {
                SetSnapshot(ToStatus(state, false));
                return new(notices, false, false);
            }
        }

        state.LastAttemptUtc = now;
        state.LastCheckedUtc = now;
        await PersistStateAsync(state, true, token).ConfigureAwait(false);

        var errors = new List<string>();
        var upgradeStarted = false;

        try
        {
            var previousOfficial = state.LatestOfficial ?? state.LatestSeenTag;
            var release = await ReadJsonAsync<GitHubRelease>(LatestReleaseApi, token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(release?.TagName) || !TryParseVersion(release.TagName, out var latest))
                throw new InvalidDataException();

            state.LatestOfficial = release.TagName;
            state.LatestSeenTag = release.TagName;
            if (TryParseVersion(currentVersion, out var installed) && latest > installed
                && !string.Equals(previousOfficial, release.TagName, StringComparison.OrdinalIgnoreCase))
                notices.Add($"检测到官方 v2rayN {release.TagName}。官方 GUI 永远不会安装；只等待已签名的 Quiet Control Center 完整包。");
            await PersistStateAsync(state, true, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { errors.Add(OfficialQueryError); }

        try
        {
            var channel = await _state.ReadChannelAsync(token).ConfigureAwait(false);
            if (IsConfigured(channel, out var manifestUri))
            {
                var manifest = await ReadJsonAsync<QuietUpdateManifest>(manifestUri!, token).ConfigureAwait(false);
                if (!ValidateManifest(manifest, channel!, out _))
                {
                    errors.Add(CustomValidationError);
                }
                else
                {
                    state.LatestCustom = manifest!.AppVersion;
                    await PersistStateAsync(state, true, token).ConfigureAwait(false);
                    if (TryParseVersion(manifest.AppVersion, out var custom)
                        && TryParseVersion(currentVersion, out var installed) && custom > installed)
                    {
                        try
                        {
                            upgradeStarted = await DownloadVerifyAndLaunchAsync(manifest, channel!, token).ConfigureAwait(false);
                            if (upgradeStarted)
                                notices.Add($"已验证 Quiet Control Center {manifest.AppVersion} 的签名、完整包哈希和产品标记，正在安全更新。");
                            else
                                errors.Add(CustomInstallError);
                        }
                        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                        catch { errors.Add(CustomInstallError); }
                    }
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { errors.Add(CustomQueryError); }

        if (errors.Count == 0) state.LastSuccessUtc = now;
        state.LastCompletedUtc = now;
        state.LastError = errors.Count == 0 ? null : string.Join("；", errors.Distinct(StringComparer.Ordinal));
        await PersistStateAsync(state, false, token).ConfigureAwait(false);
        return new(notices, upgradeStarted, true);
    }

    private async Task PersistStateAsync(QuietUpdateState state, bool isChecking, CancellationToken token)
    {
        try
        {
            await _state.WriteStateAsync(state, token).ConfigureAwait(false);
            lock (_sync) _stateWriteFailed = false;
            SetSnapshot(ToStatus(state, isChecking));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            lock (_sync) _stateWriteFailed = true;
            SetSnapshot(ToStatus(state, isChecking) with { LastError = StateAccessError });
        }
    }

    private void SetSnapshot(QuietUpdateStatus status)
    {
        lock (_sync) _snapshot = status;
    }

    private bool IsChecking { get { lock (_sync) return _inflight is not null; } }

    private static QuietUpdateStatus ToStatus(QuietUpdateState? state, bool isChecking) => state is null
        ? QuietUpdateStatus.Empty with { IsChecking = isChecking }
        : new(state.LastAttemptUtc ?? state.LastCheckedUtc, state.LastSuccessUtc, state.LastError,
            state.LatestOfficial ?? state.LatestSeenTag, state.LatestCustom, state.LastCompletedUtc, isChecking);

    private async Task<bool> DownloadVerifyAndLaunchAsync(QuietUpdateManifest manifest, QuietChannelConfig channel, CancellationToken token)
    {
        var work = Path.Combine(Path.GetTempPath(), "QuietControlCenter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var handedOff = false;
        Process? helperProcess = null;
        string? cleanupInstallDirectory = null;
        string? cleanupToken = null;
        try
        {
            var package = Path.Combine(work, "package.zip");
            await using (var source = await _http.GetAsync(new Uri(manifest.AssetUrl), token).ConfigureAwait(false))
            await using (var target = new FileStream(package, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough))
            {
                var buffer = new byte[81920]; long total = 0; int read;
                while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
                { total += read; if (total > MaxPackageBytes) throw new InvalidDataException("Package size cap exceeded."); await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false); }
            }
            var hash = HashFile(package);
            if (!hash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Downloaded SHA-256 mismatch.");
            ValidatePackageMarker(package, manifest);

            var helperSource = Path.Combine(AppContext.BaseDirectory, "AmazTool.exe");
            var originExecutable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "v2rayN.exe"));
            if (!File.Exists(helperSource) || !File.Exists(originExecutable)) throw new InvalidDataException("Updater helper or origin executable is unavailable.");
            var helper = Path.Combine(work, "AmazTool.exe"); File.Copy(helperSource, helper);
            var tokenValue = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            var installDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
            _ = GetCanonicalStagePath(installDirectory, tokenValue);
            cleanupInstallDirectory = installDirectory;
            cleanupToken = tokenValue;
            var ack = Path.Combine(work, "startup.ack");
            using var current = Process.GetCurrentProcess();
            var instruction = new
            {
                schema = 1, product = "QuietControlCenter", packagePath = package, installDirectory,
                mainExecutable = "v2rayN.exe", expectedPackageSha256 = manifest.Sha256, expectedVersion = manifest.AppVersion,
                originExecutablePath = originExecutable, originExecutableSha256 = HashFile(originExecutable),
                processId = current.Id, processStartTimeUtc = new DateTimeOffset(current.StartTime.ToUniversalTime(), TimeSpan.Zero), token = tokenValue, ackPath = ack
            };
            var instructionPath = Path.Combine(work, "instruction.json");
            await File.WriteAllTextAsync(instructionPath, JsonSerializer.Serialize(instruction), token).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(work, "instruction.sha256"), HashFile(instructionPath), token).ConfigureAwait(false);

            using var readyPipe = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            var clientHandle = readyPipe.GetClientHandleAsString();
            helperProcess = _processLauncher.Launch(helper, $"qcc-upgrade \"{instructionPath}\" {clientHandle}", work);
            readyPipe.DisposeLocalCopyOfClientHandle();
            var ready = await WaitForReadyAsync(readyPipe, helperProcess, token).ConfigureAwait(false);
            if (!IsValidReady(ready, helperProcess.Id, instruction)) return false;
            handedOff = true;
            return true;
        }
        finally
        {
            if (!handedOff)
            {
                CleanupFailedHandoff(helperProcess, cleanupInstallDirectory, cleanupToken, work);
            }
            helperProcess?.Dispose();
        }
    }

    private async Task<QuietUpgradeReady?> WaitForReadyAsync(Stream pipe, Process helper, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(_readyTimeout);
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 1024, true);
        try
        {
            var readTask = reader.ReadLineAsync(timeout.Token).AsTask();
            var exitTask = helper.WaitForExitAsync(timeout.Token);
            var completed = await Task.WhenAny(readTask, exitTask).ConfigureAwait(false);
            if (completed != readTask && !readTask.IsCompletedSuccessfully) return null;
            var line = await readTask.ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(line) ? null : JsonSerializer.Deserialize<QuietUpgradeReady>(line, WebJson);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (JsonException) { return null; }
    }

    private static bool IsValidReady(QuietUpgradeReady? ready, int helperPid, object instructionObject)
    {
        var instruction = JsonSerializer.Deserialize<QuietUpgradeInstructionProof>(JsonSerializer.Serialize(instructionObject), WebJson);
        return ready is not null && instruction is not null
            && ready.Token == instruction.Token
            && ready.HelperProcessId == helperPid
            && ready.ParentProcessId == instruction.ProcessId
            && Math.Abs((ready.ParentStartTimeUtc - instruction.ProcessStartTimeUtc).TotalSeconds) <= 2
            && string.Equals(Path.GetFullPath(ready.ParentExecutablePath), Path.GetFullPath(instruction.OriginExecutablePath), StringComparison.OrdinalIgnoreCase)
            && string.Equals(ready.ParentExecutableSha256, instruction.OriginExecutableSha256, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldCheck(DateTimeOffset? last, DateTimeOffset now) => last is null || last > now.AddMinutes(5) || now - last >= CheckInterval;
    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new(); if (string.IsNullOrWhiteSpace(value)) return false;
        var match = VersionPattern().Match(value); return match.Success && Version.TryParse(match.Value, out version);
    }
    public static bool IsConfigured(QuietChannelConfig? c, out Uri? manifest)
    {
        manifest = null;
        return c is not null && !string.IsNullOrWhiteSpace(c.PublicKeyPem) && !string.IsNullOrWhiteSpace(c.ExpectedOwner) && !string.IsNullOrWhiteSpace(c.ExpectedRepository)
            && SafeHttps(c.ManifestUrl, out manifest) && IsExpectedGitHub(manifest!, c.ExpectedOwner, c.ExpectedRepository);
    }
    public static bool ValidateManifest(QuietUpdateManifest? m, QuietChannelConfig c, out string error)
    {
        error = "invalid manifest";
        if (m is null || m.Schema != 1 || m.Product != "QuietControlCenter" || m.Platform != "win-x64" || !TryParseVersion(m.AppVersion, out _)
            || !ShaPattern().IsMatch(m.Sha256 ?? "") || !SafeHttps(m.AssetUrl, out var asset) || !SafeHttps(m.ProvenanceUrl, out var provenance)
            || !IsExpectedGitHub(asset!, c.ExpectedOwner, c.ExpectedRepository) || !IsExpectedGitHub(provenance!, c.ExpectedOwner, c.ExpectedRepository)) return false;
        try
        {
            using var key = ECDsa.Create(); key.ImportFromPem(c.PublicKeyPem);
            if (!key.VerifyData(CanonicalBytes(m), Convert.FromBase64String(m.Signature ?? ""), HashAlgorithmName.SHA256)) { error = "signature"; return false; }
            error = ""; return true;
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { error = "signature"; return false; }
    }
    public static byte[] CanonicalBytes(QuietUpdateManifest m) => Encoding.UTF8.GetBytes(string.Join("\n", m.Schema, m.Product, m.AppVersion, m.Platform, m.AssetUrl, m.Sha256.ToLowerInvariant(), m.ProvenanceUrl) + "\n");

    private static bool SafeHttps(string? text, out Uri? uri) => Uri.TryCreate(text, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo);
    private static bool IsExpectedGitHub(Uri uri, string owner, string repo) =>
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        uri.AbsolutePath.StartsWith($"/{owner}/{repo}/", StringComparison.OrdinalIgnoreCase);
    private async Task<T?> ReadJsonAsync<T>(Uri uri, CancellationToken token)
    { await using var stream = await _http.GetAsync(uri, token).ConfigureAwait(false); return await JsonSerializer.DeserializeAsync<T>(stream, WebJson, token).ConfigureAwait(false); }
    private static string HashFile(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    private static void TryDeleteDirectory(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    private static bool TryKillExact(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
            return process.WaitForExit(5000) && process.HasExited;
        }
        catch { return false; }
    }

    internal static string GetCanonicalStagePath(string installDirectory, string token)
    {
        if (token.Length != 48 || !token.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid update nonce.");
        var install = Path.TrimEndingDirectorySeparator(Path.GetFullPath(installDirectory));
        var parent = Directory.GetParent(install)?.FullName ?? throw new InvalidDataException("Install directory has no parent.");
        var leaf = ".qcc-stage-" + token.ToUpperInvariant();
        var stage = Path.GetFullPath(Path.Combine(parent, leaf));
        if (!string.Equals(Directory.GetParent(stage)?.FullName, parent, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetFileName(stage), leaf, StringComparison.Ordinal))
            throw new InvalidDataException("Stage path is not canonical.");
        return stage;
    }

    internal static bool CleanupCanonicalStage(string installDirectory, string token)
        => TryDeleteCanonicalStage(GetCanonicalStagePath(installDirectory, token));

    internal static bool CleanupFailedHandoff(Process? helperProcess, string? installDirectory, string? token, string workRoot)
    {
        var helperTerminated = helperProcess is null || TryKillExact(helperProcess);
        if (!helperTerminated) return false;
        var stageRemoved = installDirectory is null || token is null || CleanupCanonicalStage(installDirectory, token);
        for (var attempt = 0; attempt < 20 && Directory.Exists(workRoot); attempt++)
        {
            TryDeleteDirectory(workRoot);
            if (Directory.Exists(workRoot)) Thread.Sleep(50);
        }
        return stageRemoved && !Directory.Exists(workRoot);
    }

    private static bool TryDeleteCanonicalStage(string stage)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try { DeleteTreeWithoutFollowingLinks(stage); } catch { }
            if (!Directory.Exists(stage)) return true;
            Thread.Sleep(50);
        }
        return false;
    }

    private static void DeleteTreeWithoutFollowingLinks(string path)
    {
        if (!Directory.Exists(path)) return;
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(path, false);
            return;
        }
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.TopDirectoryOnly))
        {
            try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            File.Delete(file);
        }
        foreach (var directory in Directory.EnumerateDirectories(path, "*", SearchOption.TopDirectoryOnly))
            DeleteTreeWithoutFollowingLinks(directory);
        Directory.Delete(path, false);
    }
    private static void ValidatePackageMarker(string package, QuietUpdateManifest manifest)
    {
        using var zip = ZipFile.OpenRead(package); var entry = zip.GetEntry("qcc-package.json") ?? throw new InvalidDataException("Package marker missing.");
        using var s = entry.Open(); var marker = JsonSerializer.Deserialize<PackageMarker>(s, WebJson) ?? throw new InvalidDataException("Package marker invalid.");
        if (marker.Product != "QuietControlCenter" || marker.Platform != "win-x64" || marker.Version != manifest.AppVersion || marker.Files.Count == 0) throw new InvalidDataException("Wrong product marker.");
        foreach (var pair in marker.Files)
        {
            if (IsMutablePackagePath(pair.Key)) throw new InvalidDataException("Mutable paths cannot be marker-owned.");
            var e = zip.GetEntry(pair.Key.Replace('\\', '/')) ?? throw new InvalidDataException("Marker file absent.");
            using var es = e.Open(); var actual = Convert.ToHexString(SHA256.HashData(es));
            if (!actual.Equals(pair.Value, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Payload marker hash mismatch.");
        }
        var payloadEntries = zip.Entries.Where(e => e.Name.Length > 0 && !e.FullName.Equals("qcc-package.json", StringComparison.OrdinalIgnoreCase))
            .Select(e => e.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!payloadEntries.SetEquals(marker.Files.Keys.Select(path => path.Replace('\\', '/'))))
            throw new InvalidDataException("Package contains unmarked payload files.");
    }
    private static bool IsMutablePackagePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return new[] { "guiConfigs", "guiLogs", "logs", "binConfigs" }.Any(root => normalized.Equals(root, StringComparison.OrdinalIgnoreCase) || normalized.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    }

    [GeneratedRegex(@"\d+(?:\.\d+){1,3}")] private static partial Regex VersionPattern();
    [GeneratedRegex("^[A-Fa-f0-9]{64}$")] private static partial Regex ShaPattern();
    private sealed class GitHubRelease { [JsonPropertyName("tag_name")] public string? TagName { get; set; } }
    private sealed class PackageMarker { public string Product { get; set; } = ""; public string Platform { get; set; } = ""; public string Version { get; set; } = ""; public Dictionary<string, string> Files { get; set; } = []; }
    private sealed class QuietUpgradeInstructionProof { public string Token { get; set; } = ""; public int ProcessId { get; set; } public DateTimeOffset ProcessStartTimeUtc { get; set; } public string OriginExecutablePath { get; set; } = ""; public string OriginExecutableSha256 { get; set; } = ""; }
}

public sealed class QuietUpgradeReady
{
    public string Token { get; set; } = "";
    public int HelperProcessId { get; set; }
    public int ParentProcessId { get; set; }
    public DateTimeOffset ParentStartTimeUtc { get; set; }
    public string ParentExecutablePath { get; set; } = "";
    public string ParentExecutableSha256 { get; set; } = "";
}

public sealed record QuietUpdateResult(IReadOnlyList<string> Notices, bool UpgradeStarted, bool CheckPerformed = false);
public sealed class QuietUpdateState
{
    // Legacy fields remain serialized so existing installations continue to observe the same schedule and notice history.
    public DateTimeOffset? LastCheckedUtc { get; set; }
    public string? LatestSeenTag { get; set; }
    public DateTimeOffset? LastAttemptUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
    public DateTimeOffset? LastCompletedUtc { get; set; }
    public string? LastError { get; set; }
    public string? LatestOfficial { get; set; }
    public string? LatestCustom { get; set; }
}
public sealed record QuietUpdateStatus(DateTimeOffset? LastAttemptUtc, DateTimeOffset? LastSuccessUtc,
    string? LastError, string? LatestOfficial, string? LatestCustom, DateTimeOffset? LastCompletedUtc, bool IsChecking)
{
    public static QuietUpdateStatus Empty { get; } = new(null, null, null, null, null, null, false);
}
public sealed class QuietChannelConfig { public string? ManifestUrl { get; set; } public string? PublicKeyPem { get; set; } public string? ExpectedOwner { get; set; } public string? ExpectedRepository { get; set; } }
public sealed class QuietUpdateManifest
{
    public int Schema { get; set; } public string Product { get; set; } = ""; public string AppVersion { get; set; } = ""; public string Platform { get; set; } = "";
    public string AssetUrl { get; set; } = ""; public string Sha256 { get; set; } = ""; public string ProvenanceUrl { get; set; } = ""; public string? Signature { get; set; }
}
