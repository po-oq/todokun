namespace Todokun.Core;

/// <summary>起動時コピーの結果（計画 11.2）。</summary>
public abstract record StartupCopyResult
{
    private StartupCopyResult() { }
    public sealed record Copied(DateOnly From) : StartupCopyResult;
    /// <summary>今日のファイルが既にある（読めなくても上書きしない）。</summary>
    public sealed record NotNeeded : StartupCopyResult;
    /// <summary>元になるファイルが無い。</summary>
    public sealed record NoSource : StartupCopyResult;
    public sealed record Failed(string Reason) : StartupCopyResult;
}

/// <summary>起動時に、今日より前の最新の（空白だけでない）日ファイルを、今日のファイルとして複製する（要件 7 章）。</summary>
public static class StartupCopy
{
    public static StartupCopyResult Run(DateOnly today, DayStore store, DayIndex index)
    {
        if (store.Exists(today)) return new StartupCopyResult.NotNeeded();

        var unreadable = 0;
        foreach (var day in index.Before(today).ToList())
        {
            switch (store.Read(day))
            {
                case DayContent.Loaded loaded when !string.IsNullOrWhiteSpace(loaded.Text):
                    try
                    {
                        store.Write(today, loaded.Text);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        return new StartupCopyResult.Failed($"今日のファイルを作れません：{ex.Message}");
                    }
                    index.Add(today);
                    return new StartupCopyResult.Copied(day);
                case DayContent.Failed:
                    unreadable++;
                    break;
            }
        }
        return unreadable > 0
            ? new StartupCopyResult.Failed($"前の日のファイルが読めないため、コピーできませんでした（読めないファイル {unreadable} 件）")
            : new StartupCopyResult.NoSource();
    }
}
