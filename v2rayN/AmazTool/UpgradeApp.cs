using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AmazTool;

internal static class UpgradeApp
{
    private const long MaxArchiveBytes = 512L * 1024 * 1024;
    private const long MaxExpandedBytes = 2L * 1024 * 1024 * 1024;
    private const int MaxEntries = 10000;
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static int Execute(string instructionFile, UpgradeTestHooks? hooks = null)
        => ExecuteWithReady(instructionFile, hooks, _ => { });

    internal static int ExecuteWithReady(string instructionFile, UpgradeTestHooks? hooks, Action<UpgradeReady> signalReady)
    {
        instructionFile = ValidateWorkPath(instructionFile, "instruction.json", out var workRoot);
        try { return ExecuteValidated(instructionFile, workRoot, hooks, signalReady); }
        finally { CleanupWorkRoot(workRoot); }
    }

    private static int ExecuteValidated(string instructionFile, string workRoot, UpgradeTestHooks? hooks, Action<UpgradeReady> signalReady)
    {
        var instructionHashPath = Path.Combine(workRoot, "instruction.sha256");
        if (!File.Exists(instructionFile) || !File.Exists(instructionHashPath) ||
            !string.Equals(File.ReadAllText(instructionHashPath).Trim(), HashFile(instructionFile), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Instruction hash is absent or invalid.");

        var instruction = JsonSerializer.Deserialize<UpgradeInstruction>(File.ReadAllText(instructionFile), JsonOptions())
            ?? throw new InvalidDataException("Invalid instruction.");
        ValidateInstruction(instruction, workRoot);

        var package = Path.GetFullPath(instruction.PackagePath);
        var install = TrimSeparator(Path.GetFullPath(instruction.InstallDirectory));
        var parent = Directory.GetParent(install)?.FullName ?? throw new InvalidDataException("Install directory has no parent.");
        if (!File.Exists(package) || new FileInfo(package).Length > MaxArchiveBytes)
            throw new InvalidDataException("Package is absent or too large.");

        // Acquire and validate the exact originating process before any expensive
        // package work. The retained Process handle preserves identity even if
        // the GUI exits immediately after receiving the ready handshake.
        using var originProcess = AcquireExactProcess(instruction, install);

        var nonce = instruction.Token[..12];
        var stage = Path.Combine(parent, $".qcc-stage-{nonce}");
        var backup = Path.Combine(parent, $".qcc-backup-{nonce}");
        var failed = Path.Combine(parent, $".qcc-failed-{nonce}");
        var committed = false;
        try
        {
            EnsureAbsent(stage, backup, failed);
            ExtractValidated(package, stage, instruction.ExpectedPackageSha256);
            var marker = ValidateMarker(stage, instruction.ExpectedVersion, false);
            PreserveMutableData(install, stage, marker);
            ValidateMarker(stage, instruction.ExpectedVersion, true);

            signalReady(new UpgradeReady(
                instruction.Token,
                Environment.ProcessId,
                instruction.ProcessId,
                instruction.ProcessStartTimeUtc,
                instruction.OriginExecutablePath,
                instruction.OriginExecutableSha256));

            if (!originProcess.WaitForExit(60000)) throw new TimeoutException("Application did not exit after ready.");
            Directory.Move(install, backup);
            Directory.Move(stage, install);

            var exe = Path.Combine(install, instruction.MainExecutable);
            var startInfo = new ProcessStartInfo(exe,
                $"--qcc-startup-ack \"{instruction.AckPath}\" {instruction.Token}")
            { UseShellExecute = false, WorkingDirectory = install };
            Func<ProcessStartInfo, Process?> launcher = hooks?.StartProcess ?? (info => Process.Start(info));
            var started = launcher(startInfo)
                ?? throw new InvalidOperationException("Updated application did not start.");

            if (!WaitForAck(instruction.AckPath, instruction.Token, started, TimeSpan.FromSeconds(30)))
            {
                TryKillExact(started);
                Directory.Move(install, failed);
                Directory.Move(backup, install);
                throw new InvalidOperationException("Startup acknowledgement timed out; rollback completed.");
            }

            // A valid startup acknowledgement commits the transaction. Cleanup
            // is deliberately outside rollback semantics and may be retried later.
            committed = true;
            try
            {
                hooks?.BeforeBackupDelete?.Invoke();
                Directory.Delete(backup, true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Committed update; deferred backup cleanup: {ex.Message}");
            }
            TryDelete(package);
            TryDelete(instructionFile);
            return 0;
        }
        catch
        {
            if (!committed && Directory.Exists(backup))
            {
                if (Directory.Exists(install)) Directory.Move(install, failed);
                Directory.Move(backup, install);
            }
            throw;
        }
        finally
        {
            try { if (Directory.Exists(stage)) Directory.Delete(stage, true); } catch { }
        }
    }

    private static void ValidateInstruction(UpgradeInstruction i, string workRoot)
    {
        if (i.Schema != 1 || i.Product != "QuietControlCenter" ||
            string.IsNullOrWhiteSpace(i.Token) || i.Token.Length < 24 ||
            Path.GetFullPath(i.PackagePath) != Path.Combine(workRoot, "package.zip") ||
            Path.GetFullPath(i.AckPath) != Path.Combine(workRoot, "startup.ack") ||
            !Path.IsPathFullyQualified(i.InstallDirectory) || !IsSha(i.ExpectedPackageSha256) ||
            !IsSha(i.OriginExecutableSha256) || !Path.IsPathFullyQualified(i.OriginExecutablePath) ||
            i.ProcessId <= 0 || i.ProcessStartTimeUtc == default ||
            i.MainExecutable.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            Path.GetFileName(i.MainExecutable) != i.MainExecutable)
            throw new InvalidDataException("Instruction fields are invalid.");
    }

    private static string ValidateWorkPath(string instructionFile, string exactName, out string workRoot)
    {
        if (!Path.IsPathFullyQualified(instructionFile)) throw new InvalidDataException("Work path must be absolute.");
        var full = Path.GetFullPath(instructionFile);
        workRoot = Path.GetDirectoryName(full) ?? throw new InvalidDataException("Missing work root.");
        var expectedParent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "QuietControlCenter"));
        if (!string.Equals(Directory.GetParent(workRoot)?.FullName, expectedParent, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(workRoot), "N", out _) ||
            !string.Equals(Path.GetFileName(full), exactName, StringComparison.Ordinal))
            throw new InvalidDataException("Instruction is outside the generated QCC work root.");
        return full;
    }

    private static void ExtractValidated(string package, string stage, string expectedSha)
    {
        if (!string.Equals(HashFile(package), expectedSha, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Package hash changed before installation.");
        Directory.CreateDirectory(stage);
        var root = TrimSeparator(Path.GetFullPath(stage)) + Path.DirectorySeparatorChar;
        var paths = new HashSet<string>(PathComparer);
        long expanded = 0;
        using var archive = ZipFile.OpenRead(package);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("Too many ZIP entries.");
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
            if (string.IsNullOrWhiteSpace(name) || Path.IsPathFullyQualified(name) || name.Contains(':') || IsLink(entry))
                throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
            var segments = name.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Any(s => s is "." or ".." || IsDeviceName(s)))
                throw new InvalidDataException($"Unsafe ZIP entry: {entry.FullName}");
            var output = Path.GetFullPath(Path.Combine(stage, name));
            if (!output.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !paths.Add(output))
                throw new InvalidDataException($"ZIP path traversal/collision: {entry.FullName}");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaxExpandedBytes || (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > 200))
                throw new InvalidDataException("ZIP expansion limit exceeded.");
            if (entry.Name.Length == 0) { Directory.CreateDirectory(output); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            entry.ExtractToFile(output, false);
            if ((File.GetAttributes(output) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse point in package.");
        }
    }

    private static PackageMarker ValidateMarker(string stage, string expectedVersion, bool allowMutableExtras)
    {
        var markerPath = Path.Combine(stage, "qcc-package.json");
        var marker = JsonSerializer.Deserialize<PackageMarker>(File.ReadAllText(markerPath), JsonOptions())
            ?? throw new InvalidDataException("Missing package marker.");
        if (marker.Product != "QuietControlCenter" || marker.Platform != "win-x64" || marker.Version != expectedVersion || marker.Files.Count == 0 ||
            marker.Files.Keys.Any(IsMutablePath))
            throw new InvalidDataException("Package marker does not match instruction.");
        foreach (var pair in marker.Files)
        {
            if (!IsSha(pair.Value) || Path.IsPathFullyQualified(pair.Key) || pair.Key.Contains(".."))
                throw new InvalidDataException("Invalid marker path/hash.");
            var path = Path.GetFullPath(Path.Combine(stage, pair.Key));
            if (!path.StartsWith(TrimSeparator(Path.GetFullPath(stage)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path) || !string.Equals(HashFile(path), pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"Payload hash mismatch: {pair.Key}");
        }
        var actualFiles = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(stage, path).Replace('\\', '/'))
            .Where(path => !path.Equals("qcc-package.json", StringComparison.OrdinalIgnoreCase))
            .Where(path => !allowMutableExtras || !IsMutablePath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!actualFiles.SetEquals(marker.Files.Keys.Select(path => path.Replace('\\', '/'))))
            throw new InvalidDataException("Package contains unmarked payload files.");
        if (!marker.Files.Keys.Any(k => string.Equals(k.Replace('/', Path.DirectorySeparatorChar), "v2rayN.exe", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Main executable is not covered by marker.");
        return marker;
    }

    private static void PreserveMutableData(string oldRoot, string newRoot, PackageMarker marker)
    {
        foreach (var name in new[] { "guiConfigs", "guiLogs", "logs", "binConfigs" })
        {
            if (marker.Files.Keys.Any(path => IsUnder(path, name))) throw new InvalidDataException("Signed payload collides with mutable data.");
            var source = Path.Combine(oldRoot, name);
            if (Directory.Exists(source)) CopyTree(source, Path.Combine(newRoot, name));
        }
    }

    private static void CopyTree(string source, string destination)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Mutable data root is a reparse point.");
        Directory.CreateDirectory(destination);
        CopyTreeLevel(source, destination);
    }

    private static void CopyTreeLevel(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in mutable data.");
            var target = Path.Combine(destination, Path.GetFileName(directory));
            Directory.CreateDirectory(target);
            CopyTreeLevel(directory, target);
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Reparse point in mutable data.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
        }
    }

    private static Process AcquireExactProcess(UpgradeInstruction instruction, string install)
    {
        var p = Process.GetProcessById(instruction.ProcessId);
        var actual = new DateTimeOffset(p.StartTime.ToUniversalTime(), TimeSpan.Zero);
        var expectedExe = Path.GetFullPath(Path.Combine(install, instruction.MainExecutable));
        var processExe = Path.GetFullPath(p.MainModule?.FileName ?? throw new InvalidDataException("Cannot prove parent executable."));
        if (Math.Abs((actual - instruction.ProcessStartTimeUtc).TotalSeconds) > 2 ||
            !string.Equals(expectedExe, Path.GetFullPath(instruction.OriginExecutablePath), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(expectedExe, processExe, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(HashFile(processExe), instruction.OriginExecutableSha256, StringComparison.OrdinalIgnoreCase))
        {
            p.Dispose();
            throw new InvalidDataException("Exact parent PID/start/path/hash could not be proven.");
        }
        return p;
    }

    private static void CleanupWorkRoot(string workRoot)
    {
        try { Directory.Delete(workRoot, true); return; } catch { }
        try
        {
            var escaped = workRoot.Replace("'", "''");
            var script = $"$p=Get-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue;if($p){{$p|Wait-Process}};Remove-Item -LiteralPath '{escaped}' -Recurse -Force -ErrorAction SilentlyContinue";
            var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
            _ = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}")
            { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }
        catch { }
    }

    private static bool WaitForAck(string path, string token, Process process, TimeSpan timeout)
    {
        var end = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < end)
        {
            if (File.Exists(path) && string.Equals(File.ReadAllText(path).Trim(), token, StringComparison.Ordinal)) { TryDelete(path); return true; }
            if (process.HasExited) return false;
            Thread.Sleep(200);
        }
        return false;
    }

    private static bool IsLink(ZipArchiveEntry e) => ((e.ExternalAttributes >> 16) & 0xF000) == 0xA000;
    private static bool IsDeviceName(string s)
    {
        var n = s.TrimEnd('.', ' ').Split('.')[0].ToUpperInvariant();
        return n is "CON" or "PRN" or "AUX" or "NUL" ||
               (n.Length == 4 && (n.StartsWith("COM") || n.StartsWith("LPT")) && n[3] is >= '1' and <= '9');
    }
    private static string HashFile(string path) { using var s = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(s)); }
    private static bool IsSha(string? s) => s?.Length == 64 && s.All(Uri.IsHexDigit);
    private static bool IsMutablePath(string path) => new[] { "guiConfigs", "guiLogs", "logs", "binConfigs" }.Any(root => IsUnder(path, root));
    private static bool IsUnder(string path, string root) => path.Replace('\\', '/').Equals(root, StringComparison.OrdinalIgnoreCase) || path.Replace('\\', '/').StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    private static string TrimSeparator(string p) => Path.TrimEndingDirectorySeparator(p);
    private static void EnsureAbsent(params string[] paths) { if (paths.Any(p => Directory.Exists(p) || File.Exists(p))) throw new IOException("Staging path already exists."); }
    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }
    private static void TryKillExact(Process p) { try { if (!p.HasExited) { p.Kill(true); p.WaitForExit(5000); } } catch { } }
    private static JsonSerializerOptions JsonOptions() => new(JsonSerializerDefaults.Web) { PropertyNameCaseInsensitive = false };

    private sealed class UpgradeInstruction
    {
        public int Schema { get; set; }
        public string Product { get; set; } = "";
        public string PackagePath { get; set; } = "";
        public string InstallDirectory { get; set; } = "";
        public string MainExecutable { get; set; } = "v2rayN.exe";
        public string ExpectedPackageSha256 { get; set; } = "";
        public string ExpectedVersion { get; set; } = "";
        public string OriginExecutablePath { get; set; } = "";
        public string OriginExecutableSha256 { get; set; } = "";
        public int ProcessId { get; set; }
        public DateTimeOffset ProcessStartTimeUtc { get; set; }
        public string Token { get; set; } = "";
        public string AckPath { get; set; } = "";
    }
    private sealed class PackageMarker
    {
        public string Product { get; set; } = "";
        public string Platform { get; set; } = "";
        public string Version { get; set; } = "";
        public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
}

internal sealed class UpgradeTestHooks
{
    public Func<ProcessStartInfo, Process?>? StartProcess { get; init; }
    public Action? BeforeBackupDelete { get; init; }
}

internal sealed record UpgradeReady(
    string Token,
    int HelperProcessId,
    int ParentProcessId,
    DateTimeOffset ParentStartTimeUtc,
    string ParentExecutablePath,
    string ParentExecutableSha256);
