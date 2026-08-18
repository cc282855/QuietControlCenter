using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

var arguments = Environment.GetCommandLineArgs();
var ticketPath = ValueAfter(arguments, "--ticket");
var pipeName = ValueAfter(arguments, "--pipe");
if (ticketPath is null || pipeName is null) return 2;

SmokeTicket? ticket;
try
{
    ticket = JsonSerializer.Deserialize<SmokeTicket>(await File.ReadAllBytesAsync(ticketPath));
}
catch { return 21; }
if (ticket is null || string.IsNullOrWhiteSpace(ticket.Nonce)) return 3;

using var process = Process.GetCurrentProcess();
var token = OpenCurrentProcessToken();
if (token == IntPtr.Zero) return 4;
try
{
    SmokeMessage message;
    try
    {
        using var identity = new WindowsIdentity(token);
        var sid = identity.User?.Value;
        if (sid is null) return 5;
        message = new SmokeMessage(
            ticket.Nonce,
            process.Id,
            process.StartTime.ToUniversalTime().Ticks,
            process.SessionId,
            GetIntegrityRid(token),
            GetTokenType(token),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid))),
            Environment.GetEnvironmentVariable("QCC_AUTH_SMOKE_PARENT_ONLY") is not null);
    }
    catch { return 22; }

    try
    {
        await using var pipe = new NamedPipeClientStream(
            ".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await pipe.ConnectAsync(timeout.Token);
        var payload = JsonSerializer.SerializeToUtf8Bytes(message);
        var prefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(prefix, payload.Length);
        await pipe.WriteAsync(prefix, timeout.Token);
        await pipe.WriteAsync(payload, timeout.Token);
        await pipe.FlushAsync(timeout.Token);
        var ack = new byte[1];
        await pipe.ReadExactlyAsync(ack, timeout.Token);
        return ack[0] == 0xA5 ? 0 : 6;
    }
    catch { return 23; }
}
finally
{
    CloseHandle(token);
}

static string? ValueAfter(string[] args, string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

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

static int GetTokenType(IntPtr token)
{
    var buffer = Marshal.AllocHGlobal(sizeof(int));
    try
    {
        return GetTokenInformation(token, 8, buffer, sizeof(int), out _)
            ? Marshal.ReadInt32(buffer)
            : -1;
    }
    finally { Marshal.FreeHGlobal(buffer); }
}

[DllImport("advapi32.dll", SetLastError = true)]
static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
[DllImport("advapi32.dll", SetLastError = true)]
static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
[DllImport("kernel32.dll")]
static extern bool CloseHandle(IntPtr handle);

record SmokeTicket(string Nonce);
record SmokeMessage(
    string Nonce, int Pid, long StartTicks, int SessionId, int IntegrityRid,
    int TokenType, string SidHash, bool ParentCanaryVisible);
[StructLayout(LayoutKind.Sequential)] struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
[StructLayout(LayoutKind.Sequential)] struct TokenMandatoryLabel { public SidAndAttributes Label; }
