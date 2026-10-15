using System.Text;

namespace Todokun.Core;

/// <summary>
/// 最後に開いていたメモのタイトルを 1 行で覚える（<c>&lt;cache&gt;/last-memo.txt</c>。LF・BOM なし・アトミック）。
/// マシンごとの、消してよい場所に置く（データルートを汚さない）。書けない・読めないときは黙って諦める
/// （ログに理由だけ。メモの本文・タイトルは書かない）。
/// </summary>
public sealed class MemoLastOpened(string path)
{
    private string? _lastWritten;

    public string Path { get; } = path;

    /// <summary>覚えているタイトル。無い・空・読めないなら null。</summary>
    public string? Read()
    {
        try
        {
            if (!File.Exists(Path)) return null;
            var first = File.ReadAllText(Path, Encoding.UTF8).Split('\n')[0].Trim();
            return first.Length == 0 ? null : first;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>覚える。同じタイトルを続けて渡されたら書かない。</summary>
    public void Write(string title)
    {
        if (title == _lastWritten) return;
        try
        {
            AtomicFile.WriteAllText(Path, title + "\n");
            _lastWritten = title;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"最後に開いたメモを覚えられない: {ex.Message}");
        }
    }

    /// <summary>忘れる（開いているメモが無くなったとき）。無ければ何もしない。</summary>
    public void Clear()
    {
        _lastWritten = null;
        try
        {
            if (File.Exists(Path)) File.Delete(Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"最後に開いたメモを忘れられない: {ex.Message}");
        }
    }
}
