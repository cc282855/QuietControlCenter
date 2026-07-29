using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace v2rayN.Services;

/// <summary>
/// Detection-only updater for the customized shell. Official v2rayN GUI
/// releases are never downloaded or installed by this service.
/// </summary>
public sealed partial class QuietUpdateService
{
    private const string LatestReleaseApi = "https://api.github.com/repos/2dust/v2rayN/releases/latest";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _stateDirectory;
    private readonly string _statePath;
    private readonly string _channelPath;

    public QuietUpdateService()
    {
        _stateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QuietControlCenter");
        _statePath = Path.Combine(_stateDirectory, "update-state.json");
        _channelPath = Path.Combine(_stateDirectory, "update-channel.json");
    }

    public async Task<IReadOnlyList<string>> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var notices = new List<string>();
        if (!await Gate.WaitAsync(0, cancellationToken))
        {
            return notices;
        }

        try
        {
            Directory.CreateDirectory(_stateDirectory);
            var state = await ReadJsonAsync<UpdateState>(_statePath, cancellationToken) ?? new();
            var now = DateTimeOffset.UtcNow;
            if (!ShouldCheck(state.LastCheckedUtc, now))
            {
                return notices;
            }

            // Persist before networking so offline starts cannot create a retry storm.
            state.LastCheckedUtc = now;
            await WriteJsonAsync(_statePath, state, cancellationToken);

            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("QuietControlCenter", "7.24.3"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var installedVersionIsValid = TryParseVersion(currentVersion, out var installedVersion);
            var latest = await ReadLatestTagAsync(client, cancellationToken);
            if (TryParseVersion(latest, out var latestVersion)
                && installedVersionIsValid
                && latestVersion > installedVersion
                && !string.Equals(state.LatestSeenTag, latest, StringComparison.OrdinalIgnoreCase))
            {
                notices.Add($"检测到官方 v2rayN {latest}。当前定制界面保持不变；请等待或安装兼容的 Quiet Control Center 完整包。");
                state.LatestSeenTag = latest;
                await WriteJsonAsync(_statePath, state, cancellationToken);
            }

            var channel = await ReadJsonAsync<QuietChannelConfig>(_channelPath, cancellationToken);
            if (channel is not null && Uri.TryCreate(channel.ManifestUrl, UriKind.Absolute, out var manifestUri)
                && manifestUri.Scheme == Uri.UriSchemeHttps)
            {
                var manifest = await ReadJsonFromUriAsync<QuietUpdateManifest>(client, manifestUri, cancellationToken);
                if (installedVersionIsValid
                    && IsValidManifest(manifest)
                    && TryParseVersion(manifest!.AppVersion, out var customVersion)
                    && customVersion > installedVersion)
                {
                    notices.Add($"Quiet Control Center {manifest.AppVersion} 已发布。已验证更新清单；请从定制发布通道手动安装完整包。");
                }
            }

            return notices;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or UnauthorizedAccessException)
        {
            // Update detection must never delay or break proxy startup.
            return notices;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static bool ShouldCheck(DateTimeOffset? lastCheckedUtc, DateTimeOffset nowUtc) =>
        lastCheckedUtc is null || nowUtc - lastCheckedUtc.Value >= CheckInterval;

    public static bool TryParseVersion(string? value, out Version version)
    {
        version = new Version();
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var match = VersionPattern().Match(value);
        return match.Success && Version.TryParse(match.Value, out version);
    }

    public static bool IsValidManifest(QuietUpdateManifest? manifest)
    {
        return manifest is not null
               && TryParseVersion(manifest.AppVersion, out _)
               && string.Equals(manifest.Platform, "win-x64", StringComparison.OrdinalIgnoreCase)
               && Uri.TryCreate(manifest.AssetUrl, UriKind.Absolute, out var assetUri)
               && assetUri.Scheme == Uri.UriSchemeHttps
               && Sha256Pattern().IsMatch(manifest.Sha256 ?? string.Empty)
               && Uri.TryCreate(manifest.ProvenanceUrl, UriKind.Absolute, out var provenanceUri)
               && provenanceUri.Scheme == Uri.UriSchemeHttps;
    }

    private static async Task<string?> ReadLatestTagAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var release = await ReadJsonFromUriAsync<GitHubRelease>(client, new Uri(LatestReleaseApi), cancellationToken);
        return release?.TagName;
    }

    private static async Task<T?> ReadJsonFromUriAsync<T>(HttpClient client, Uri uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<T?> ReadJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return default;
        }
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }

    private static async Task WriteJsonAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporaryPath = path + ".tmp";
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
        }
        File.Move(temporaryPath, path, true);
    }

    [GeneratedRegex(@"\d+(?:\.\d+){1,3}")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("^[A-Fa-f0-9]{64}$")]
    private static partial Regex Sha256Pattern();

    private sealed class GitHubRelease
    {
        public string? TagName { get; set; }
    }

    private sealed class UpdateState
    {
        public DateTimeOffset? LastCheckedUtc { get; set; }
        public string? LatestSeenTag { get; set; }
    }
}

public sealed class QuietChannelConfig
{
    public string? ManifestUrl { get; set; }
}

public sealed class QuietUpdateManifest
{
    public string? AppVersion { get; set; }
    public string? Platform { get; set; }
    public string? AssetUrl { get; set; }
    public string? Sha256 { get; set; }
    public string? Signature { get; set; }
    public string? ProvenanceUrl { get; set; }
}
