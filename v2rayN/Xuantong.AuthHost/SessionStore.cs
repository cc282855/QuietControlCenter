using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Xuantong.AuthHost;

internal sealed record StoredCookie(
    string Name, string Value, string Domain, string Path,
    bool Secure, bool HttpOnly, int SameSite, long? ExpiresUnixSeconds);

internal sealed record SessionRecord(
    int Version, string SubscriptionHash, string Origin, List<StoredCookie> Cookies);

internal static class CookiePolicy
{
    public static bool IsApplicableDomain(string? cookieDomain, string officialHost)
    {
        if (string.IsNullOrWhiteSpace(cookieDomain)) return false;
        var domain = cookieDomain.TrimEnd('.').ToLowerInvariant();
        var host = officialHost.TrimEnd('.').ToLowerInvariant();
        return string.Equals(domain.TrimStart('.'), host, StringComparison.Ordinal);
    }

    public static bool IsAcceptable(StoredCookie cookie, string officialHost, DateTimeOffset now)
        => cookie.Secure && IsApplicableDomain(cookie.Domain, officialHost)
           && cookie.Name.Length is > 0 and <= 256 && cookie.Value.Length <= 4096
           && cookie.Path.Length is > 0 and <= 1024 && cookie.Path.StartsWith('/')
           && cookie.SameSite is >= 0 and <= 2
           && (!cookie.ExpiresUnixSeconds.HasValue || cookie.ExpiresUnixSeconds > now.ToUnixTimeSeconds());
}

internal static class SessionStore
{
    private const int MaxStoreBytes = 64 * 1024;
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xuantong", "AuthSessions");

    public static AuthResponse Clear(AuthTicket ticket)
    {
        try
        {
            var sessionsCleared = ClearSavedSessions(ticket);
            var profilesCleared = OwnedUdfStore.Clear(ticket);
            return new(sessionsCleared && profilesCleared ? "Cleared" : "ClearFailed");
        }
        catch { return new("AuthHostUnavailable"); }
    }

    internal static bool ClearSavedSessions(AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        return ClearFilesForSubscription(Root, context.SubscriptionHash);
    }

    public static async Task LoadAsync(CoreWebView2CookieManager manager, AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        var path = GetPath(context);
        if (!File.Exists(path)) return;
        try
        {
            var bytes = await File.ReadAllBytesAsync(path);
            if (bytes.Length is <= 0 or > MaxStoreBytes) return;
            var envelope = JsonSerializer.Deserialize<ProtectedEnvelope>(bytes);
            if (envelope is null || !ContextProtection.TryUnprotect(envelope, context, out SessionRecord? record)
                || record is null || record.Version != context.Version
                || record.SubscriptionHash != context.SubscriptionHash || record.Origin != context.Origin) return;
            var host = new Uri(context.Origin).IdnHost;
            foreach (var stored in record.Cookies.Take(64).Where(c => CookiePolicy.IsAcceptable(c, host, DateTimeOffset.UtcNow)))
            {
                var cookie = manager.CreateCookie(stored.Name, stored.Value, stored.Domain, stored.Path);
                cookie.IsSecure = true;
                cookie.IsHttpOnly = stored.HttpOnly;
                cookie.SameSite = (CoreWebView2CookieSameSiteKind)stored.SameSite;
                if (stored.ExpiresUnixSeconds.HasValue)
                    cookie.Expires = DateTimeOffset.FromUnixTimeSeconds(stored.ExpiresUnixSeconds.Value).UtcDateTime;
                manager.AddOrUpdateCookie(cookie);
            }
        }
        catch { }
    }

