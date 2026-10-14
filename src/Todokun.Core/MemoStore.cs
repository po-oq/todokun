namespace Todokun.Core;

/// <summary>メモ（アプリ全体で 1 つ）の読み書き。<c>&lt;root&gt;/memo.md</c>。日ファイルの走査には混ざらない（<see cref="DayFile.TryParse"/> は 3 階層だけを受ける）。</summary>
public sealed class MemoStore(string root)
{
    public const string FileName = "memo.md";

    public string Root { get; } = root;

    public string Path => System.IO.Path.Combine(Root, FileName);

    public bool Exists() => File.Exists(Path);

    public DayContent Read() => Utf8TextFile.Read(Path);

    /// <summary>LF・BOM なし・アトミックに書く。失敗は例外。</summary>
    public void Write(string text) => Utf8TextFile.Write(Path, text);

    /// <summary>消す。無ければ何もしない。失敗は例外。</summary>
    public void Delete()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}
