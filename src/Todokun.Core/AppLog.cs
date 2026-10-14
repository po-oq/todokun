namespace Todokun.Core;

/// <summary>
/// <c>&lt;exe&gt;/cache/logs/app.log</c> への簡易ログ（要件 12 章）。書けなくてもアプリは止めない。
/// <b>TODO の本文は書かない</b>（呼ぶ側が、パス・種類・例外のメッセージだけを渡す）。
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();
    private static string? _path;

    public static void Init(string cacheRoot) => _path = Path.Combine(cacheRoot, "logs", "app.log");

    public static void Write(string message)
    {
        if (_path is null) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                File.AppendAllText(_path, $"{DateTimeOffset.Now:o} {message}{Environment.NewLine}");
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
