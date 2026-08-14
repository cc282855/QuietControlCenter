using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ServiceLib.Services;
using Xuantong.AuthHost;

namespace v2rayN.Services;

internal sealed class AuthHostClient
{
    private const int Protocol = 1;
    private const int MaxMessageBytes = 64 * 1024;
    private static readonly TimeSpan InteractionTimeout = TimeSpan.FromMinutes(15);

    public Task<SubscriptionQuotaResult> LoginAndQueryAsync(
        string subId, string officialUrl, int socksPort, CancellationToken cancellationToken)
        => ExecuteAsync("login-query", subId, officialUrl, socksPort, cancellationToken);

    public Task<SubscriptionQuotaResult> ClearSessionAsync(
        string subId, string officialUrl, int socksPort, CancellationToken cancellationToken)
        => ExecuteAsync("clear", subId, officialUrl, socksPort, cancellationToken);

    public Task<SubscriptionQuotaResult> QuerySessionAsync(
        string subId, string officialUrl, int socksPort, CancellationToken cancellationToken)
        => ExecuteAsync("query-session", subId, officialUrl, socksPort, cancellationToken);

    private static async Task<SubscriptionQuotaResult> ExecuteAsync(
        string operation,
        string subId,
        string officialUrl,
        int socksPort,
        CancellationToken cancellationToken)
    {
        var normalized = SubscriptionOfficialUrlParser.Normalize(officialUrl);
        var origin = SubscriptionOfficialUrlParser.GetCanonicalOrigin(normalized);
        if (normalized is null || origin is null || subId.Length is <= 0 or > 128
            || (operation != "clear" && !AuthBounds.IsValidProxyPort(socksPort))
            || operation is not ("login-query" or "query-session" or "clear"))
        {
            return new(SubscriptionQuotaStatusCode.InvalidRequest);
        }

        var helper = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Xuantong.AuthHost.exe"));
        if (!helper.StartsWith(Path.GetFullPath(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(helper))
        {
            return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
        }

        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var pipeName = "xuantong-auth-" + Guid.NewGuid().ToString("N");
        var ticketRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Xuantong", "AuthTickets");
        Directory.CreateDirectory(ticketRoot);
        var ticketPath = Path.Combine(ticketRoot, "ticket-" + Guid.NewGuid().ToString("N") + ".bin");
        var context = AuthContext.Create(subId, origin)!;
        string helperHash;
        using (var helperStream = File.OpenRead(helper))
            helperHash = Convert.ToHexString(SHA256.HashData(helperStream)).ToLowerInvariant();
        var ticket = new AuthTicket(
            Protocol, nonce, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds(),
            operation, subId, normalized, origin, socksPort, pipeName, helperHash);

        OwnedProcess? owned = null;
        try
        {
            var ciphertext = ContextProtection.Protect(ticket, context);
            var ticketBytes = JsonSerializer.SerializeToUtf8Bytes(
                new ProtectedEnvelope(context.Version, context.SubscriptionHash, context.Origin, ciphertext));
            if (ticketBytes.Length > 32 * 1024)
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
            await using (var ticketFile = new FileStream(
                ticketPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                await ticketFile.WriteAsync(ticketBytes, cancellationToken);
                await ticketFile.FlushAsync(cancellationToken);
            }

            await using var pipe = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                4096, MaxMessageBytes);
            owned = SecureProcessLauncher.TryLaunchMedium(helper, ticketPath, pipeName);
            if (owned is null)
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(InteractionTimeout);
            var connectTask = pipe.WaitForConnectionAsync(linked.Token);
            var exitTask = owned.Process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connectTask, exitTask) != connectTask)
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
            await connectTask;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actualClientPid)
                || !AuthBounds.IsExpectedPeer(actualClientPid, owned.Pid))
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);

            var envelope = await ReadFrameAsync<AuthEnvelope>(pipe, linked.Token);
            if (envelope is null
                || envelope.Protocol != Protocol
                || envelope.HelperPid != owned.Pid
                || envelope.HelperStartTicks != owned.StartTicks
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(envelope.Nonce ?? string.Empty), Encoding.UTF8.GetBytes(nonce))
                || !TryMapResponse(envelope.Response, out var mapped))
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
            await WriteFrameAsync(pipe,
                new AuthAck(Protocol, nonce, operation, envelope.Response.Status), linked.Token);
            var commit = await ReadFrameAsync<AuthCommitEnvelope>(pipe, linked.Token);
            if (commit is null || commit.Protocol != Protocol
                || commit.Nonce.Length != nonce.Length
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(commit.Nonce), Encoding.UTF8.GetBytes(nonce))
                || commit.Status is not ("Committed" or "NotRequired")
                || (commit.Status == "Committed"
                    && (operation != "login-query"
                        || envelope.Response.Status is not ("Success" or "AuthenticatedUnsupported"))))
                return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
            return mapped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(SubscriptionQuotaStatusCode.Cancelled);
        }
        catch
        {
            return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
        }
        finally
        {
            owned?.TerminateIfOwned();
            owned?.Dispose();
            try { File.Delete(ticketPath); } catch { }
        }
    }

    private static async Task<T?> ReadFrameAsync<T>(Stream pipe, CancellationToken cancellationToken)
    {
        var prefix = new byte[4];
        await pipe.ReadExactlyAsync(prefix, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
        if (!AuthBounds.IsValidMessageLength(length)) return default;
        var payload = new byte[length];
        await pipe.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload);
    }

    private static async Task WriteFrameAsync<T>(Stream pipe, T value, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(value);
        if (!AuthBounds.IsValidMessageLength(payload.Length))
            throw new InvalidDataException("Authentication acknowledgement exceeds the protocol limit.");
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await pipe.WriteAsync(prefix, cancellationToken);
        await pipe.WriteAsync(payload, cancellationToken);
        await pipe.FlushAsync(cancellationToken);
    }

    private static SubscriptionQuotaResult MapResponse(AuthResponse? response)
    {
        if (response is null) return new(SubscriptionQuotaStatusCode.AuthHostUnavailable);
        if (response.Status == "Success" && response.TotalBytes.HasValue
            && response.UploadBytes <= response.TotalBytes.Value
            && response.DownloadBytes <= response.TotalBytes.Value - response.UploadBytes
            && response.RemainingBytes == response.TotalBytes.Value - response.UploadBytes - response.DownloadBytes)
        {
            DateTimeOffset? expires = null;
            if (response.ExpiresUnixSeconds is > 0 and <= 4133980799)
                expires = DateTimeOffset.FromUnixTimeSeconds(response.ExpiresUnixSeconds.Value);
            return new(
                SubscriptionQuotaStatusCode.Success,
                new(response.UploadBytes, response.DownloadBytes, response.TotalBytes,
                    response.RemainingBytes, expires, DateTimeOffset.UtcNow,
                    SubscriptionQuotaSource.OfficialWebsite));
        }
        return new(response.Status switch
        {
            "LoginRequired" => SubscriptionQuotaStatusCode.LoginRequired,
            "AuthenticatedUnsupported" => SubscriptionQuotaStatusCode.AuthenticatedUnsupported,
            "WebView2RuntimeMissing" => SubscriptionQuotaStatusCode.WebView2RuntimeMissing,
            "ProxyUnavailable" => SubscriptionQuotaStatusCode.ProxyUnavailable,
            "NetworkError" => SubscriptionQuotaStatusCode.NetworkError,
            "BodyTooLarge" => SubscriptionQuotaStatusCode.BodyTooLarge,
            "HttpError" => SubscriptionQuotaStatusCode.HttpError,
            "Cancelled" => SubscriptionQuotaStatusCode.Cancelled,
            "Cleared" => SubscriptionQuotaStatusCode.SessionCleared,
            "ClearFailed" => SubscriptionQuotaStatusCode.SessionClearFailed,
            _ => SubscriptionQuotaStatusCode.AuthHostUnavailable
        });
    }

    private static bool TryMapResponse(AuthResponse? response, out SubscriptionQuotaResult result)
    {
        result = MapResponse(response);
        if (response is null) return false;
        if (response.Status == "Success") return result.IsSuccess;
        return response.Status is "LoginRequired" or "AuthenticatedUnsupported"
                   or "WebView2RuntimeMissing" or "AuthHostUnavailable"
                   or "ProxyUnavailable" or "NetworkError" or "BodyTooLarge" or "HttpError"
                   or "Cancelled" or "Cleared" or "ClearFailed" or "CleanupFailed"
               && response.UploadBytes == 0 && response.DownloadBytes == 0
               && response.TotalBytes is null && response.RemainingBytes == 0
               && response.ExpiresUnixSeconds is null;
    }

    private sealed record AuthTicket(
        int Protocol, string Nonce, int ParentPid, long ParentStartTicks, long ExpiresUnixSeconds,
        string Operation, string SubId, string OfficialUrl, string Origin,
        int SocksPort, string PipeName, string HelperExecutableSha256);
    private sealed record AuthEnvelope(int Protocol, string Nonce, int HelperPid, long HelperStartTicks, AuthResponse Response);
    private sealed record AuthAck(int Protocol, string Nonce, string Operation, string ResponseStatus);
    private sealed record AuthCommitEnvelope(int Protocol, string Nonce, string Status);
    private sealed record AuthResponse(
        string Status, ulong UploadBytes, ulong DownloadBytes, ulong? TotalBytes,
        ulong RemainingBytes, long? ExpiresUnixSeconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);
}

