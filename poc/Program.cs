using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Avalonia;

namespace Poc;

internal static class Program
{
    public static string[] Args = [];

    [STAThread]
    public static int Main(string[] args)
    {
        Args = args;
        // --mutex-hold <秒> / --mutex-try : 名前付き Mutex（計画 1-6 と同じ名前の作り方）
        if (args.Contains("--mutex-hold") || args.Contains("--mutex-try"))
            return MutexTest(args);
        if (args.Contains("--where"))
        {
            Console.WriteLine($"ProcessPath={Environment.ProcessPath}");
            Console.WriteLine($"BaseDirectory={AppContext.BaseDirectory}");
            Console.WriteLine($"Cwd={Environment.CurrentDirectory}");
            return 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();

    public static string? Opt(string name)
    {
        for (var i = 0; i < Args.Length - 1; i++)
            if (Args[i] == name) return Args[i + 1];
        return null;
    }

    private static int MutexTest(string[] args)
    {
        var root = Path.GetFullPath(args[^1]).TrimEnd(Path.DirectorySeparatorChar).ToLowerInvariant();
        var name = "todokun-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(root)))[..16].ToLowerInvariant();
        try
        {
            using var m = new Mutex(false, name, out var createdNew);
            var got = m.WaitOne(0);
            Console.WriteLine($"name={name} createdNew={createdNew} acquired={got}");
            if (args.Contains("--mutex-hold") && got)
            {
                var sec = int.Parse(args[Array.IndexOf(args, "--mutex-hold") + 1]);
                Thread.Sleep(sec * 1000);
                m.ReleaseMutex();
            }
            return got ? 0 : 3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EXCEPTION {ex.GetType().Name}: {ex.Message}");
            return 4;
        }
    }
}
