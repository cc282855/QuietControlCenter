using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Xuantong.AuthHost;

internal static class HostSecurity
{
    public static bool IsExpectedParent(int parentPid, long expectedStartTicks)
    {
        if (parentPid <= 0 || parentPid == Environment.ProcessId) return false;
        try
        {
            using var parent = Process.GetProcessById(parentPid);
            if (parent.HasExited || parent.SessionId != Process.GetCurrentProcess().SessionId
                || parent.StartTime.ToUniversalTime().Ticks != expectedStartTicks) return false;
            var parentHandle = OpenProcess(0x1000, false, parentPid);
            if (parentHandle == IntPtr.Zero) return false;
            try
            {
                if (!OpenProcessToken(parentHandle, 0x0008, out var token)) return false;
                try
                {
                    using var parentIdentity = new WindowsIdentity(token);
                    return WindowsIdentity.GetCurrent().User?.Equals(parentIdentity.User) == true;
                }
                finally { CloseHandle(token); }
            }
            finally { CloseHandle(parentHandle); }
        }
        catch { return false; }
    }

    public static bool IsMediumIntegrityInteractiveUser()
    {
        if (!OperatingSystem.IsWindows() || Process.GetCurrentProcess().SessionId == 0) return false;
        using var identity = WindowsIdentity.GetCurrent(TokenAccessLevels.Query);
        if (identity.User is null || identity.IsSystem || identity.IsAnonymous || identity.IsGuest)
        {
            return false;
        }
        if (!OpenProcessToken(Process.GetCurrentProcess().Handle, 0x0008, out var token)) return false;
        try
        {
            GetTokenInformation(token, 25, IntPtr.Zero, 0, out var length);
            var buffer = Marshal.AllocHGlobal(length);
            try
            {
                if (!GetTokenInformation(token, 25, buffer, length, out _)) return false;
                var label = Marshal.PtrToStructure<TokenMandatoryLabel>(buffer);
                var sid = new SecurityIdentifier(label.Label.Sid);
                var bytes = new byte[sid.BinaryLength];
                sid.GetBinaryForm(bytes, 0);
                var count = bytes[1];
                if (count == 0 || bytes.Length < 8 + count * 4) return false;
                var rid = BitConverter.ToUInt32(bytes, 8 + (count - 1) * 4);
                return rid is >= 0x2000 and < 0x3000;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        finally { CloseHandle(token); }
    }

    public static async Task<bool> IsLoopbackSocksAvailableAsync(int port)
    {
        if (port is <= 0 or > 65535) return false;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var client = new TcpClient(AddressFamily.InterNetwork);
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return true;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct SidAndAttributes { public IntPtr Sid; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)] private struct TokenMandatoryLabel { public SidAndAttributes Label; }
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr info, int length, out int returnLength);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
}
