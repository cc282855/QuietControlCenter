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
    public Task<QuietChannelConfig?> ReadChannelAsync(CancellationToken token) => ReadAsync<QuietChannelConfig>("update-channel.json", token);
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
            if (result.Notices.Count > 0 || result.UpgradeStarted) await publish(result).ConfigureAwait(false);
            var untilDue = await _service.GetDelayUntilDueAsync(token).ConfigureAwait(false);
            await _delay.Delay(untilDue < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : untilDue, token).ConfigureAwait(false);
        }
    }
}

public sealed partial class QuietUpdateService
{
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private static readonly Uri LatestReleaseApi = new("https://api.github.com/repos/2dust/v2rayN/releases/latest");
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly IQuietClock _clock; private readonly IQuietHttp _http; private readonly IQuietStateStore _state;
    private readonly IQuietProcessLauncher _processLauncher;
    private readonly TimeSpan _readyTimeout;
    private readonly object _sync = new(); private Task<QuietUpdateResult>? _inflight;

    public QuietUpdateService(IQuietClock? clock = null, IQuietHttp? http = null, IQuietStateStore? state = null, IQuietProcessLauncher? processLauncher = null, TimeSpan? readyTimeout = null)
    { _clock = clock ?? new SystemQuietClock(); _http = http ?? new QuietHttp(); _state = state ?? new FileQuietStateStore(); _processLauncher = processLauncher ?? new QuietProcessLauncher(); _readyTimeout = readyTimeout ?? TimeSpan.FromMinutes(2); }

    public Task<QuietUpdateResult> CheckAsync(string currentVersion, CancellationToken token = default)
    {
        lock (_sync) return _inflight ??= CompleteAsync(CheckCoreAsync(currentVersion, token));
    }
    public async Task<TimeSpan> GetDelayUntilDueAsync(CancellationToken token = default)
    {
        try
        {
            var last = (await _state.ReadStateAsync(token).ConfigureAwait(false))?.LastCheckedUtc;
            if (last is null || last > _clock.UtcNow.AddMinutes(5)) return TimeSpan.Zero;
            var remaining = CheckInterval - (_clock.UtcNow - last.Value);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return TimeSpan.Zero; }
    }
    private async Task<QuietUpdateResult> CompleteAsync(Task<QuietUpdateResult> task)
    {
        try { return await task.ConfigureAwait(false); }
        finally { lock (_sync) _inflight = null; }
    }

    private async Task<QuietUpdateResult> CheckCoreAsync(string currentVersion, CancellationToken token)
    {
        // Ensure the coalesced task is published before even fully synchronous test doubles complete.
        await Task.Yield();
        var notices = new List<string>();
        try
        {
            var state = await _state.ReadStateAsync(token).ConfigureAwait(false) ?? new();
            var now = _clock.UtcNow;
            if (!ShouldCheck(state.LastCheckedUtc, now)) return new(notices, false);
            state.LastCheckedUtc = now;
            await _state.WriteStateAsync(state, token).ConfigureAwait(false);

            var release = await ReadJsonAsync<GitHubRelease>(LatestReleaseApi, token).ConfigureAwait(false);
            if (TryParseVersion(currentVersion, out var installed) && TryParseVersion(release?.TagName, out var latest) && latest > installed)
            {
                if (!string.Equals(state.LatestSeenTag, release!.TagName, StringComparison.OrdinalIgnoreCase))
                    notices.Add($"检测到官方 v2rayN {release.TagName}。官方 GUI 永远不会安装；只等待已签名的 Quiet Control Center 完整包。");
                state.LatestSeenTag = release.TagName;
                await _state.WriteStateAsync(state, token).ConfigureAwait(false);
            }

            var channel = await _state.ReadChannelAsync(token).ConfigureAwait(false);
            if (!IsConfigured(channel, out var manifestUri)) return new(notices, false);
            var manifest = await ReadJsonAsync<QuietUpdateManifest>(manifestUri!, token).ConfigureAwait(false);
            if (!ValidateManifest(manifest, channel!, out _) || !TryParseVersion(manifest!.AppVersion, out var custom) || !TryParseVersion(currentVersion, out installed) || custom <= installed)
                return new(notices, false);

            var started = await DownloadVerifyAndLaunchAsync(manifest, channel!, token).ConfigureAwait(false);
            if (started) notices.Add($"已验证 Quiet Control Center {manifest.AppVersion} 的签名、完整包哈希和产品标记，正在安全更新。");
            return new(notices, started);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch { return new(notices, false); }
    }

    private async Task<bool> DownloadVerifyAndLaunchAsync(QuietUpdateManifest manifest, QuietChannelConfig channel, CancellationToken token)
    {
        var work = Path.Combine(Path.GetTempPath(), "QuietControlCenter", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        var handedOff = false;
        Process? helperProcess = null;
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
            var ack = Path.Combine(work, "startup.ack");
            using var current = Process.GetCurrentProcess();
            var instruction = new
            {
                schema = 1, product = "QuietControlCenter", packagePath = package, installDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory),
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
                TryKillExact(helperProcess);
                TryDeleteDirectory(work);
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
    private static void TryKillExact(Process? process) { try { if (process is { HasExited: false }) { process.Kill(true); process.WaitForExit(5000); } } catch { } }
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

public sealed record QuietUpdateResult(IReadOnlyList<string> Notices, bool UpgradeStarted);
public sealed class QuietUpdateState { public DateTimeOffset? LastCheckedUtc { get; set; } public string? LatestSeenTag { get; set; } }
public sealed class QuietChannelConfig { public string? ManifestUrl { get; set; } public string? PublicKeyPem { get; set; } public string? ExpectedOwner { get; set; } public string? ExpectedRepository { get; set; } }
public sealed class QuietUpdateManifest
{
    public int Schema { get; set; } public string Product { get; set; } = ""; public string AppVersion { get; set; } = ""; public string Platform { get; set; } = "";
    public string AssetUrl { get; set; } = ""; public string Sha256 { get; set; } = ""; public string ProvenanceUrl { get; set; } = ""; public string? Signature { get; set; }
}
