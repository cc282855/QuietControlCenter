using System.Buffers.Binary;
using System.IO.Pipes;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Xuantong.AuthHost;

internal sealed record AuthTicket(
    int Protocol,
    string Nonce,
    int ParentPid,
    long ParentStartTicks,
    long ExpiresUnixSeconds,
    string Operation,
    string SubId,
    string OfficialUrl,
    string Origin,
    int SocksPort,
    string PipeName,
    string HelperExecutableSha256)
{
    private const int MaxTicketBytes = 32 * 1024;
    private static readonly ReplayValidator Replays = new();

    public static bool TryConsume(string[] args, out AuthTicket ticket)
    {
        ticket = null!;
        var ticketIndex = Array.IndexOf(args, "--ticket");
        var pipeIndex = Array.IndexOf(args, "--pipe");
        if (ticketIndex < 0 || ticketIndex + 1 >= args.Length
            || pipeIndex < 0 || pipeIndex + 1 >= args.Length)
        {
            return false;
        }
        var path = Path.GetFullPath(args[ticketIndex + 1]);
        var expectedRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Xuantong", "AuthTickets") + Path.DirectorySeparatorChar;
        if (!path.StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
        {
            return false;
        }

        try
        {
            byte[] envelopeBytes;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None,
                       4096, FileOptions.DeleteOnClose))
            {
                if (stream.Length is <= 0 or > MaxTicketBytes) return false;
                envelopeBytes = new byte[stream.Length];
                stream.ReadExactly(envelopeBytes);
            }
            var envelope = JsonSerializer.Deserialize<ProtectedEnvelope>(envelopeBytes);
            var context = envelope is null ? null : AuthContext.FromIdentity(envelope.SubscriptionHash, envelope.Origin);
            if (envelope is null || context is null
                || !ContextProtection.TryUnprotect(envelope, context, out AuthTicket? decoded)) return false;
            ticket = decoded!;
            var innerContext = AuthContext.Create(ticket.SubId, ticket.Origin);
            return ticket is not null
                   && ticket.Protocol == 1
                   && innerContext is not null
                   && innerContext.SubscriptionHash == context.SubscriptionHash
                   && ticket.Nonce.Length is >= 40 and <= 64
                   && Replays.TryUse(ticket.Nonce)
                   && ticket.ParentPid > 0
                   && HostSecurity.IsExpectedParent(ticket.ParentPid, ticket.ParentStartTicks)
                   && ticket.ExpiresUnixSeconds >= DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                   && ticket.ExpiresUnixSeconds <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()
                   && ticket.Operation is "login-query" or "query-session" or "clear"
                   && ticket.SubId.Length is > 0 and <= 128
                   && (ticket.Operation == "clear" || AuthBounds.IsValidProxyPort(ticket.SocksPort))
                   && ticket.PipeName == args[pipeIndex + 1]
                   && ticket.PipeName.StartsWith("xuantong-auth-", StringComparison.Ordinal)
                   && UriPolicy.IsExactOrigin(ticket.OfficialUrl, ticket.Origin)
                   && IsCurrentExecutable(ticket.HelperExecutableSha256);
        }
        catch
        {
            try { File.Delete(path); } catch { }
            ticket = null!;
            return false;
        }
    }

    private static bool IsCurrentExecutable(string expectedHash)
    {
        try
        {
            var path = Environment.ProcessPath;
            if (path is null || expectedHash.Length != 64) return false;
            using var stream = File.OpenRead(path);
            var actual = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expectedHash));
        }
        catch { return false; }
    }
}

internal sealed record AuthResponse(
    string Status,
    ulong UploadBytes = 0,
    ulong DownloadBytes = 0,
    ulong? TotalBytes = null,
    ulong RemainingBytes = 0,
    long? ExpiresUnixSeconds = null);

internal sealed record AuthAck(int Protocol, string Nonce, string Operation, string ResponseStatus);
internal sealed record AuthCommitEnvelope(int Protocol, string Nonce, string Status);

internal sealed class CommitAckValidator(AuthTicket ticket, AuthResponse response)
{
    private int _used;

    public bool TryAccept(AuthAck? ack)
    {
        if (ack is null || ack.Protocol != 1 || ack.Operation != ticket.Operation
            || ack.ResponseStatus != response.Status || ack.Nonce.Length != ticket.Nonce.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(ack.Nonce), Encoding.UTF8.GetBytes(ticket.Nonce)))
            return false;
        return Interlocked.CompareExchange(ref _used, 1, 0) == 0;
    }
}

internal sealed class AuthPipeConnection : IAsyncDisposable
{
    private readonly NamedPipeClientStream _pipe;
    private readonly AuthTicket _ticket;
    private AuthPipeConnection(NamedPipeClientStream pipe, AuthTicket ticket) { _pipe = pipe; _ticket = ticket; }

    public static async Task<AuthPipeConnection?> ConnectAsync(AuthTicket ticket, CancellationToken cancellationToken)
    {
        try
        {
            var pipe = new NamedPipeClientStream(
                ".", ticket.PipeName, PipeDirection.InOut,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(cancellationToken);
            if (!GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverPid)
                || !AuthBounds.IsExpectedPeer(serverPid, ticket.ParentPid))
            { await pipe.DisposeAsync(); return null; }
            return new(pipe, ticket);
        }
        catch { return null; }
    }

    public bool IsExpectedServerAlive()
        => _pipe.IsConnected && GetNamedPipeServerProcessId(_pipe.SafePipeHandle, out var serverPid)
           && AuthBounds.IsExpectedPeer(serverPid, _ticket.ParentPid)
           && HostSecurity.IsExpectedParent(_ticket.ParentPid, _ticket.ParentStartTicks);

    public async Task<bool> SendAndAwaitAckAsync(AuthResponse response, CancellationToken cancellationToken)
    {
        if (!IsExpectedServerAlive()) return false;
        var envelope = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Protocol = 1,
            Nonce = _ticket.Nonce,
            HelperPid = Environment.ProcessId,
            HelperStartTicks = Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            Response = response
        });
        if (!AuthBounds.IsValidMessageLength(envelope.Length)) return false;
        await WriteFrameAsync(envelope, cancellationToken);
        var ackBytes = await ReadFrameAsync(cancellationToken);
        if (ackBytes is null || !IsExpectedServerAlive()) return false;
        AuthAck? ack;
        try { ack = JsonSerializer.Deserialize<AuthAck>(ackBytes); }
        catch { return false; }
        return new CommitAckValidator(_ticket, response).TryAccept(ack);
    }

    public async Task SendCommitResultAsync(string status, CancellationToken cancellationToken)
    {
        if (!IsExpectedServerAlive()) throw new IOException("Authenticated pipe owner is unavailable.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new AuthCommitEnvelope(1, _ticket.Nonce, status));
        if (!AuthBounds.IsValidMessageLength(bytes.Length)) throw new InvalidDataException("Commit result is too large.");
        await WriteFrameAsync(bytes, cancellationToken);
    }

    private async Task WriteFrameAsync(byte[] payload, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await _pipe.WriteAsync(prefix, cancellationToken);
        await _pipe.WriteAsync(payload, cancellationToken);
        await _pipe.FlushAsync(cancellationToken);
    }

    private async Task<byte[]?> ReadFrameAsync(CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await _pipe.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (!AuthBounds.IsValidMessageLength(length)) return null;
        var payload = new byte[length];
        await _pipe.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    public ValueTask DisposeAsync() => _pipe.DisposeAsync();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeServerProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint serverProcessId);
}
