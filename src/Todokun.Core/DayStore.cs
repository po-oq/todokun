namespace Todokun.Core;

/// <summary>日ファイルの読み書き（要件 6・12 章、計画 11.8）。</summary>
public sealed class DayStore(string root)
{
    public string Root { get; } = root;

    public string PathOf(DateOnly day) => DayFile.PathOf(Root, day);

    public bool Exists(DateOnly day) => File.Exists(PathOf(day));

    public DayContent Read(DateOnly day) => Utf8TextFile.Read(PathOf(day));

    /// <summary>LF に統一し、BOM なしの UTF-8 で、アトミックに書く。失敗は例外（IOException など）。</summary>
    public void Write(DateOnly day, string text) =>
        Utf8TextFile.Write(PathOf(day), text);

    /// <summary>日ファイルを消す。無ければ何もしない。失敗は例外。</summary>
    public void Delete(DateOnly day)
    {
        var path = PathOf(day);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n");
}