internal static class SecureProcessLauncher
{
    public static OwnedProcess? TryLaunchMedium(string executable, string ticketPath, string pipeName)
    {
        if (!OperatingSystem.IsWindows() || Process.GetCurrentProcess().SessionId == 0) return null;
        var currentIntegrity = GetCurrentIntegrityRid();
        if (currentIntegrity is >= 0x2000 and < 0x3000)
        {
            try
            {
                var process = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = false,
                    WorkingDirectory = AppContext.BaseDirectory,
                    ArgumentList = { "--ticket", ticketPath, "--pipe", pipeName }
                });
                return process is null ? null : OwnedProcess.TryCreate(process, executable);
            }
            catch { return null; }
        }
        if (currentIntegrity is not (>= 0x3000 and < 0x5000)) return null;
        return TryLaunchFromInteractiveShell(executable, ticketPath, pipeName);
    }

    private static OwnedProcess? TryLaunchFromInteractiveShell(string executable, string ticketPath, string pipeName)
    {
        var expectedSid = WindowsIdentity.GetCurrent().User;
        if (expectedSid is null) return null;
        foreach (var explorer in Process.GetProcessesByName("explorer")
                     .Where(p => p.SessionId == Process.GetCurrentProcess().SessionId))
        {
            try
            {
                if (!OpenProcessToken(explorer.Handle, 0x0001 | 0x0002 | 0x0008, out var token)) continue;
                try
                {
                    using var identity = new WindowsIdentity(token);
                    if (!expectedSid.Equals(identity.User) || GetTokenIntegrityRid(token) is not (>= 0x2000 and < 0x3000))
                        continue;
                    var command = $"\"{executable}\" --ticket \"{ticketPath}\" --pipe \"{pipeName}\"";
                    var startup = new StartupInfo { cb = Marshal.SizeOf<StartupInfo>(), lpDesktop = "winsta0\\default" };
                    if (!CreateProcessWithTokenW(token, 1, executable, command, 0x00000400,
                            IntPtr.Zero, AppContext.BaseDirectory, ref startup, out var info)) continue;
                    try
                    {
                        var process = Process.GetProcessById(unchecked((int)info.dwProcessId));
                        return OwnedProcess.TryCreate(process, executable);
                    }
                    finally { CloseHandle(info.hThread); CloseHandle(info.hProcess); }
                }
                finally { CloseHandle(token); }
            }
            catch { }
            finally { explorer.Dispose(); }
        }
        return null;
    }

    private static int GetCurrentIntegrityRid()
    {
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0008, out var token)) return -1;
        try { return GetTokenIntegrityRid(token); }
        finally { CloseHandle(token); }
    }

    private static int GetTokenIntegrityRid(IntPtr token)
    {
        GetTokenInformation(token, 25, IntPtr.Zero, 0, out var length);
        if (length <= 0) return -1;
        var buffer = Marshal.AllocHGlobal(length);
        try
        {
            if (!GetTokenInformation(token, 25, buffer, length, out _)) return -1;
            var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
            var sid = new SecurityIdentifier(label.Label.Sid);
            var bytes = new byte[sid.BinaryLength];
            sid.GetBinaryForm(bytes, 0);
            var count = bytes[1];
            return count == 0 ? -1 : unchecked((int)BitConverter.ToUInt32(bytes, 8 + (count - 1) * 4));
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenMandatoryLabel { public SidAndAttributes Label; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)] private struct StartupInfo
    {
        public int cb; public string? lpReserved; public string? lpDesktop; public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute;
        public int dwFlags; public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }
    [StructLayout(LayoutKind.Sequential)] private struct ProcessInformation
    {
        public IntPtr hProcess, hThread; public uint dwProcessId, dwThreadId;
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string applicationName,
        string commandLine, uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

internal sealed class OwnedProcess : IDisposable
{
    private static readonly ConcurrentDictionary<int, OwnedProcess> Active = new();
    static OwnedProcess() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var process in Active.Values) process.TerminateIfOwned();
    };

    private readonly string _path;
    internal Process Process { get; }
    internal int Pid { get; }
    internal long StartTicks { get; }

    private OwnedProcess(Process process, string path)
    {
        Process = process; Pid = process.Id; StartTicks = process.StartTime.ToUniversalTime().Ticks; _path = Path.GetFullPath(path);
        Active[Pid] = this;
    }

    internal static OwnedProcess? TryCreate(Process process, string expectedPath)
    {
        try
        {
            var actual = process.MainModule?.FileName;
            if (actual is null || !string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expectedPath), StringComparison.OrdinalIgnoreCase))
            { process.Dispose(); return null; }
            return new(process, expectedPath);
        }
        catch { process.Dispose(); return null; }
    }

    internal void TerminateIfOwned()
    {
        try
        {
            Process.Refresh();
            var actualPath = Process.MainModule?.FileName;
            if (!Process.HasExited && AuthBounds.IsOwnedProcess(Process.Id, Process.StartTime.ToUniversalTime().Ticks,
                    actualPath, Pid, StartTicks, _path)) Process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public void Dispose() { Active.TryRemove(Pid, out _); Process.Dispose(); }
}
