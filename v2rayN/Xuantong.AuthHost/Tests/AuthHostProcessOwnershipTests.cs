using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using ServiceLib.Models.Dto;
using v2rayN.Services;
using Xunit;

namespace Xuantong.AuthHost.Tests;

public sealed class AuthHostProcessOwnershipTests
{
    [Fact]
    public async Task RawHandleCleanupTerminatesSyntheticChildAndReleasesOwnership()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var process = StartSyntheticChild();
        var raw = OpenProcess(0x0001 | 0x00100000 | 0x1000, false, unchecked((uint)process.Id));
        Assert.NotEqual(IntPtr.Zero, raw);
        var handle = new SafeProcessHandle(raw, ownsHandle: true);

        _ = CreatedProcessReaper.TerminateOrOwn(handle);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await process.WaitForExitAsync(timeout.Token);
        for (var attempt = 0; attempt < 40 && CreatedProcessReaper.HasPending; attempt++)
            await Task.Delay(50, timeout.Token);

        Assert.True(process.HasExited);
        Assert.False(CreatedProcessReaper.HasPending);
    }

    [Fact]
    public async Task UnacceptedProcessIsTerminatedInsteadOfBeingReturned()
    {
        if (!OperatingSystem.IsWindows()) return;
        var process = StartSyntheticChild();
        var pid = process.Id;
        var impossibleExpectedPath = Path.Combine(Path.GetTempPath(), "not-the-created-process.exe");

        var result = await OwnedProcess.TryCreateAsync(
            process, impossibleExpectedPath, TestContext.Current.CancellationToken);

        Assert.Null(result.Process);
        Assert.Equal(SubscriptionQuotaDiagnosticCode.ChildIdentityValidationFailed, result.Diagnostic);
        var isRunning = true;
        for (var attempt = 0; attempt < 40 && isRunning; attempt++)
        {
            try { using var observed = Process.GetProcessById(pid); }
            catch (ArgumentException) { isRunning = false; }
            if (isRunning) await Task.Delay(50, TestContext.Current.CancellationToken);
        }
        Assert.False(isRunning);
    }

    private static Process StartSyntheticChild()
        => Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"),
            "/d /s /c \"ping -n 30 127.0.0.1 >nul\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        })!;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);
}