    public static async Task<PendingSession?> PrepareAsync(CoreWebView2CookieManager manager, AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        var host = new Uri(context.Origin).IdnHost;
        var now = DateTimeOffset.UtcNow;
        var source = await manager.GetCookiesAsync(ticket.Origin);
        var cookies = source.Take(128).Select(cookie => new StoredCookie(
                cookie.Name, cookie.Value, cookie.Domain, cookie.Path, cookie.IsSecure, cookie.IsHttpOnly,
                (int)cookie.SameSite, cookie.IsSession ? null : new DateTimeOffset(cookie.Expires.ToUniversalTime()).ToUnixTimeSeconds()))
            .Where(cookie => CookiePolicy.IsAcceptable(cookie, host, now)).Take(64).ToList();
        if (cookies.Count == 0) return null;
        var record = new SessionRecord(context.Version, context.SubscriptionHash, context.Origin, cookies);
        var ciphertext = ContextProtection.Protect(record, context);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            new ProtectedEnvelope(context.Version, context.SubscriptionHash, context.Origin, ciphertext));
        if (bytes.Length > MaxStoreBytes) throw new InvalidDataException("Protected session exceeds the storage limit.");
        var path = GetPath(context);
        byte[]? previous = null;
        if (File.Exists(path))
        {
            var info = new FileInfo(path);
            if (info.Length is <= 0 or > MaxStoreBytes)
                throw new InvalidDataException("Existing session cannot be rolled back safely.");
            previous = await File.ReadAllBytesAsync(path);
        }
        return new PendingSession(path, bytes, previous);
    }

    public static bool CleanupPending()
    {
        try
        {
            if (!Directory.Exists(Root)) return true;
            foreach (var path in Directory.EnumerateFiles(Root, "*.pending", SearchOption.TopDirectoryOnly)
                         .Concat(Directory.EnumerateFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly)))
                File.Delete(path);
            return !Directory.EnumerateFiles(Root, "*.pending", SearchOption.TopDirectoryOnly).Any()
                   && !Directory.EnumerateFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly).Any();
        }
        catch { return false; }
    }

    public static async Task<string?> GetExactHostCookieHeaderAsync(AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        try
        {
            var bytes = await File.ReadAllBytesAsync(GetPath(context));
            if (bytes.Length is <= 0 or > MaxStoreBytes) return null;
            var envelope = JsonSerializer.Deserialize<ProtectedEnvelope>(bytes);
            if (envelope is null || !ContextProtection.TryUnprotect(envelope, context, out SessionRecord? record)
                || record is null || record.Version != context.Version || record.SubscriptionHash != context.SubscriptionHash
                || record.Origin != context.Origin) return null;
            var host = new Uri(context.Origin).IdnHost;
            var cookies = record.Cookies.Where(c => CookiePolicy.IsAcceptable(c, host, DateTimeOffset.UtcNow)).Take(64).ToArray();
            return cookies.Length == 0 ? null : string.Join("; ", cookies.Select(c => c.Name + "=" + c.Value));
        }
        catch { return null; }
    }

    internal static string GetPath(AuthContext context)
        => Path.Combine(Root, context.SubscriptionHash + "-" + context.Key + ".bin");
    internal static bool DeleteCurrentContext(AuthTicket ticket)
    {
        try
        {
            var path = GetPath(AuthContext.Create(ticket.SubId, ticket.Origin)!);
            if (!Directory.Exists(Root)) return true;
            if (File.Exists(path)) File.Delete(path);
            foreach (var temp in Directory.EnumerateFiles(Root, Path.GetFileName(path) + ".*.pending")) File.Delete(temp);
            return !File.Exists(path) && !Directory.EnumerateFiles(Root, Path.GetFileName(path) + ".*.pending").Any();
        }
        catch { return false; }
    }

    internal static bool ClearFilesForSubscription(string root, string subscriptionHash)
    {
        try
        {
            Directory.CreateDirectory(root);
            var prefix = subscriptionHash + "-";
            foreach (var path in Directory.EnumerateFiles(root, prefix + "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(path);
                if (name.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            return !Directory.EnumerateFiles(root, prefix + "*", SearchOption.TopDirectoryOnly)
                .Any(path => path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".pending", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    internal sealed class PendingSession
    {
        private readonly string _path;
        private byte[]? _bytes;
        private byte[]? _previous;
        private bool _committed;
        private bool _finished;

        internal PendingSession(string path, byte[] bytes, byte[]? previous)
        {
            _path = path;
            _bytes = bytes;
            _previous = previous;
        }

        public async Task<bool> CommitAsync()
        {
            if (_finished || _bytes is null) return false;
            var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".pending";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                await using (var stream = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 4096, FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(_bytes);
                    await stream.FlushAsync();
                }
                File.Move(temporary, _path, true);
                _committed = true;
                return File.Exists(_path) && !File.Exists(temporary);
            }
            catch
            {
                try { File.Delete(temporary); } catch { }
                return false;
            }
        }

        public bool Rollback()
        {
            if (_finished) return false;
            try
            {
                if (_committed)
                {
                    if (_previous is null)
                    {
                        if (File.Exists(_path)) File.Delete(_path);
                    }
                    else
                    {
                        var restore = _path + "." + Guid.NewGuid().ToString("N") + ".pending";
                        File.WriteAllBytes(restore, _previous);
                        File.Move(restore, _path, true);
                    }
                }
                return !_committed
                       || (_previous is null
                           ? !File.Exists(_path)
                           : File.Exists(_path)
                             && CryptographicOperations.FixedTimeEquals(File.ReadAllBytes(_path), _previous));
            }
            catch { return false; }
            finally { Finish(); }
        }

        public void FinalizeCommit() => Finish();

        private void Finish()
        {
            if (_bytes is not null) CryptographicOperations.ZeroMemory(_bytes);
            if (_previous is not null) CryptographicOperations.ZeroMemory(_previous);
            _bytes = null;
            _previous = null;
            _finished = true;
        }
    }
}
