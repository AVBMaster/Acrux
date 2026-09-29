using System.Runtime.InteropServices;

namespace UpBrowser;

class Program
{
    static async Task Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = (Exception)e.ExceptionObject;
            Log($"[Main] UnhandledException: {ex.GetType().FullName}: {ex.Message}");
            Log(ex.StackTrace ?? "[Main] No stack trace");
        };

        if (args.Length > 0 && (args[0] == "--snapshot" || args[0] == "--diff" || args[0] == "--dumplayout"
            || args[0] == "--textops" || args[0] == "--pixels" || args[0] == "--anim" || args[0] == "--rows"))
        {
            Environment.ExitCode = SnapshotCli.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--frameshot")
        {
            Environment.ExitCode = FrameSnapshotCli.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--tab-host")
        {
            Environment.ExitCode = UpBrowser.Process.TabHostApp.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--idletest")
        {
            Environment.ExitCode = IdleResourceTest.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--scrollstress")
        {
            Environment.ExitCode = ScrollStressTest.Run(args);
            return;
        }

        if (args.Length > 0 && args[0] == "--proctest")
        {
            Environment.ExitCode = ProcessTabSelfTest.Run();
            return;
        }

        if (args.Length > 0 && args[0] == "--tabtest")
        {
            Environment.ExitCode = TabConcurrencySelfTest.Run();
            return;
        }

        Log("[Main] Starting UpBrowser");
        Log($"[Main] OS: {Environment.OSVersion.VersionString}");
        Log($"[Main] Platform: {RuntimeInformation.OSDescription}");
        Log($"[Main] Arch: {RuntimeInformation.OSArchitecture}");

        try
        {
            Log("[Main] Creating BrowserApp...");
            string? startupUrl = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : null;
            var app = new BrowserApp(1024, 768, startupUrl);
            Log("[Main] BrowserApp created successfully");

            Log("[Main] Starting RunAsync...");
            await app.RunAsync();
            Log("[Main] RunAsync completed");
        }
        catch (Exception ex)
        {
            Log($"[Main] CRASH in managed code: {ex.GetType().FullName}: {ex.Message}");
            Log(ex.StackTrace ?? "[Main] No stack trace");
        }
        finally
        {
            Log("[Main] Cleanup complete");
        }
    }

    private static void Log(string msg)
    {
        Console.WriteLine(msg);
        try
        {
            File.AppendAllText("upbrowser_startup.log", $"{msg}\n");
        }
        catch { }
    }
}
