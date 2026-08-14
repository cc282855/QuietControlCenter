namespace Xuantong.AuthHost;

internal static class OwnedUdfStore
{
    private static readonly string Root = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Xuantong", "AuthUdf");

    public static string Create(AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        Directory.CreateDirectory(Root);
        return Path.Combine(Root, $"session-{context.SubscriptionHash}-{context.Key[..12]}-{Guid.NewGuid():N}");
    }

    public static bool CleanupAllOwned() => DeleteMatching(Root, "session-*");
    public static bool Clear(AuthTicket ticket)
    {
        var context = AuthContext.Create(ticket.SubId, ticket.Origin)!;
        return DeleteMatching(Root, $"session-{context.SubscriptionHash}-*");
    }

    public static bool DeleteOwnedPath(string path)
    {
        try
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(Root) + Path.DirectorySeparatorChar;
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(full).StartsWith("session-", StringComparison.Ordinal)) Directory.Delete(full, true);
            return !Directory.Exists(full);
        }
        catch { return false; }
    }

    internal static bool DeleteMatching(string root, string pattern)
    {
        try
        {
            if (!Directory.Exists(root)) return true;
            var paths = Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly).ToArray();
            var success = true;
            foreach (var path in paths)
            {
                try { Directory.Delete(path, true); }
                catch { success = false; }
            }
            return success && !Directory.EnumerateDirectories(root, pattern, SearchOption.TopDirectoryOnly).Any();
        }
        catch { return false; }
    }
}
