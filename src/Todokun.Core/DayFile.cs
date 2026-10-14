using System.Globalization;

namespace Todokun.Core;

/// <summary>日ファイルのパス：<c>&lt;root&gt;/yyyy/MM/yyyy-MM-dd.md</c>（要件 6 章）。</summary>
public static class DayFile
{
    public static string PathOf(string root, DateOnly day) =>
        Path.Combine(root,
            day.Year.ToString("0000", CultureInfo.InvariantCulture),
            day.Month.ToString("00", CultureInfo.InvariantCulture),
            day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md");

    /// <summary>
    /// パスが日ファイルの形に一致するか。年・月のフォルダが 4 桁・2 桁で、ファイル名の年月と一致するものだけ。
    /// 一時ファイル（<c>.md.&lt;guid&gt;.tmp</c>）や他の拡張子は一致しない。
    /// </summary>
    public static bool TryParse(string root, string fullPath, out DateOnly day)
    {
        day = default;
        var rel = Path.GetRelativePath(root, fullPath);
        var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts.Length != 3) return false;
        var (yDir, mDir, name) = (parts[0], parts[1], parts[2]);
        if (yDir.Length != 4 || mDir.Length != 2 || !name.EndsWith(".md", StringComparison.Ordinal)) return false;
        if (!DateOnly.TryParseExact(name[..^3], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return false;
        if (name.Length != 13) return false;   // yyyy-MM-dd.md 以外の揺れ（空白・符号）を除く
        if (!int.TryParse(yDir, NumberStyles.None, CultureInfo.InvariantCulture, out var y) || y != d.Year) return false;
        if (!int.TryParse(mDir, NumberStyles.None, CultureInfo.InvariantCulture, out var m) || m != d.Month) return false;
        day = d;
        return true;
    }
}
