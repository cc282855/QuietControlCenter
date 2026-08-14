using System.Text;
using Xunit;

namespace Xuantong.AuthHost.Tests;

public sealed class AuthSecurityTests
{
    [Fact]
    public void ContextProtection_IsolatesSubscriptionAndOriginAndRejectsTamper()
    {
        if (!OperatingSystem.IsWindows()) return;
        var first = AuthContext.Create("sub-a", "https://portal.example/")!;
        var otherSub = AuthContext.Create("sub-b", "https://portal.example/")!;
        var otherOrigin = AuthContext.Create("sub-a", "https://other.example/")!;
        var record = new SessionRecord(1, first.SubscriptionHash, first.Origin, []);
        var cipher = ContextProtection.Protect(record, first);
        var envelope = new ProtectedEnvelope(1, first.SubscriptionHash, first.Origin, cipher);

        Assert.True(ContextProtection.TryUnprotect(envelope, first, out SessionRecord? decoded));
        Assert.Equal(first.SubscriptionHash, decoded!.SubscriptionHash);
        Assert.False(ContextProtection.TryUnprotect(envelope, otherSub, out SessionRecord? _));
        Assert.False(ContextProtection.TryUnprotect(envelope, otherOrigin, out SessionRecord? _));
        var tampered = cipher.ToArray(); tampered[tampered.Length / 2] ^= 0x40;
        Assert.False(ContextProtection.TryUnprotect(envelope with { Ciphertext = tampered }, first, out SessionRecord? _));
    }

    [Theory]
    [InlineData("portal.example.com", "portal.example.com", true)]
    [InlineData(".portal.example.com", "portal.example.com", true)]
    [InlineData(".example.com", "portal.example.com", false)]
    [InlineData(".ample.com", "portal.example.com", false)]
    [InlineData(".evil.example", "portal.example.com", false)]
    [InlineData(".com", "portal.example.com", false)]
    public void CookieDomainPolicy_EnforcesExactOfficialHost(string domain, string host, bool expected)
        => Assert.Equal(expected, CookiePolicy.IsApplicableDomain(domain, host));

