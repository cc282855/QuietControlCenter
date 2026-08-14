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
    private const string RollbackSuffix = ".rollback";
    private const string CompleteSuffix = ".complete";
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
            if (!RecoverRollbackJournals(Root)) return false;
            foreach (var path in Directory.EnumerateFiles(Root, "*.pending", SearchOption.TopDirectoryOnly)
                         .Concat(Directory.EnumerateFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly))
                         .Concat(Directory.EnumerateFiles(Root, "*" + CompleteSuffix, SearchOption.TopDirectoryOnly))
                         .Concat(Directory.EnumerateFiles(Root, "*.lock", SearchOption.TopDirectoryOnly)))
                File.Delete(path);
            return !Directory.EnumerateFiles(Root, "*.pending", SearchOption.TopDirectoryOnly).Any()
                   && !Directory.EnumerateFiles(Root, "*.tmp", SearchOption.TopDirectoryOnly).Any()
                   && !Directory.EnumerateFiles(Root, "*" + RollbackSuffix, SearchOption.TopDirectoryOnly).Any()
                   && !Directory.EnumerateFiles(Root, "*" + CompleteSuffix, SearchOption.TopDirectoryOnly).Any();
        }
        catch { return false; }
    }

    internal static bool RecoverRollbackJournals(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return true;
            var journals = Directory.EnumerateFiles(root, "*" + RollbackSuffix, SearchOption.TopDirectoryOnly).ToArray();
            var success = true;
            foreach (var journal in journals)
            {
                var path = journal[..^RollbackSuffix.Length];
                try
                {
                    using var sessionLock = AcquireSessionLock(path);
                    if (!RestoreFromJournal(path, journal)) success = false;
                }
                catch { success = false; }
            }
            return success && !Directory.EnumerateFiles(root, "*" + RollbackSuffix, SearchOption.TopDirectoryOnly).Any();
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
            foreach (var suffix in new[] { ".*.pending", RollbackSuffix, CompleteSuffix, ".lock" })
                foreach (var extra in Directory.EnumerateFiles(Root, Path.GetFileName(path) + suffix)) File.Delete(extra);
            return !File.Exists(path)
                   && !Directory.EnumerateFiles(Root, Path.GetFileName(path) + ".*").Any();
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
                    || name.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(RollbackSuffix, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(CompleteSuffix, StringComparison.OrdinalIgnoreCase)
                    || name.EndsWith(".lock", StringComparison.OrdinalIgnoreCase)) File.Delete(path);
            }
            return !Directory.EnumerateFiles(root, prefix + "*", SearchOption.TopDirectoryOnly)
                .Any(path => path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".pending", StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(RollbackSuffix, StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(CompleteSuffix, StringComparison.OrdinalIgnoreCase)
                             || path.EndsWith(".lock", StringComparison.OrdinalIgnoreCase));
        }
        catch { return false; }
    }

    private static FileStream AcquireSessionLock(string path)
        => new(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None,
            1, FileOptions.DeleteOnClose | FileOptions.WriteThrough);

    private static bool RestoreFromJournal(string path, string journal)
    {
        try
        {
            var info = new FileInfo(journal);
            if (info.Length < 0 || info.Length > MaxStoreBytes) return false;
            if (info.Length == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                if (File.Exists(path)) return false;
            }
            else
            {
                var previous = File.ReadAllBytes(journal);
                var restore = path + "." + Guid.NewGuid().ToString("N") + ".pending";
                WriteThrough(restore, previous);
                File.Move(restore, path, true);
                if (!File.Exists(path)
                    || !CryptographicOperations.FixedTimeEquals(File.ReadAllBytes(path), previous)) return false;
            }
            File.Delete(journal);
            return !File.Exists(journal);
        }
        catch { return false; }
    }

    private static void WriteThrough(string path, ReadOnlySpan<byte> bytes)
    {
        using var stream = new FileStream(
            path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        stream.Write(bytes);
        stream.Flush(true);
    }

    internal sealed class PendingSession
    {
        private readonly string _path;
        private readonly string _journalPath;
        private byte[]? _bytes;
        private byte[]? _previous;
        private FileStream? _sessionLock;
        private bool _finished;

        internal PendingSession(string path, byte[] bytes, byte[]? previous)
        {
            _path = path;
            _journalPath = path + RollbackSuffix;
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
                _sessionLock = AcquireSessionLock(_path);
                if (File.Exists(_journalPath)) return false;
                if (_previous is null)
                {
                    using var marker = new FileStream(
                        _journalPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                        1, FileOptions.WriteThrough);
                    marker.Flush(true);
                }
                else
                {
                    var journalTemporary = _journalPath + "." + Guid.NewGuid().ToString("N") + ".pending";
                    WriteThrough(journalTemporary, _previous);
                    File.Move(journalTemporary, _journalPath, true);
                }
                await using (var stream = new FileStream(
                                 temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                 4096, FileOptions.WriteThrough))
                {
                    await stream.WriteAsync(_bytes);
                    await stream.FlushAsync();
                }
                File.Move(temporary, _path, true);
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
            var success = true;
            try
            {
                if (File.Exists(_journalPath))
                    success = _sessionLock is not null && RestoreFromJournal(_path, _journalPath);
                return success;
            }
            catch { return false; }
            finally { Finish(); }
        }

        public bool FinalizeCommit()
        {
            if (_finished) return false;
            var acknowledged = _path + CompleteSuffix;
            try
            {
                if (File.Exists(_journalPath)) File.Move(_journalPath, acknowledged, true);
                try { File.Delete(acknowledged); } catch { }
                return !File.Exists(_journalPath);
            }
            catch { return false; }
            finally { Finish(); }
        }

        private void Finish()
        {
            if (_bytes is not null) CryptographicOperations.ZeroMemory(_bytes);
            if (_previous is not null) CryptographicOperations.ZeroMemory(_previous);
            _bytes = null;
            _previous = null;
            _sessionLock?.Dispose();
            _sessionLock = null;
            _finished = true;
        }
    }
}
