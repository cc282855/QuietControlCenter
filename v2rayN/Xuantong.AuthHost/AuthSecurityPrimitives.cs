using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Xuantong.AuthHost;

internal sealed record AuthContext(int Version, string SubscriptionHash, string Origin, byte[] Entropy, string Key)
{
    public static AuthContext? Create(string? subId, string? origin)
    {
        if (string.IsNullOrWhiteSpace(subId) || subId.Length > 128 || UriPolicy.Origin(origin) is not { } canonical)
            return null;
        var subHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subId))).ToLowerInvariant();
        return FromIdentity(subHash, canonical);
    }

    public static AuthContext? FromIdentity(string? subHash, string? origin)
    {
        if (subHash is null || subHash.Length != 64 || subHash.Any(c => !Uri.IsHexDigit(c))
            || UriPolicy.Origin(origin) is not { } canonical) return null;
        subHash = subHash.ToLowerInvariant();
        var material = Encoding.UTF8.GetBytes($"xuantong-auth\0v1\0{subHash}\0{canonical}");
        var entropy = SHA256.HashData(material);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(subHash + "\n" + canonical))).ToLowerInvariant();
        return new(1, subHash, canonical, entropy, key);
    }
}

internal sealed record ProtectedEnvelope(int Version, string SubscriptionHash, string Origin, byte[] Ciphertext);

internal static class ContextProtection
{
    public static byte[] Protect<T>(T value, AuthContext context)
    {
        var clear = JsonSerializer.SerializeToUtf8Bytes(value);
        try { return ProtectedData.Protect(clear, context.Entropy, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public static bool TryUnprotect<T>(ProtectedEnvelope envelope, AuthContext expected, out T? value)
    {
        value = default;
        if (envelope.Version != expected.Version
            || !string.Equals(envelope.SubscriptionHash, expected.SubscriptionHash, StringComparison.Ordinal)
            || !string.Equals(envelope.Origin, expected.Origin, StringComparison.Ordinal)
            || envelope.Ciphertext is not { Length: > 0 and <= 64 * 1024 }) return false;
        try
        {
            var clear = ProtectedData.Unprotect(envelope.Ciphertext, expected.Entropy, DataProtectionScope.CurrentUser);
            try { value = JsonSerializer.Deserialize<T>(clear); return value is not null; }
            finally { CryptographicOperations.ZeroMemory(clear); }
        }
        catch { return false; }
    }
}

internal sealed class ReplayValidator(int capacity = 128)
{
    private readonly int _capacity = Math.Clamp(capacity, 1, 1024);
    private readonly HashSet<string> _used = new(StringComparer.Ordinal);
    public bool TryUse(string? nonce)
    {
        if (nonce is null || nonce.Length is < 40 or > 64 || _used.Count >= _capacity) return false;
        return _used.Add(nonce);
    }
}

internal static class AuthBounds
{
    public static bool IsValidMessageLength(int length) => length is > 0 and <= 64 * 1024;
    public static bool IsValidProxyPort(int port) => port is > 0 and <= 65535;
    public static bool IsExpectedPeer(uint actualPid, int expectedPid) => actualPid == unchecked((uint)expectedPid) && expectedPid > 0;
    public static bool IsOwnedProcess(int actualPid, long actualStartTicks, string? actualPath,
        int expectedPid, long expectedStartTicks, string expectedPath)
        => actualPid == expectedPid && actualStartTicks == expectedStartTicks
           && string.Equals(Path.GetFullPath(actualPath ?? string.Empty), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase);
}
