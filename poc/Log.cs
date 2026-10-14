using System.Diagnostics;
using System.Text.Json;

namespace Poc;

/// <summary>PoC の記録。1 行 1 JSON（時刻は起動からのミリ秒）。</summary>
public static class Log
{
    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static readonly object Gate = new();
    private static string? _path;

    public static double Now => Clock.Elapsed.TotalMilliseconds;

    public static void Init(string path)
    {
        _path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, "");
    }

    public static void Write(string kind, object? data = null)
    {
        var line = JsonSerializer.Serialize(new { t = Math.Round(Now, 1), kind, data });
        lock (Gate)
        {
            if (_path is not null) File.AppendAllText(_path, line + "\n");
        }
        Console.WriteLine(line);
    }
}