    [Fact]
    public void CookiePolicy_RejectsExpiredInsecureAndOversizedValues()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.True(CookiePolicy.IsAcceptable(new("sid", "v", ".portal.example.com", "/", true, true, 1,
            now.AddHours(1).ToUnixTimeSeconds()), "portal.example.com", now));
        Assert.False(CookiePolicy.IsAcceptable(new("sid", "v", ".example.com", "/", false, true, 1, null), "portal.example.com", now));
        Assert.False(CookiePolicy.IsAcceptable(new("sid", "v", ".example.com", "/", true, true, 1,
            now.AddSeconds(-1).ToUnixTimeSeconds()), "portal.example.com", now));
        Assert.False(CookiePolicy.IsAcceptable(new("sid", new string('x', 4097), ".example.com", "/", true, true, 1, null), "portal.example.com", now));
    }

    [Fact]
    public void ProtocolValidators_RejectReplayBoundsWrongPeerAndWrongOwnedProcess()
    {
        var replay = new ReplayValidator();
        var nonce = Convert.ToBase64String(Encoding.UTF8.GetBytes(new string('n', 32)));
        Assert.True(replay.TryUse(nonce));
        Assert.False(replay.TryUse(nonce));
        Assert.False(AuthBounds.IsValidMessageLength(0));
        Assert.False(AuthBounds.IsValidMessageLength(64 * 1024 + 1));
        Assert.True(AuthBounds.IsExpectedPeer(42, 42));
        Assert.False(AuthBounds.IsExpectedPeer(41, 42));
        Assert.True(AuthBounds.IsValidProxyPort(10808));
        Assert.False(AuthBounds.IsValidProxyPort(0));
        Assert.True(AuthBounds.IsOwnedProcess(10, 20, "C:\\safe\\host.exe", 10, 20, "C:\\safe\\host.exe"));
        Assert.False(AuthBounds.IsOwnedProcess(10, 21, "C:\\safe\\host.exe", 10, 20, "C:\\safe\\host.exe"));
    }

    [Fact]
    public void CommitAck_RequiresBoundNonceOperationStatusAndSingleUse()
    {
        var nonce = Convert.ToBase64String(Enumerable.Repeat((byte)0x5A, 32).ToArray());
        var ticket = new AuthTicket(1, nonce, 1, 2, 3, "login-query", "sub", "https://portal.example/",
            "https://portal.example", 10808, "xuantong-auth-test", new string('a', 64));
        var response = new AuthResponse("AuthenticatedUnsupported");
        var validator = new CommitAckValidator(ticket, response);

        Assert.False(validator.TryAccept(null));
        Assert.False(validator.TryAccept(new(1, nonce, "query-session", response.Status)));
        Assert.False(validator.TryAccept(new(1, nonce, ticket.Operation, "Success")));
        Assert.False(validator.TryAccept(new(1, Convert.ToBase64String(Enumerable.Repeat((byte)0x6B, 32).ToArray()),
            ticket.Operation, response.Status)));
        var valid = new AuthAck(1, nonce, ticket.Operation, response.Status);
        Assert.True(validator.TryAccept(valid));
        Assert.False(validator.TryAccept(valid));
    }

    [Fact]
    public async Task PendingSession_IsInvisibleBeforeAckAndCanRollbackAfterCommitSendFailure()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "pending-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.bin");
        try
        {
            var beforeAck = new SessionStore.PendingSession(path, [1, 2, 3], null);
            Assert.False(File.Exists(path));
            Assert.True(beforeAck.Rollback());
            Assert.False(File.Exists(path));

            var previous = new byte[] { 4, 5, 6 };
            await File.WriteAllBytesAsync(path, previous, TestContext.Current.CancellationToken);
            var afterAck = new SessionStore.PendingSession(path, [7, 8, 9], previous.ToArray());
            Assert.True(await afterAck.CommitAsync());
            Assert.Equal(new byte[] { 7, 8, 9 },
                await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.True(afterAck.Rollback());
            Assert.Equal(previous, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(root, "*.pending"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OriginPolicy_FailsClosedForNonHttpsPrivateAndCrossOrigin()
    {
        Assert.False(UriPolicy.TryNormalize("http://portal.example/", out _));
        Assert.False(UriPolicy.TryNormalize("https://127.0.0.1/", out _));
        Assert.False(UriPolicy.TryNormalize("https://192.168.1.1/", out _));
        Assert.True(UriPolicy.IsExactOrigin("https://portal.example/a", "https://portal.example/"));
        Assert.False(UriPolicy.IsExactOrigin("https://idp.example/a", "https://portal.example/"));
    }

    [Fact]
    public void ClearFiles_RemovesOnlyMatchingFinalAndTemporaryRecords()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "clear-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = AuthContext.Create("sub-clear", "https://portal.example/")!;
            var changedOrigin = AuthContext.Create("sub-clear", "https://new.example/")!;
            var final = Path.Combine(root, context.SubscriptionHash + "-" + context.Key + ".bin");
            var changed = Path.Combine(root, changedOrigin.SubscriptionHash + "-" + changedOrigin.Key + ".bin");
            var temp = final + ".abc.tmp";
            var pending = final + ".abc.pending";
            var orphan = Path.Combine(root, context.SubscriptionHash + "-malformed.bin");
            var unrelated = Path.Combine(root, "unrelated.bin");
            File.WriteAllText(final, "x"); File.WriteAllText(changed, "x"); File.WriteAllText(temp, "x");
            File.WriteAllText(pending, "x");
            File.WriteAllText(orphan, "x"); File.WriteAllText(unrelated, "x");
            Assert.True(SessionStore.ClearFilesForSubscription(root, context.SubscriptionHash));
            Assert.False(File.Exists(final)); Assert.False(File.Exists(changed)); Assert.False(File.Exists(temp));
            Assert.False(File.Exists(pending));
            Assert.False(File.Exists(orphan)); Assert.True(File.Exists(unrelated));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void OwnedUdfDeletion_DoesNotReportSuccessWhileDataIsLocked()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(AppContext.BaseDirectory, "udf-test-" + Guid.NewGuid().ToString("N"));
        var owned = Path.Combine(root, "session-test-locked");
        Directory.CreateDirectory(owned);
        var file = Path.Combine(owned, "lock.bin");
        using (new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.False(OwnedUdfStore.DeleteMatching(root, "session-*"));
            Assert.True(Directory.Exists(owned));
        }
        Directory.Delete(root, true);
    }

    [Fact]
    public void BrowserArguments_ForceSocksAndDisableNonProxiedWebRtcUdp()
    {
        var arguments = BrowserSecurityOptions.BuildProxyArguments(10808);
        Assert.Contains("--proxy-server=\"socks5://127.0.0.1:10808\"", arguments);
        Assert.Contains("--proxy-bypass-list=\"<-loopback>\"", arguments);
        Assert.Contains("--force-webrtc-ip-handling-policy=disable_non_proxied_udp", arguments);
        Assert.Throws<ArgumentOutOfRangeException>(() => BrowserSecurityOptions.BuildProxyArguments(0));
    }
}
