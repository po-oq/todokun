using System.Text;

namespace Todokun.Core;

/// <summary>一覧の 1 件。<paramref name="ModifiedUtc"/> はファイルの更新日時、<paramref name="Snippet"/> は先頭行の抜粋。</summary>
public sealed record MemoEntry(string Title, DateTime ModifiedUtc, string Snippet);

/// <summary>メモの一覧（更新が新しい順）。起動時に走査し、追加・削除・改名・保存で更新する。外のファイルの変更は監視しない。</summary>
public sealed class MemoIndex
{
    public const string UnreadableSnippet = "（読めません）";
    private const int SnippetBytes = 4096;
    private const int SnippetChars = 80;

    // 厳密な UTF-8。不正なバイトを置き換えず、例外にする。
    private static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);

    private static readonly string[] ListMarks = ["- [ ]", "- [x]", "- [X]", "* [ ]", "* [x]", "* [X]", "- ", "* ", "+ ", "> "];

    private readonly MemoLibrary _library;
    private readonly List<MemoEntry> _items = [];

    public event Action? Changed;

    public MemoIndex(MemoLibrary library)
    {
        _library = library;
        foreach (var title in library.EnumerateTitles())
        {
            if (!Contains(title)) _items.Add(ReadEntry(title));
        }
        Sort();
    }

    public int Count => _items.Count;

    /// <summary>更新が新しい順（同時刻はタイトルの大文字小文字を無視した順）。</summary>
    public IReadOnlyList<MemoEntry> Items => _items;

    public bool Contains(string title) => Find(title) is not null;

    public MemoEntry? Find(string title) => _items.FirstOrDefault(e => Same(e.Title, title));

    /// <summary>ファイルを読み直して加える（作成・外で増えたメモ）。すでにあれば置き換える。</summary>
    public void Add(string title)
    {
        Replace(ReadEntry(title));
        Changed?.Invoke();
    }

    public void Remove(string title)
    {
        if (_items.RemoveAll(e => Same(e.Title, title)) > 0) Changed?.Invoke();
    }

    /// <summary>改名。更新日時・抜粋は変えない（並びが動かない）。元が一覧に無ければ読み直して足す。</summary>
    public void Rename(string oldTitle, string newTitle)
    {
        var old = Find(oldTitle);
        _items.RemoveAll(e => Same(e.Title, oldTitle));
        Replace(old is null ? ReadEntry(newTitle) : old with { Title = newTitle });
        Changed?.Invoke();
    }

    /// <summary>保存した後に呼ぶ。更新日時（ファイルから）と抜粋（保存した本文から）を直す。一覧に無ければ足す。</summary>
    public void Touch(string title, string text)
    {
        Replace(new MemoEntry(title, ModifiedOf(title), MakeSnippet(text)));
        Changed?.Invoke();
    }

    /// <summary>
    /// タイトルまたは本文に <paramref name="query"/> を含むもの（大文字小文字を無視）。空なら全件。
    /// <paramref name="readBody"/> は本文を返す（読めなければ null＝不一致）。タイトルが一致すれば本文は読まない。
    /// </summary>
    public IReadOnlyList<MemoEntry> Search(string query, Func<string, string?> readBody)
    {
        var q = query.Trim();
        if (q.Length == 0) return [.. _items];
        return
        [
            .. _items.Where(e =>
                e.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (readBody(e.Title)?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
        ];
    }

    /// <summary>先頭の空でない行から、見出しの <c>#</c>・箇条書き・チェックボックスの印を除いたもの。</summary>
    public static string MakeSnippet(string text)
    {
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim().TrimStart('#').TrimStart();
            foreach (var mark in ListMarks)
            {
                if (!line.StartsWith(mark, StringComparison.Ordinal)) continue;
                line = line[mark.Length..].TrimStart();
                break;
            }
            if (line.Length > 0) return MemoTitle.Truncate(line, SnippetChars);
        }
        return "";
    }

    private static bool Same(string a, string b) =>
        string.Equals(a.Normalize(NormalizationForm.FormC), b.Normalize(NormalizationForm.FormC), StringComparison.OrdinalIgnoreCase);

    private void Replace(MemoEntry entry)
    {
        _items.RemoveAll(e => Same(e.Title, entry.Title));
        _items.Add(entry);
        Sort();
    }

    private void Sort() =>
        _items.Sort((a, b) =>
        {
            var byTime = b.ModifiedUtc.CompareTo(a.ModifiedUtc);
            return byTime != 0 ? byTime : StringComparer.OrdinalIgnoreCase.Compare(a.Title, b.Title);
        });

    private DateTime ModifiedOf(string title)
    {
        try
        {
            return File.GetLastWriteTimeUtc(_library.PathOf(title));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.UtcNow;
        }
    }

    private MemoEntry ReadEntry(string title) => new(title, ModifiedOf(title), ReadSnippet(_library.PathOf(title)));

    /// <summary>先頭 4 KB だけ読む。境目で切れた多バイト文字は持ち越すだけで、読めないとは誤らない。</summary>
    private static string ReadSnippet(string path)
    {
        try
        {
            var buffer = new byte[SnippetBytes];
            int read;
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                read = fs.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            var start = read >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
            var chars = new char[Strict.GetMaxCharCount(read) + 1];
            var n = Strict.GetDecoder().GetChars(buffer, start, read - start, chars, 0, flush: false);
            return MakeSnippet(new string(chars, 0, n));
        }
        catch (Exception ex) when (ex is DecoderFallbackException or IOException or UnauthorizedAccessException)
        {
            return UnreadableSnippet;
        }
    }
}
