using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AmazTool;
using Xunit;

namespace v2rayN.Tests;

public sealed class UpgradeSafetyTests
{
    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("C:/absolute.txt")]
    [InlineData("CON/file.txt")]
    public void UnsafePathsFailBeforeInstallMutation(string entryName)
    {
        var fixture = InvalidFixture(entryName);
        Assert.ThrowsAny<Exception>(() => UpgradeApp.Execute(fixture.Instruction));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
    }

    [Fact]
    public void CaseCollisionsFailBeforeInstallMutation()
    {
        var fixture = InvalidFixture("A.txt", "a.TXT");
        Assert.Throws<InvalidDataException>(() => UpgradeApp.Execute(fixture.Instruction));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
    }

    [Fact]
    public void InstructionOutsideGeneratedWorkRootFails()
    {
        var root = Path.Combine(Path.GetTempPath(), "not-qcc", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var instruction = Path.Combine(root, "instruction.json"); File.WriteAllText(instruction, "{}"); File.WriteAllText(Path.Combine(root, "instruction.sha256"), Hash(instruction));
        Assert.Throws<InvalidDataException>(() => UpgradeApp.Execute(instruction));
    }

    [Fact]
    public void MutableMarkerCollisionFailsClosed()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = [1], ["guiConfigs/owned.db"] = [2] });
        try { Assert.Throws<InvalidDataException>(() => UpgradeApp.Execute(fixture.Instruction)); }
        finally { Stop(fixture.Parent); }
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
    }

    [Fact]
    public void ReparseMutableRootIsRejected()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = File.ReadAllBytes(SystemExe("where.exe")) });
        var outside = Path.Combine(Path.GetTempPath(), "qcc-outside", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outside); File.WriteAllText(Path.Combine(outside, "secret"), "x");
        var link = Path.Combine(fixture.Install, "guiConfigs");
        var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{outside}\"") { UseShellExecute = false, CreateNoWindow = true })!;
        mklink.WaitForExit(); Assert.Equal(0, mklink.ExitCode);
        try { Assert.Throws<InvalidDataException>(() => UpgradeApp.Execute(fixture.Instruction)); }
        finally { Stop(fixture.Parent); Directory.Delete(link); }
        Assert.True(File.Exists(Path.Combine(outside, "secret")));
        Directory.Delete(outside, true);
    }

    [Fact]
    public void ReadyPrecedesParentExitAndInstallSwap()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = File.ReadAllBytes(SystemExe("where.exe")) });
        var readyObserved = false;
        var hooks = AckHooks(fixture);

        var exit = UpgradeApp.ExecuteWithReady(fixture.Instruction, hooks, ready =>
        {
            readyObserved = true;
            Assert.Equal(fixture.Token, ready.Token);
            Assert.NotNull(fixture.Parent);
            Assert.False(fixture.Parent!.HasExited);
            Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
            Stop(fixture.Parent);
            Assert.True(fixture.Parent.WaitForExit(5000));
        });

        Assert.Equal(0, exit);
        Assert.True(readyObserved);
        Assert.False(File.Exists(Path.Combine(fixture.Install, "sentinel.txt")));
        Assert.False(Directory.Exists(fixture.Root));
    }

    [Fact]
    public void EarlyValidationFailureCleansWorkRootAndStage()
    {
        var fixture = InvalidFixture("../escape.txt");
        var stage = Path.Combine(Directory.GetParent(fixture.Install)!.FullName, ".qcc-stage-" + fixture.Token[..12]);

        Assert.ThrowsAny<Exception>(() => UpgradeApp.Execute(fixture.Instruction));

        Assert.False(Directory.Exists(fixture.Root));
        Assert.False(Directory.Exists(stage));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
    }

    [Fact]
    public void ValidPackageWithoutStartupAckRollsBack()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = File.ReadAllBytes(SystemExe("where.exe")) });
        Assert.Throws<InvalidOperationException>(() => UpgradeApp.Execute(fixture.Instruction));
        Assert.Equal("old", File.ReadAllText(Path.Combine(fixture.Install, "sentinel.txt")));
        Assert.True(File.Exists(Path.Combine(Directory.GetParent(fixture.Install)!.FullName, ".qcc-failed-" + fixture.Token[..12], "v2rayN.exe")));
    }

    [Fact]
    public void ValidAcknowledgementCommitsAndPreservesOnlyMutableData()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = File.ReadAllBytes(SystemExe("where.exe")) });
        Directory.CreateDirectory(Path.Combine(fixture.Install, "guiConfigs")); File.WriteAllText(Path.Combine(fixture.Install, "guiConfigs", "user.db"), "user");
        Directory.CreateDirectory(Path.Combine(fixture.Install, "bin")); File.WriteAllText(Path.Combine(fixture.Install, "bin", "old-core.exe"), "old");
        var hooks = AckHooks(fixture);
        Assert.Equal(0, UpgradeApp.Execute(fixture.Instruction, hooks));
        Assert.True(File.Exists(Path.Combine(fixture.Install, "guiConfigs", "user.db")));
        Assert.False(File.Exists(Path.Combine(fixture.Install, "bin", "old-core.exe")));
        Assert.False(File.Exists(Path.Combine(fixture.Install, "sentinel.txt")));
    }

    [Fact]
    public void BackupCleanupFailureDoesNotRollbackCommittedInstall()
    {
        var fixture = PackageFixture(new Dictionary<string, byte[]> { ["v2rayN.exe"] = File.ReadAllBytes(SystemExe("where.exe")) });
        var hooks = AckHooks(fixture, () => throw new IOException("simulated cleanup lock"));
        Assert.Equal(0, UpgradeApp.Execute(fixture.Instruction, hooks));
        Assert.False(File.Exists(Path.Combine(fixture.Install, "sentinel.txt")));
        Assert.True(Directory.Exists(Path.Combine(Directory.GetParent(fixture.Install)!.FullName, ".qcc-backup-" + fixture.Token[..12])));
    }

    private static UpgradeTestHooks AckHooks(Fixture fixture, Action? beforeCleanup = null) => new()
    {
        BeforeBackupDelete = beforeCleanup,
        StartProcess = _ =>
        {
            File.WriteAllText(Path.Combine(fixture.Root, "startup.ack"), fixture.Token);
            return Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 3 127.0.0.1 > nul") { UseShellExecute = false, CreateNoWindow = true });
        }
    };

    private static Fixture InvalidFixture(params string[] entries)
    {
        var root = WorkRoot(); var install = Path.Combine(Directory.GetParent(root)!.FullName, "app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(install); File.WriteAllText(Path.Combine(install, "sentinel.txt"), "old");
        var package = Path.Combine(root, "package.zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create)) foreach (var name in entries) { var e = zip.CreateEntry(name); using var w = new StreamWriter(e.Open()); w.Write("x"); }
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var instruction = WriteInstruction(root, install, package, Process.GetCurrentProcess(), Process.GetCurrentProcess().MainModule!.FileName!, "7.25.0", token);
        return new(root, install, instruction, null, token);
    }

    private static Fixture PackageFixture(Dictionary<string, byte[]> files)
    {
        var root = WorkRoot(); var install = Path.Combine(Directory.GetParent(root)!.FullName, "app-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(install);
        var oldExe = Path.Combine(install, "v2rayN.exe"); File.Copy(SystemExe("ping.exe"), oldExe); File.WriteAllText(Path.Combine(install, "sentinel.txt"), "old");
        var package = Path.Combine(root, "package.zip");
        using (var zip = ZipFile.Open(package, ZipArchiveMode.Create))
        {
            var hashes = new Dictionary<string, string>();
            foreach (var pair in files) { var e = zip.CreateEntry(pair.Key); using var s = e.Open(); s.Write(pair.Value); hashes[pair.Key] = Convert.ToHexString(SHA256.HashData(pair.Value)); }
            var marker = zip.CreateEntry("qcc-package.json"); using var stream = marker.Open(); JsonSerializer.Serialize(stream, new { product = "QuietControlCenter", platform = "win-x64", version = "7.25.0", files = hashes });
        }
        var parent = Process.Start(new ProcessStartInfo(oldExe, "-n 2 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
        var instruction = WriteInstruction(root, install, package, parent, oldExe, "7.25.0", token);
        return new(root, install, instruction, parent, token);
    }

    private static string WriteInstruction(string root, string install, string package, Process process, string originExe, string version, string token)
    {
        var instruction = Path.Combine(root, "instruction.json");
        File.WriteAllText(instruction, JsonSerializer.Serialize(new
        {
            schema = 1, product = "QuietControlCenter", packagePath = package, installDirectory = install, mainExecutable = "v2rayN.exe",
            expectedPackageSha256 = Hash(package), expectedVersion = version, originExecutablePath = originExe, originExecutableSha256 = Hash(originExe),
            processId = process.Id, processStartTimeUtc = new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero), token, ackPath = Path.Combine(root, "startup.ack")
        }));
        File.WriteAllText(Path.Combine(root, "instruction.sha256"), Hash(instruction));
        return instruction;
    }
    private static string WorkRoot() { var root = Path.Combine(Path.GetTempPath(), "QuietControlCenter", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root); return root; }
    private static string SystemExe(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), name);
    private static string Hash(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    private static void Stop(Process? process) { try { if (process is { HasExited: false }) process.Kill(true); } catch { } }
    private sealed record Fixture(string Root, string Install, string Instruction, Process? Parent, string Token);
}
