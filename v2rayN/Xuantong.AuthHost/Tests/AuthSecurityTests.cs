using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace Xuantong.AuthHost.Tests;

public sealed class AuthSecurityTests
{
    [Fact]
    public void MediumIntegrityCheck_NeverThrowsForCurrentWindowsProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        var exception = Record.Exception(() => HostSecurity.IsMediumIntegrityInteractiveUser());
        Assert.Null(exception);
    }

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

    [Theory]
    [InlineData("expire=1")]
    [InlineData("expire=4133980800")]
    [InlineData("expire=18446744073709551615")]
    [InlineData("expire=18446744073709551616")]
    public void AuthenticatedQuotaHeader_RejectsOutOfRangeAndOverflowExpiry(string expiry)
    {
        Assert.False(AuthenticatedQuotaParser.TryParseHeader(
            $"upload=1; download=2; total=10; {expiry}", out _));
    }

    [Fact]
    public void AuthenticatedQuotaHeader_AllowsZeroOrBoundedExpiry()
    {
        Assert.True(AuthenticatedQuotaParser.TryParseHeader(
            "upload=1; download=2; total=10; expire=0", out var noExpiry));
        Assert.Null(noExpiry.ExpiresUnixSeconds);
        Assert.True(AuthenticatedQuotaParser.TryParseHeader(
            "upload=1; download=2; total=10; expire=4133980799", out var maximum));
        Assert.Equal(4133980799, maximum.ExpiresUnixSeconds);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "Success")]
    [InlineData(HttpStatusCode.Found, "LoginRequired")]
    [InlineData(HttpStatusCode.Unauthorized, "LoginRequired")]
    [InlineData(HttpStatusCode.Forbidden, "LoginRequired")]
    [InlineData(HttpStatusCode.NotFound, "HttpError")]
    [InlineData(HttpStatusCode.InternalServerError, "HttpError")]
    public void AuthenticatedQuotaHttpStatus_IsClassifiedPrecisely(HttpStatusCode status, string expected)
        => Assert.Equal(expected, AuthenticatedQuotaQuery.ClassifyHttpStatus(status));

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
            Assert.True(File.Exists(path + ".rollback"));
            Assert.Equal(new byte[] { 7, 8, 9 },
                await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.True(afterAck.Rollback());
            Assert.Equal(previous, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.Empty(Directory.EnumerateFiles(root, "*.pending"));
            Assert.Empty(Directory.EnumerateFiles(root, "*.rollback"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PendingSession_RollbackFailureLeavesJournalForAuthenticatedStartupRecovery()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "rollback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.bin");
        var previous = new byte[] { 1, 3, 5, 7 };
        try
        {
            await File.WriteAllBytesAsync(path, previous, TestContext.Current.CancellationToken);
            var pending = new SessionStore.PendingSession(path, [2, 4, 6, 8], previous.ToArray());
            Assert.True(await pending.CommitAsync());
            using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.False(pending.Rollback());
                Assert.True(File.Exists(path + ".rollback"));
            }

            Assert.True(SessionStore.RecoverRollbackJournals(root));
            Assert.Equal(previous, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(path + ".rollback"));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task PendingSession_LockPreventsOverlappingCommit()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "lock-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.bin");
        try
        {
            var first = new SessionStore.PendingSession(path, [1], null);
            var second = new SessionStore.PendingSession(path, [2], null);
            Assert.True(await first.CommitAsync());
            Assert.False(await second.CommitAsync());
            Assert.False(second.Rollback());
            Assert.True(first.Rollback());
            Assert.False(File.Exists(path));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task AuthPipe_InvalidAckIsRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pair = await OpenAuthenticatedPipeAsync(TestContext.Current.CancellationToken);
        await using var server = pair.Server;
        await using var client = pair.Client;
        var response = new AuthResponse("AuthenticatedUnsupported");
        var send = client.SendAndAwaitAckAsync(response, TestContext.Current.CancellationToken);
        _ = await ReadPipeFrameAsync<JsonElement>(server, TestContext.Current.CancellationToken);
        await WritePipeFrameAsync(server,
            new AuthAck(1, pair.Ticket.Nonce, "query-session", response.Status),
            TestContext.Current.CancellationToken);
        Assert.False(await send);
    }

    [Fact]
    public async Task AuthPipe_MissingAckTimesOut()
    {
        if (!OperatingSystem.IsWindows()) return;
        var pair = await OpenAuthenticatedPipeAsync(TestContext.Current.CancellationToken);
        await using var server = pair.Server;
        await using var client = pair.Client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var send = client.SendAndAwaitAckAsync(new("AuthenticatedUnsupported"), timeout.Token);
        _ = await ReadPipeFrameAsync<JsonElement>(server, TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMilliseconds(200));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await send);
    }

    [Fact]
    public async Task AuthPipe_CommitResultFailureRollsBackPersistedSession()
    {
        if (!OperatingSystem.IsWindows()) return;
        var root = Path.Combine(AppContext.BaseDirectory, "pipe-rollback-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "session.bin");
        var previous = new byte[] { 10, 20, 30 };
        try
        {
            await File.WriteAllBytesAsync(path, previous, TestContext.Current.CancellationToken);
            var pending = new SessionStore.PendingSession(path, [40, 50, 60], previous.ToArray());
            var pair = await OpenAuthenticatedPipeAsync(TestContext.Current.CancellationToken);
            await using var server = pair.Server;
            await using var client = pair.Client;
            var response = new AuthResponse("AuthenticatedUnsupported");
            var send = client.SendAndAwaitAckAsync(response, TestContext.Current.CancellationToken);
            _ = await ReadPipeFrameAsync<JsonElement>(server, TestContext.Current.CancellationToken);
            await WritePipeFrameAsync(server,
                new AuthAck(1, pair.Ticket.Nonce, pair.Ticket.Operation, response.Status),
                TestContext.Current.CancellationToken);
            Assert.True(await send);
            Assert.True(await pending.CommitAsync());

            server.Disconnect();
            await Assert.ThrowsAnyAsync<Exception>(
                () => client.SendCommitResultAsync("Committed", TestContext.Current.CancellationToken));
            Assert.True(pending.Rollback());
            Assert.Equal(previous, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
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

    private static async Task<(NamedPipeServerStream Server, AuthPipeConnection Client, AuthTicket Ticket)>
        OpenAuthenticatedPipeAsync(CancellationToken cancellationToken)
    {
        var pipeName = "xuantong-auth-test-" + Guid.NewGuid().ToString("N");
        var process = Process.GetCurrentProcess();
        var ticket = new AuthTicket(
            1, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)), process.Id,
            process.StartTime.ToUniversalTime().Ticks, DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds(),
            "login-query", "sub", "https://portal.example/", "https://portal.example",
            10808, pipeName, new string('a', 64));
        var server = new NamedPipeServerStream(
            pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var rawClient = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        var connect = rawClient.ConnectAsync(cancellationToken);
        await server.WaitForConnectionAsync(cancellationToken);
        await connect;
        var client = AuthPipeConnection.CreateForTests(rawClient, ticket);
        return (server, client, ticket);
    }

    private static async Task<T?> ReadPipeFrameAsync<T>(Stream stream, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        Assert.True(AuthBounds.IsValidMessageLength(length));
        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload);
    }

    private static async Task WritePipeFrameAsync<T>(
        Stream stream, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await stream.WriteAsync(prefix, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
