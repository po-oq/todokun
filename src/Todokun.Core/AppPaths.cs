namespace Todokun.Core;

/// <summary>データルートとキャッシュ置き場（要件 3 章）。</summary>
public sealed record AppPaths(string ExeDir, string DataRoot, string CacheRoot, bool IsCustomDataRoot)
{
    /// <summary>exe のフォルダ。<c>Environment.ProcessPath</c> が取れなければ <c>AppContext.BaseDirectory</c>。</summary>
    public static string ExeDirOf(string? processPath, string baseDirectory) =>
        (string.IsNullOrEmpty(processPath) ? null : Path.GetDirectoryName(processPath)) is { Length: > 0 } d ? d : baseDirectory;

    public static AppPaths Resolve(LaunchOptions options, string exeDir)
    {
        var exe = Path.GetFullPath(exeDir);
        var custom = options.DataDir is { Length: > 0 };
        var data = custom ? Path.GetFullPath(options.DataDir!) : Path.Combine(exe, "data");
        return new AppPaths(exe, data, Path.Combine(exe, "cache"), custom);
    }

    /// <summary>
    /// 書き込めるかを、実際に作って確かめる（フォルダを作る→空の一時ファイルを作って消す）。
    /// フォルダが作れない・ファイルを指している・権限が無いときは false と理由。
    /// </summary>
    public static bool ProbeWritable(string dir, out string? reason)
    {
        try
        {
            if (File.Exists(dir))
            {
                reason = "フォルダではなくファイルです";
                return false;
            }
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, $".probe-{Guid.NewGuid():N}.tmp");
            using (File.Create(probe)) { }
            File.Delete(probe);
            reason = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            reason = ex.Message;
            return false;
        }
    }
}
