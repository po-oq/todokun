using System.Text;

namespace Todokun.Core;

/// <summary>メモの読み書き・作成・改名・削除。<c>&lt;root&gt;/memos/&lt;タイトル&gt;.md</c>（タイトル＝ファイル名）。日ファイルの走査には混ざらない。</summary>
public sealed class MemoLibrary(string root)
{
    public const string FolderName = "memos";
    public const string DefaultTitle = "無題のメモ";
    private const string Extension = ".md";

    public string Root { get; } = root;

    public string Folder => System.IO.Path.Combine(Root, FolderName);

    public string PathOf(string title) => System.IO.Path.Combine(Folder, title + Extension);

    public bool Exists(string title) => File.Exists(PathOf(title));

    public DayContent Read(string title) => Utf8TextFile.Read(PathOf(title));

    /// <summary>LF・BOM なし・アトミックに書く（空でも書く）。失敗は例外。</summary>
    public void Write(string title, string text) => Utf8TextFile.Write(PathOf(title), text);

    /// <summary>
    /// 一覧に出るメモのタイトル（NFC）。<c>memos/</c> 直下の <c>*.md</c> だけ（拡張子は大文字小文字を問わない）。
    /// サブフォルダ・<c>.</c> 始まり・<c>.md</c> で終わらない名前（一時ファイル）は無視。フォルダが無い・列挙できないときは空。
    /// </summary>
    public IReadOnlyList<string> EnumerateTitles()
    {
        var titles = new List<string>();
        try
        {
            if (!Directory.Exists(Folder)) return titles;
            foreach (var file in Directory.EnumerateFiles(Folder))
            {
                var name = System.IO.Path.GetFileName(file);
                if (name.StartsWith('.') || !name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase)) continue;
                var title = name[..^Extension.Length];
                if (title.Length > 0) titles.Add(title.Normalize(NormalizationForm.FormC));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 列挙できたところまでを採用する（落とさない）
        }
        return titles;
    }

    /// <summary>
    /// 重複しないタイトル。<paramref name="candidate"/> を正規化し、空なら <see cref="DefaultTitle"/>。
    /// 大文字小文字を無視して <paramref name="except"/>（自分自身）以外と比べ、衝突したら <c>(2)</c>・<c>(3)</c>…。
    /// <c>(n)</c> を足して 100 文字を超えるときは本体のほうを切る。
    /// </summary>
    public string UniqueTitle(string candidate, string? except = null)
    {
        var baseTitle = MemoTitle.Normalize(candidate);
        if (baseTitle.Length == 0) baseTitle = DefaultTitle;
        return Unique(baseTitle, except);
    }

    private string Unique(string baseTitle, string? except)
    {
        var skip = except?.Normalize(NormalizationForm.FormC);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in EnumerateTitles())
        {
            if (skip is null || !string.Equals(t, skip, StringComparison.OrdinalIgnoreCase)) taken.Add(t);
        }

        if (!taken.Contains(baseTitle)) return baseTitle;
        for (var n = 2; ; n++)
        {
            var suffix = $" ({n})";
            var title = MemoTitle.Truncate(baseTitle, MemoTitle.MaxLength - suffix.Length) + suffix;
            if (!taken.Contains(title)) return title;
        }
    }

    /// <summary>空のメモを作り、重複を避けたタイトルを返す。同時に同名が作られたら次の番号へ。失敗は例外。</summary>
    public string Create(string baseTitle)
    {
        Directory.CreateDirectory(Folder);
        while (true)
        {
            var title = UniqueTitle(baseTitle);
            try
            {
                using var _ = new FileStream(PathOf(title), FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return title;
            }
            catch (IOException) when (File.Exists(PathOf(title)))
            {
                // 取り合いに負けた。もう一度、空いている番号を探す。
            }
        }
    }

    /// <summary>
    /// 改名して、確定したタイトルを返す。正規化して空・同じ名前なら何もしない（元のタイトルを返す）。
    /// 衝突したら <c>(2)</c>。上書きはしない。大文字小文字だけの変更も <see cref="File.Move(string, string)"/> を直接使う。
    /// 失敗（元が無い・使用中など）は例外で、元のまま。
    /// </summary>
    public string Rename(string oldTitle, string newTitle)
    {
        var normalized = MemoTitle.Normalize(newTitle);
        if (normalized.Length == 0) return oldTitle;

        var final = Unique(normalized, except: oldTitle);
        if (string.Equals(final, oldTitle.Normalize(NormalizationForm.FormC), StringComparison.Ordinal)) return oldTitle;

        File.Move(PathOf(oldTitle), PathOf(final));
        return final;
    }

    /// <summary>消す。無ければ何もしない。失敗は例外。</summary>
    public void Delete(string title)
    {
        var path = PathOf(title);
        if (File.Exists(path)) File.Delete(path);
    }
}
