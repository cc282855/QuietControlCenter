using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ServiceLib.Common;
using ServiceLib.Models.Dto;
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
            return new(SubscriptionQuotaStatusCode.AuthHostHelperMissing);
        }

        var failureStatus = SubscriptionQuotaStatusCode.AuthHostStartFailed;
        var failureDiagnostic = SubscriptionQuotaDiagnosticCode.UnknownStartFailure;
        string? ticketPath = null;
        OwnedProcess? owned = null;
        try
        {
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.TicketDirectoryFailed;
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            var pipeName = "xuantong-auth-" + Guid.NewGuid().ToString("N");
            var ticketRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Xuantong", "AuthTickets");
            Directory.CreateDirectory(ticketRoot);
            ticketPath = Path.Combine(ticketRoot, "ticket-" + Guid.NewGuid().ToString("N") + ".bin");
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.TicketProtectionFailed;
            var context = AuthContext.Create(subId, origin)!;
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.HelperVerificationFailed;
            string helperHash;
            using (var helperStream = File.OpenRead(helper))
                helperHash = Convert.ToHexString(SHA256.HashData(helperStream)).ToLowerInvariant();
            var ticket = new AuthTicket(
                Protocol, nonce, Environment.ProcessId, Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,
                DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds(),
                operation, subId, normalized, origin, socksPort, pipeName, helperHash);
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.TicketProtectionFailed;
            var ciphertext = ContextProtection.Protect(ticket, context);
            var ticketBytes = JsonSerializer.SerializeToUtf8Bytes(
                new ProtectedEnvelope(context.Version, context.SubscriptionHash, context.Origin, ciphertext));
            if (ticketBytes.Length > 32 * 1024)
                return new(SubscriptionQuotaStatusCode.AuthHostStartFailed, null,
                    SubscriptionQuotaDiagnosticCode.TicketProtectionFailed);
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.TicketWriteFailed;
            await using (var ticketFile = new FileStream(
                ticketPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                4096, FileOptions.WriteThrough))
            {
                await ticketFile.WriteAsync(ticketBytes, cancellationToken);
                await ticketFile.FlushAsync(cancellationToken);
            }

            failureDiagnostic = SubscriptionQuotaDiagnosticCode.PipeCreationFailed;
            await using var pipe = CurrentUserMediumPipe.Create(pipeName, 4096, MaxMessageBytes);
            failureDiagnostic = SubscriptionQuotaDiagnosticCode.UnknownStartFailure;
            var launch = await SecureProcessLauncher.TryLaunchMediumAsync(
                helper, ticketPath, pipeName, cancellationToken);
            owned = launch.Process;
            if (owned is null)
                return new(SubscriptionQuotaStatusCode.AuthHostStartFailed, null, launch.Diagnostic,
                    launch.NativeErrorCode);
            failureStatus = SubscriptionQuotaStatusCode.AuthHostCommunicationFailed;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            linked.CancelAfter(InteractionTimeout);
            var connectTask = pipe.WaitForConnectionAsync(linked.Token);
            var exitTask = owned.Process.WaitForExitAsync(CancellationToken.None);
            if (await Task.WhenAny(connectTask, exitTask) != connectTask)
            {
                var exitCode = 0;
                try
                {
                    await exitTask.ConfigureAwait(false);
                    exitCode = owned.Process.ExitCode;
                }
                catch { }
                return new(SubscriptionQuotaStatusCode.AuthHostCommunicationFailed, null,
                    SubscriptionQuotaDiagnosticCode.None, exitCode);
            }
            await connectTask;
            if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var actualClientPid)
                || !AuthBounds.IsExpectedPeer(actualClientPid, owned.Pid))
                return new(SubscriptionQuotaStatusCode.AuthHostCommunicationFailed);

            var envelope = await ReadFrameAsync<AuthEnvelope>(pipe, linked.Token);
            if (envelope is null
                || envelope.Protocol != Protocol
                || envelope.HelperPid != owned.Pid
                || envelope.HelperStartTicks != owned.StartTicks
                || !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(envelope.Nonce ?? string.Empty), Encoding.UTF8.GetBytes(nonce))
                || !TryMapResponse(envelope.Response, out var mapped))
                return new(SubscriptionQuotaStatusCode.AuthHostCommunicationFailed);
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
                return new(SubscriptionQuotaStatusCode.AuthHostCommunicationFailed);
            return mapped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new(SubscriptionQuotaStatusCode.Cancelled);
        }
        catch
        {
            return failureStatus == SubscriptionQuotaStatusCode.AuthHostStartFailed
                ? new(failureStatus, null, failureDiagnostic)
                : new(failureStatus);
        }
        finally
        {
            owned?.TerminateIfOwned();
            owned?.Dispose();
            if (ticketPath is not null)
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
            _ => SubscriptionQuotaStatusCode.AuthHostCommunicationFailed
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

internal sealed record AuthHostLaunchResult(
    OwnedProcess? Process,
    SubscriptionQuotaDiagnosticCode Diagnostic,
    int NativeErrorCode = 0);

internal static class SecureProcessLauncher
{
    public static async Task<AuthHostLaunchResult> TryLaunchMediumAsync(
        string executable,
        string ticketPath,
        string pipeName,
        CancellationToken cancellationToken)
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (!OperatingSystem.IsWindows() || currentProcess.SessionId == 0)
            return Failed(SubscriptionQuotaDiagnosticCode.InteractiveShellUnavailable);
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
                if (process is null)
                    return Failed(SubscriptionQuotaDiagnosticCode.MediumProcessCreateFailed);
                var (owned, diagnostic) = await OwnedProcess.TryCreateAsync(
                    process, executable, cancellationToken).ConfigureAwait(false);
                return new(owned, diagnostic);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { return Failed(SubscriptionQuotaDiagnosticCode.MediumProcessCreateFailed); }
        }
        if (currentIntegrity is not (>= 0x3000 and < 0x5000))
            return Failed(SubscriptionQuotaDiagnosticCode.UnknownStartFailure);
        return await TryLaunchFromInteractiveShellAsync(
            executable, ticketPath, pipeName, currentProcess.SessionId, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<AuthHostLaunchResult> TryLaunchFromInteractiveShellAsync(
        string executable,
        string ticketPath,
        string pipeName,
        int sessionId,
        CancellationToken cancellationToken)
    {
        if (CreatedProcessReaper.HasPending)
            return Failed(SubscriptionQuotaDiagnosticCode.ChildCleanupFailed);

        var expectedSid = WindowsIdentity.GetCurrent().User;
        if (expectedSid is null)
            return Failed(SubscriptionQuotaDiagnosticCode.ShellTokenUnavailable);
        var explorers = Process.GetProcessesByName("explorer");
        var processErrors = new List<int>();
        var environmentErrors = new List<int>();
        try
        {
            if (!explorers.Any(process => TryGetSessionId(process, out var value) && value == sessionId))
                return Failed(SubscriptionQuotaDiagnosticCode.InteractiveShellUnavailable);

            foreach (var explorer in explorers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    if (!TryGetSessionId(explorer, out var explorerSessionId) || explorerSessionId != sessionId)
                        continue;
                    if (!OpenProcessToken(explorer.Handle, 0x0001 | 0x0002 | 0x0008, out var token))
                    {
                        processErrors.Add(Marshal.GetLastPInvokeError());
                        continue;
                    }
                    try
                    {
                        using var identity = new WindowsIdentity(token);
                        if (!expectedSid.Equals(identity.User)
                            || GetTokenType(token) != 1
                            || GetTokenIntegrityRid(token) is not (>= 0x2000 and < 0x3000))
                            continue;

                        if (!DuplicateTokenEx(token, 0x02000000, IntPtr.Zero, 2, 1, out var launchToken))
                        {
                            processErrors.Add(Marshal.GetLastPInvokeError());
                            continue;
                        }
                        try
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            if (!CreateEnvironmentBlock(out var environment, launchToken, false))
                            {
                                environmentErrors.Add(Marshal.GetLastPInvokeError());
                                continue;
                            }
                            try
                            {
                                var command = new StringBuilder(
                                    $"\"{executable}\" --ticket \"{ticketPath}\" --pipe \"{pipeName}\"");
                                var startup = new StartupInfo
                                {
                                    cb = Marshal.SizeOf<StartupInfo>(),
                                    lpDesktop = "winsta0\\default"
                                };
                                if (!CreateProcessWithTokenW(launchToken, 0, executable, command, 0x00000400,
                                        environment, AppContext.BaseDirectory, ref startup, out var info))
                                {
                                    var error = Marshal.GetLastPInvokeError();
                                    if (AuthHostLaunchErrorPolicy.IsGlobalProcessCreateError(error))
                                        return Failed(AuthHostLaunchErrorPolicy.MapProcessCreateError(error), error);
                                    processErrors.Add(error);
                                    continue;
                                }

                                SafeProcessHandle? processHandle = null;
                                try
                                {
                                    processHandle = new SafeProcessHandle(info.hProcess, ownsHandle: true);
                                    info.hProcess = IntPtr.Zero;
                                    var validation = await ValidateCreatedProcessAsync(
                                        processHandle, info.dwProcessId, executable, expectedSid, sessionId,
                                        cancellationToken).ConfigureAwait(false);
                                    if (!validation.IsValid)
                                    {
                                        var cleaned = CreatedProcessReaper.TerminateOrOwn(processHandle);
                                        processHandle = null;
                                        return Failed(cleaned
                                            ? validation.Diagnostic
                                            : SubscriptionQuotaDiagnosticCode.ChildCleanupFailed);
                                    }

                                    Process process;
                                    try
                                    {
                                        process = Process.GetProcessById(unchecked((int)info.dwProcessId));
                                    }
                                    catch
                                    {
                                        var cleaned = CreatedProcessReaper.TerminateOrOwn(processHandle);
                                        processHandle = null;
                                        return Failed(cleaned
                                            ? SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed
                                            : SubscriptionQuotaDiagnosticCode.ChildCleanupFailed);
                                    }

                                    var owned = OwnedProcess.FromValidatedHandle(
                                        process, executable, validation.StartTicks, processHandle);
                                    processHandle = null;
                                    return new(owned, SubscriptionQuotaDiagnosticCode.None);
                                }
                                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                                {
                                    if (processHandle is not null)
                                    {
                                        _ = CreatedProcessReaper.TerminateOrOwn(processHandle);
                                        processHandle = null;
                                    }
                                    throw;
                                }
                                catch
                                {
                                    if (processHandle is not null)
                                    {
                                        var cleaned = CreatedProcessReaper.TerminateOrOwn(processHandle);
                                        processHandle = null;
                                        return Failed(cleaned
                                            ? SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed
                                            : SubscriptionQuotaDiagnosticCode.ChildCleanupFailed);
                                    }
                                    return Failed(SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed);
                                }
                                finally
                                {
                                    if (info.hThread != IntPtr.Zero) CloseHandle(info.hThread);
                                    if (info.hProcess != IntPtr.Zero) CloseHandle(info.hProcess);
                                    processHandle?.Dispose();
                                }
                            }
                            finally
                            {
                                DestroyEnvironmentBlock(environment);
                            }
                        }
                        finally
                        {
                            CloseHandle(launchToken);
                        }
                    }
                    finally { CloseHandle(token); }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { }
            }
            if (processErrors.Count > 0)
            {
                var selected = AuthHostLaunchErrorPolicy.SelectDeterministic(processErrors);
                return Failed(selected.Diagnostic, selected.NativeErrorCode);
            }
            if (environmentErrors.Count > 0)
                return Failed(SubscriptionQuotaDiagnosticCode.EnvironmentBlockFailed,
                    SelectDeterministicCode(environmentErrors));
            return Failed(SubscriptionQuotaDiagnosticCode.ShellTokenUnavailable);
        }
        finally
        {
            foreach (var explorer in explorers) explorer.Dispose();
        }
    }

    private static AuthHostLaunchResult Failed(SubscriptionQuotaDiagnosticCode diagnostic, int nativeErrorCode = 0)
        => new(null, diagnostic, nativeErrorCode);

    private static async Task<CreatedProcessValidation> ValidateCreatedProcessAsync(
        SafeProcessHandle processHandle,
        uint processId,
        string expectedPath,
        SecurityIdentifier expectedSid,
        int expectedSessionId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (WaitForSingleObject(processHandle, 0) == 0)
                return new(false, 0, SubscriptionQuotaDiagnosticCode.ChildExitedEarly);

            if (TryQueryProcessPath(processHandle, out var actualPath)
                && TryGetProcessStartTicks(processHandle, out var startTicks))
            {
                if (!string.Equals(Path.GetFullPath(actualPath), Path.GetFullPath(expectedPath),
                        StringComparison.OrdinalIgnoreCase)
                    || !ProcessIdToSessionId(processId, out var actualSessionId)
                    || actualSessionId != expectedSessionId
                    || !OpenProcessToken(processHandle.DangerousGetHandle(), 0x0008, out var childToken))
                    return new(false, 0, SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed);
                try
                {
                    using var identity = new WindowsIdentity(childToken);
                    if (!expectedSid.Equals(identity.User)
                        || GetTokenType(childToken) != 1
                        || GetTokenIntegrityRid(childToken) is not (>= 0x2000 and < 0x3000))
                        return new(false, 0, SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed);
                }
                finally
                {
                    CloseHandle(childToken);
                }
                return new(true, startTicks, SubscriptionQuotaDiagnosticCode.None);
            }

            if (attempt < 19)
                await Task.Delay(50, cancellationToken).ConfigureAwait(false);
        }

        return new(false, 0, SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed);
    }

    private static bool TryGetSessionId(Process process, out int sessionId)
    {
        try
        {
            sessionId = process.SessionId;
            return true;
        }
        catch
        {
            sessionId = -1;
            return false;
        }
    }

    private static int GetTokenType(IntPtr token)
    {
        var buffer = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            return GetTokenInformation(token, 8, buffer, sizeof(int), out _)
                ? Marshal.ReadInt32(buffer)
                : -1;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static bool TryQueryProcessPath(SafeProcessHandle processHandle, out string path)
    {
        var capacity = 32_768;
        var buffer = new StringBuilder(capacity);
        if (QueryFullProcessImageNameW(processHandle, 0, buffer, ref capacity))
        {
            path = buffer.ToString();
            return path.Length > 0;
        }
        path = string.Empty;
        return false;
    }

    private static bool TryGetProcessStartTicks(SafeProcessHandle processHandle, out long startTicks)
    {
        startTicks = 0;
        if (!GetProcessTimes(processHandle, out var creation, out _, out _, out _)) return false;
        try
        {
            startTicks = DateTime.FromFileTimeUtc(creation.ToLong()).Ticks;
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static int SelectDeterministicCode(IReadOnlyCollection<int> errors)
        => errors.GroupBy(code => code)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key)
            .Select(group => group.Key)
            .FirstOrDefault();

    private readonly record struct CreatedProcessValidation(
        bool IsValid,
        long StartTicks,
        SubscriptionQuotaDiagnosticCode Diagnostic);

    private static int GetCurrentIntegrityRid()
    {
        using var currentProcess = Process.GetCurrentProcess();
        if (!OpenProcessToken(currentProcess.Handle, 0x0008, out var token)) return -1;
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
    [StructLayout(LayoutKind.Sequential)] private struct FileTime
    {
        public uint Low;
        public uint High;
        public long ToLong() => unchecked((long)(((ulong)High << 32) | Low));
    }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool DuplicateTokenEx(
        IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes, int impersonationLevel,
        int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessWithTokenW(IntPtr token, int logonFlags, string applicationName,
        StringBuilder commandLine, uint creationFlags, IntPtr environment, string currentDirectory,
        ref StartupInfo startupInfo, out ProcessInformation processInformation);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool CreateEnvironmentBlock(
        out IntPtr environment, IntPtr token, bool inherit);
    [DllImport("userenv.dll", SetLastError = true)] private static extern bool DestroyEnvironmentBlock(IntPtr environment);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageNameW(
        SafeProcessHandle process, uint flags, StringBuilder executableName, ref int size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetProcessTimes(
        SafeProcessHandle process, out FileTime creation, out FileTime exit, out FileTime kernel, out FileTime user);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool ProcessIdToSessionId(
        uint processId, out uint sessionId);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint WaitForSingleObject(
        SafeProcessHandle handle, uint milliseconds);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}

internal static class CurrentUserMediumPipe
{
    private const uint PipeAccessDuplex = 0x00000003;
    private const uint FileFlagOverlapped = 0x40000000;
    private const uint PipeRejectRemoteClients = 0x00000008;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    internal static NamedPipeServerStream Create(string pipeName, int inputBufferSize, int outputBufferSize)
    {
        if (string.IsNullOrWhiteSpace(pipeName) || pipeName.Length > 200
            || pipeName.Any(character => !(char.IsAsciiLetterOrDigit(character) || character == '-')))
            throw new ArgumentException("The pipe name is invalid.", nameof(pipeName));
        if (inputBufferSize <= 0 || outputBufferSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(inputBufferSize));

        var sid = WindowsIdentity.GetCurrent().User?.Value
                  ?? throw new InvalidOperationException("The current user SID is unavailable.");
        var sddl = $"D:P(A;;GA;;;{sid})S:(ML;;NW;;;ME)";
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl, 1, out var descriptor, out _))
            throw new Win32Exception(Marshal.GetLastPInvokeError());

        try
        {
            var attributes = new SecurityAttributes
            {
                Length = Marshal.SizeOf<SecurityAttributes>(),
                SecurityDescriptor = descriptor,
                InheritHandle = 0
            };
            var rawHandle = CreateNamedPipeW(
                $"\\\\.\\pipe\\{pipeName}",
                PipeAccessDuplex | FileFlagOverlapped,
                PipeRejectRemoteClients,
                1,
                unchecked((uint)outputBufferSize),
                unchecked((uint)inputBufferSize),
                0,
                ref attributes);
            if (rawHandle == InvalidHandleValue)
                throw new Win32Exception(Marshal.GetLastPInvokeError());

            var safeHandle = new SafePipeHandle(rawHandle, ownsHandle: true);
            try
            {
                var stream = new NamedPipeServerStream(
                    PipeDirection.InOut, isAsync: true, isConnected: false, safeHandle);
                safeHandle = null!;
                return stream;
            }
            finally
            {
                safeHandle?.Dispose();
            }
        }
        finally
        {
            _ = LocalFree(descriptor);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int Length;
        public IntPtr SecurityDescriptor;
        public int InheritHandle;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string stringSecurityDescriptor, uint stringSdRevision,
        out IntPtr securityDescriptor, out uint securityDescriptorSize);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateNamedPipeW(
        string name, uint openMode, uint pipeMode, uint maxInstances,
        uint outputBufferSize, uint inputBufferSize, uint defaultTimeout,
        ref SecurityAttributes securityAttributes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);
}

internal static class CreatedProcessReaper
{
    private static readonly ConcurrentDictionary<long, SafeProcessHandle> Pending = new();
    private static long _nextId;

    static CreatedProcessReaper()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            foreach (var handle in Pending.Values)
                try { _ = TerminateProcess(handle, 1); } catch { }
        };
    }

    internal static bool HasPending => !Pending.IsEmpty;

    internal static bool TerminateOrOwn(SafeProcessHandle handle)
    {
        if (handle.IsInvalid || handle.IsClosed)
        {
            handle.Dispose();
            return true;
        }

        if (WaitForSingleObject(handle, 0) == 0)
        {
            handle.Dispose();
            return true;
        }

        var terminated = TerminateProcess(handle, 1);
        if ((terminated && WaitForSingleObject(handle, 2_000) == 0)
            || (!terminated && WaitForSingleObject(handle, 0) == 0))
        {
            handle.Dispose();
            return true;
        }

        var id = Interlocked.Increment(ref _nextId);
        Pending[id] = handle;
        _ = ReapAsync(id, handle);
        return false;
    }

    private static async Task ReapAsync(long id, SafeProcessHandle handle)
    {
        try
        {
            while (WaitForSingleObject(handle, 0) != 0)
            {
                _ = TerminateProcess(handle, 1);
                if (WaitForSingleObject(handle, 1_000) == 0) break;
                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        finally
        {
            Pending.TryRemove(id, out _);
            handle.Dispose();
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(SafeProcessHandle process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);
}

internal sealed class OwnedProcess : IDisposable
{
    private static readonly ConcurrentDictionary<int, OwnedProcess> Active = new();
    static OwnedProcess() => AppDomain.CurrentDomain.ProcessExit += (_, _) =>
    {
        foreach (var process in Active.Values) process.TerminateIfOwned();
    };

    private readonly string _path;
    private SafeProcessHandle? _nativeHandle;
    private int _disposed;
    internal Process Process { get; }
    internal int Pid { get; }
    internal long StartTicks { get; }

    private OwnedProcess(Process process, string path)
    {
        Process = process; Pid = process.Id; StartTicks = process.StartTime.ToUniversalTime().Ticks; _path = Path.GetFullPath(path);
        Active[Pid] = this;
    }

    private OwnedProcess(Process process, string path, long startTicks, SafeProcessHandle nativeHandle)
    {
        Process = process;
        Pid = process.Id;
        StartTicks = startTicks;
        _path = Path.GetFullPath(path);
        _nativeHandle = nativeHandle;
        Active[Pid] = this;
    }

    internal static OwnedProcess FromValidatedHandle(
        Process process,
        string path,
        long startTicks,
        SafeProcessHandle nativeHandle)
        => new(process, path, startTicks, nativeHandle);

    internal static async Task<(OwnedProcess? Process, SubscriptionQuotaDiagnosticCode Diagnostic)> TryCreateAsync(
        Process process,
        string expectedPath,
        CancellationToken cancellationToken)
    {
        var diagnostic = SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed;
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                process.Refresh();
                if (process.HasExited)
                {
                    diagnostic = SubscriptionQuotaDiagnosticCode.ChildExitedEarly;
                    process.Dispose();
                    return (null, diagnostic);
                }
                var actual = process.MainModule?.FileName;
                if (actual is null)
                    throw new InvalidOperationException("The child executable is not ready for validation.");
                if (!string.Equals(Path.GetFullPath(actual), Path.GetFullPath(expectedPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    TerminateAndDisposeUnaccepted(process);
                    return (null, diagnostic);
                }
                var owned = new OwnedProcess(process, expectedPath);
                diagnostic = SubscriptionQuotaDiagnosticCode.None;
                return (owned, diagnostic);
            }
            catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
            {
                if (attempt < 19)
                {
                    try
                    {
                        await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        TerminateAndDisposeUnaccepted(process);
                        throw;
                    }
                    continue;
                }
            }
            catch
            {
                break;
            }
        }
        try
        {
            process.Refresh();
            if (process.HasExited)
                diagnostic = SubscriptionQuotaDiagnosticCode.ChildExitedEarly;
        }
        catch { }
        TerminateAndDisposeUnaccepted(process);
        return (null, diagnostic);
    }

    private static void TerminateAndDisposeUnaccepted(Process process)
    {
        try
        {
            process.Refresh();
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch { }
        finally { process.Dispose(); }
    }

    internal void TerminateIfOwned()
    {
        var nativeHandle = Interlocked.Exchange(ref _nativeHandle, null);
        if (nativeHandle is not null)
        {
            _ = CreatedProcessReaper.TerminateOrOwn(nativeHandle);
            return;
        }
        try
        {
            Process.Refresh();
            var actualPath = Process.MainModule?.FileName;
            if (!Process.HasExited && AuthBounds.IsOwnedProcess(Process.Id, Process.StartTime.ToUniversalTime().Ticks,
                    actualPath, Pid, StartTicks, _path)) Process.Kill(entireProcessTree: true);
        }
        catch { }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        TerminateIfOwned();
        Active.TryRemove(Pid, out _);
        Process.Dispose();
    }
}
