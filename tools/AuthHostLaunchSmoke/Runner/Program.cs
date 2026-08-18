using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using v2rayN.Services;

var arguments = Environment.GetCommandLineArgs();
var actualEvidencePath = ValueAfter(arguments, "--actual-authhost-evidence");
if (actualEvidencePath is not null)
{
    using var actualTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    var actualResult = await new AuthHostClient().QuerySessionAsync(
        "qcc-synthetic-auth-smoke", "https://example.com/", 10808, actualTimeout.Token);
    var actualPassed = actualResult.Status == ServiceLib.Models.Dto.SubscriptionQuotaStatusCode.LoginRequired;
    await WriteEvidenceAsync(actualEvidencePath, new
    {
        status = actualPassed ? "PASS" : "FAIL",
        result = actualResult.Status.ToString(),
        diagnostic = actualResult.Diagnostic.ToString(),
        nativeErrorCode = actualResult.NativeErrorCode
    });
    return actualPassed ? 0 : 20;
}

var probePath = ValueAfter(arguments, "--probe");
var evidencePath = ValueAfter(arguments, "--evidence");
var allowMediumControl = arguments.Contains("--allow-medium-control", StringComparer.Ordinal);
if (probePath is null || evidencePath is null) return 2;

var token = OpenCurrentProcessToken();
if (token == IntPtr.Zero) return 3;
int parentIntegrity;
string expectedSidHash;
try
{
    parentIntegrity = GetIntegrityRid(token);
    using var identity = new WindowsIdentity(token);
    var sid = identity.User?.Value;
    if (sid is null) return 4;
    expectedSidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid)));
}
finally { CloseHandle(token); }

if (!(parentIntegrity is >= 0x3000 and < 0x5000)
    && !(allowMediumControl && parentIntegrity is >= 0x2000 and < 0x3000))
{
    await WriteEvidenceAsync(evidencePath, new { status = "FAIL", reason = "ParentNotHigh", parentIntegrity });
    return 5;
}

var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
var pipeName = "qcc-auth-smoke-" + Guid.NewGuid().ToString("N");
var ticketPath = Path.Combine(Path.GetTempPath(), "qcc-auth-smoke-" + Guid.NewGuid().ToString("N") + ".json");
await File.WriteAllBytesAsync(ticketPath, JsonSerializer.SerializeToUtf8Bytes(new SmokeTicket(nonce)));
Environment.SetEnvironmentVariable("QCC_AUTH_SMOKE_PARENT_ONLY", Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
OwnedProcess? owned = null;
try
{
    await using var pipe = CurrentUserMediumPipe.Create(pipeName, 4096, 16_384);
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
    var launch = await SecureProcessLauncher.TryLaunchMediumAsync(
        probePath, ticketPath, pipeName, timeout.Token);
    owned = launch.Process;
    if (owned is null)
    {
        await WriteEvidenceAsync(evidencePath, new
        {
            status = "FAIL", reason = "LaunchFailed", parentIntegrity,
            diagnostic = launch.Diagnostic.ToString(), nativeErrorCode = launch.NativeErrorCode
        });
        return 6;
    }

    var connectTask = pipe.WaitForConnectionAsync(timeout.Token);
    var exitTask = owned.Process.WaitForExitAsync(CancellationToken.None);
    if (await Task.WhenAny(connectTask, exitTask) == exitTask)
    {
        int? exitCode = null;
        try { exitCode = owned.Process.ExitCode; } catch { }
        await WriteEvidenceAsync(evidencePath, new
        {
            status = "FAIL", reason = "ChildExitedBeforeHandshake", parentIntegrity, exitCode
        });
        return 12;
    }
    await connectTask;
    if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle, out var pipePid)) return 7;
    var prefix = new byte[4];
    await pipe.ReadExactlyAsync(prefix, timeout.Token);
    var length = BinaryPrimitives.ReadInt32LittleEndian(prefix);
    if (length is <= 0 or > 16_384) return 8;
    var payload = new byte[length];
    await pipe.ReadExactlyAsync(payload, timeout.Token);
    var message = JsonSerializer.Deserialize<SmokeMessage>(payload);
    if (message is null) return 9;

    using var current = Process.GetCurrentProcess();
    var passed = message.Nonce == nonce
                 && message.Pid == owned.Pid
                 && message.Pid == pipePid
                 && message.StartTicks == owned.StartTicks
                 && message.SessionId == current.SessionId
                 && message.IntegrityRid is >= 0x2000 and < 0x3000
                 && message.TokenType == 1
                 && CryptographicOperations.FixedTimeEquals(
                     Convert.FromHexString(message.SidHash), Convert.FromHexString(expectedSidHash))
                 && !message.ParentCanaryVisible;
    await pipe.WriteAsync(new byte[] { passed ? (byte)0xA5 : (byte)0x00 }, timeout.Token);
    await pipe.FlushAsync(timeout.Token);
    await owned.Process.WaitForExitAsync(timeout.Token);

    await WriteEvidenceAsync(evidencePath, new
    {
        status = passed ? "PASS" : "FAIL",
        parentIntegrity,
        childIntegrity = message.IntegrityRid,
        sameSession = message.SessionId == current.SessionId,
        sameSid = message.SidHash == expectedSidHash,
        tokenPrimary = message.TokenType == 1,
        exactPid = message.Pid == owned.Pid && message.Pid == pipePid,
        exactStartTime = message.StartTicks == owned.StartTicks,
        nonceHandshake = message.Nonce == nonce,
        parentEnvironmentCanaryHidden = !message.ParentCanaryVisible
    });
    return passed ? 0 : 10;
}
catch (Exception exception)
{
    int? childExitCode = null;
    bool? childHasExited = null;
    if (owned is not null)
    {
        try
        {
            childHasExited = owned.Process.HasExited;
            if (childHasExited == true) childExitCode = owned.Process.ExitCode;
        }
        catch { }
    }
    await WriteEvidenceAsync(evidencePath, new
    {
        status = "FAIL", reason = exception.GetType().Name, parentIntegrity,
        launchAccepted = owned is not null, childHasExited, childExitCode
    });
    return 11;
}
finally
{
    owned?.TerminateIfOwned();
    owned?.Dispose();
    Environment.SetEnvironmentVariable("QCC_AUTH_SMOKE_PARENT_ONLY", null);
    try { File.Delete(ticketPath); } catch { }
}

static string? ValueAfter(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static Task WriteEvidenceAsync(string path, object evidence)
    => File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));

static IntPtr OpenCurrentProcessToken()
{
    using var current = Process.GetCurrentProcess();
    return OpenProcessToken(current.Handle, 0x0008, out var token) ? token : IntPtr.Zero;
}

static int GetIntegrityRid(IntPtr token)
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

[DllImport("advapi32.dll", SetLastError = true)]
static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);
[DllImport("kernel32.dll", SetLastError = true)]
static extern bool GetNamedPipeClientProcessId(Microsoft.Win32.SafeHandles.SafePipeHandle pipe, out uint clientProcessId);

record SmokeTicket(string Nonce);
record SmokeMessage(
    string Nonce, int Pid, long StartTicks, int SessionId, int IntegrityRid,
    int TokenType, string SidHash, bool ParentCanaryVisible);
[StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
[StructLayout(LayoutKind.Sequential)] struct TokenMandatoryLabel { public SidAndAttributes Label; }
