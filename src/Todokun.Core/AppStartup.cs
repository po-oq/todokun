namespace Todokun.Core;

/// <summary>起動の結果（画面を作る前に決まるもの）。</summary>
public sealed record StartupResult(
    AppPaths Paths,
    SingleInstance Instance,
    bool CacheWritable,
    string? CacheReason,
    bool DataWritable,
    string? DataReason,
    DayIndex Index,
    DayStore Store,
    StartupCopyResult? Copy,
    string? Notice)
{
    /// <summary>同じデータルートで、すでに起動している（画面には案内だけを出して終わる）。</summary>
    public bool AlreadyRunning => Instance.Status == SingleInstanceStatus.AlreadyRunning;
}

/// <summary>起動の順序（計画 11.1）。画面を知らない。</summary>
public static class AppStartup
{
    /// <summary>
    /// 1 <c>AppPaths</c> → 2 <c>AppLog</c> → 3 二重起動の確認 → 4 <c>cache/</c> の書込判定 → 5 <c>data/</c> の書込判定 →
    /// 6 <c>DayIndex</c> の全走査 → データルートが書けるときだけ起動時コピー。
    /// 書けないときはコピーを呼ばず、理由は「書けない」の 1 つだけを <see cref="StartupResult.Notice"/> に入れる。
    /// </summary>
    public static StartupResult Run(LaunchOptions options, string exeDir, DateOnly today)
    {
        var paths = AppPaths.Resolve(options, exeDir);
        AppLog.Init(paths.CacheRoot);

        var instance = SingleInstance.TryAcquire(paths.DataRoot);
        if (instance.Status == SingleInstanceStatus.AlreadyRunning)
            return new StartupResult(paths, instance, true, null, true, null, new DayIndex(""), new DayStore(paths.DataRoot), null, null);
        if (instance.Status == SingleInstanceStatus.Unavailable)
            AppLog.Write($"二重起動の確認を使えない（確認を諦めて起動を続ける）: {instance.Error}");

        var cacheOk = AppPaths.ProbeWritable(paths.CacheRoot, out var cacheReason);
        if (!cacheOk) AppLog.Write($"cache を書けない: {cacheReason}");
        var dataOk = AppPaths.ProbeWritable(paths.DataRoot, out var dataReason);
        if (!dataOk) AppLog.Write($"データルートを書けない: {dataReason}");

        var index = new DayIndex(paths.DataRoot);
        var store = new DayStore(paths.DataRoot);

        StartupCopyResult? copy = null;
        string? notice = null;
        if (!dataOk)
        {
            notice = $"データの保存先に書き込めません：{dataReason}。閲覧はできますが、編集はできません";
        }
        else
        {
            copy = StartupCopy.Run(today, store, index);
            switch (copy)
            {
                case StartupCopyResult.Failed f:
                    notice = $"前の日からのコピーに失敗しました：{f.Reason}";
                    AppLog.Write($"起動時コピーに失敗: {f.Reason}");
                    break;
                case StartupCopyResult.Copied c:
                    AppLog.Write($"起動時コピー: {c.From:yyyy-MM-dd} → {today:yyyy-MM-dd}");
                    break;
            }
        }
        return new StartupResult(paths, instance, cacheOk, cacheReason, dataOk, dataReason, index, store, copy, notice);
    }
}
