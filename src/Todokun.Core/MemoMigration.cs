namespace Todokun.Core;

/// <summary>既存の <c>memo.md</c> の引き継ぎの結果。</summary>
public abstract record MemoMigrationResult
{
    private MemoMigrationResult() { }

    /// <summary><c>memo.md</c> が無い。</summary>
    public sealed record NothingToDo : MemoMigrationResult;

    /// <summary><c>memos/</c> に<paramref name="Title"/>として引き継いだ。<paramref name="OldRemoved"/> が false なら <c>memo.md</c> を消せずに残っている。</summary>
    public sealed record Migrated(string Title, bool OldRemoved) : MemoMigrationResult;

    /// <summary>同じバイト列のメモ（<paramref name="Title"/>）が <c>memos/</c> に既にあった（前回コピー済み）。コピーはしない。</summary>
    public sealed record AlreadyMigrated(string Title, bool OldRemoved) : MemoMigrationResult;

    /// <summary>引き継げなかった。<c>memo.md</c> はそのまま。次の起動でもう一度試す。</summary>
    public sealed record Failed(string Reason) : MemoMigrationResult;

    /// <summary>データルートが書けないので引き継がない（<c>memo.md</c> はある）。</summary>
    public sealed record NotWritable : MemoMigrationResult;
}

/// <summary>
/// 単一メモ <c>&lt;root&gt;/memo.md</c> を <c>memos/メモ.md</c> へ引き継ぐ。判定は「<c>memo.md</c> があるか」だけ（<c>memos/</c> の有無は見ない）。
/// バイト列のまま一時ファイルに書き、<see cref="File.Move(string, string)"/>（上書きなし）で名前を付け、成功した後にだけ元を消す。
/// どの段で止まっても次の起動でやり直せる。
/// </summary>
public static class MemoMigration
{
    public const string TitleForOldMemo = "メモ";

    public static MemoMigrationResult Run(MemoLibrary library, bool dataWritable)
    {
        var oldPath = Path.Combine(library.Root, MemoStore.FileName);
        if (!File.Exists(oldPath)) return new MemoMigrationResult.NothingToDo();
        if (!dataWritable) return new MemoMigrationResult.NotWritable();

        try
        {
            var bytes = File.ReadAllBytes(oldPath);

            var existing = FindSameBytes(library, bytes);
            if (existing is not null)
                return new MemoMigrationResult.AlreadyMigrated(existing, TryDelete(oldPath));

            Directory.CreateDirectory(library.Folder);
            var temp = Path.Combine(library.Folder, $".md.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllBytes(temp, bytes);
                var title = library.UniqueTitle(TitleForOldMemo);
                File.Move(temp, library.PathOf(title));
                return new MemoMigrationResult.Migrated(title, TryDelete(oldPath));
            }
            finally
            {
                TryDelete(temp);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new MemoMigrationResult.Failed(ex.Message);
        }
    }

    /// <summary><c>memos/</c> に同じバイト列のメモがあればそのタイトル。同じ長さのファイルだけを読んで比べる。</summary>
    private static string? FindSameBytes(MemoLibrary library, byte[] bytes)
    {
        foreach (var title in library.EnumerateTitles())
        {
            try
            {
                var path = library.PathOf(title);
                if (new FileInfo(path).Length != bytes.Length) continue;
                if (File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) return title;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 読めないものは同じとみなさない
            }
        }
        return null;
    }

    /// <summary>消せたら true（元から無ければ true）。</summary>
    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return !File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
