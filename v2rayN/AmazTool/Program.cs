namespace AmazTool;

using System.IO.Pipes;
using System.Text.Json;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && string.Equals(args[0], "qcc-upgrade", StringComparison.Ordinal))
        {
            try
            {
                using var readyPipe = new AnonymousPipeClientStream(PipeDirection.Out, args[2]);
                using var writer = new StreamWriter(readyPipe) { AutoFlush = true };
                return UpgradeApp.ExecuteWithReady(args[1], null, ready => writer.WriteLine(JsonSerializer.Serialize(ready)));
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"QCC upgrade failed: {ex.Message}");
                return 1;
            }
        }

        if (args.Length == 1 && string.Equals(args[0], "rebootas", StringComparison.OrdinalIgnoreCase))
        {
            Utils.StartV2RayN();
            return 0;
        }

        Console.Error.WriteLine("Usage: AmazTool qcc-upgrade <absolute-instruction.json> <inherited-ready-pipe>");
        Console.Error.WriteLine("Legacy ZIP arguments are intentionally rejected.");
        return 64;
    }
}
