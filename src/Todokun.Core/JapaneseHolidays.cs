namespace Todokun.Core;

/// <summary>
/// 日本の国民の祝日を日付から計算する（純粋関数。ネットワーク・ファイルに頼らない）。
/// 対象は祝日法の施行後（1948 年〜）。春分・秋分は天文計算の近似式で 1900〜2150 年に対応する。
/// 法改正・特例が入ったら、下の「年ごとの規則」か <see cref="OneOff"/> を 1 行足す。
/// </summary>
public static class JapaneseHolidays
{
    private const int FirstYear = 1948;

    /// <summary>その日が祝日なら名前（振替休日・国民の休日を含む）、そうでなければ null。</summary>
    public static string? GetName(DateOnly day)
    {
        if (day.Year < FirstYear) return null;
        var name = BaseName(day);
        if (name is not null) return name;
        if (IsSubstitute(day)) return "振替休日";
        if (IsBridgeDay(day)) return "国民の休日";
        return null;
    }

    public static bool IsHoliday(DateOnly day) => GetName(day) is not null;

    // 1 回きりの祝日（法に基づく特例）。
    private static readonly Dictionary<DateOnly, string> OneOff = new()
    {
        [new(1959, 4, 10)] = "皇太子明仁親王の結婚の儀",
        [new(1989, 2, 24)] = "昭和天皇の大喪の礼",
        [new(1990, 11, 12)] = "即位礼正殿の儀",
        [new(1993, 6, 9)] = "皇太子徳仁親王の結婚の儀",
        [new(2019, 5, 1)] = "休日（天皇の即位）",
        [new(2019, 10, 22)] = "即位礼正殿の儀",
    };

    /// <summary>振替休日・国民の休日を除いた、その日自体が祝日である日の名前。</summary>
    private static string? BaseName(DateOnly d)
    {
        if (OneOff.TryGetValue(d, out var one)) return one;
        var y = d.Year;
        var m = d.Month;
        var n = d.Day;
        switch (m)
        {
            case 1:
                if (n == 1) return "元日";
                if (y >= 2000 ? IsNthMonday(d, 2) : y >= 1949 && n == 15) return "成人の日";
                return null;
            case 2:
                if (n == 11 && y >= 1967) return "建国記念の日";
                if (n == 23 && y >= 2020) return "天皇誕生日";
                return null;
            case 3:
                return n == SpringEquinoxDay(y) ? "春分の日" : null;
            case 4:
                if (n == 29)
                {
                    if (y >= 2007) return "昭和の日";
                    if (y >= 1989) return "みどりの日";
                    if (y >= 1949) return "天皇誕生日";
                }
                return null;
            case 5:
                if (n == 3 && y >= 1949) return "憲法記念日";
                if (n == 4 && y >= 2007) return "みどりの日";
                if (n == 5 && y >= 1949) return "こどもの日";
                return null;
            case 7:
                // 2020・2021 年は東京五輪で、海の日・スポーツの日が 7 月内で移った
                if (y == 2020 && n == 24 || y == 2021 && n == 23) return "スポーツの日";
                if (y == 2021) return n == 22 ? "海の日" : null;
                if (y == 2020) return n == 23 ? "海の日" : null;
                if (y >= 2003) return IsNthMonday(d, 3) ? "海の日" : null;
                return y >= 1996 && n == 20 ? "海の日" : null;
            case 8:
                if (y == 2021) return n == 8 ? "山の日" : null;
                if (y == 2020) return n == 10 ? "山の日" : null;
                return y >= 2016 && n == 11 ? "山の日" : null;
            case 9:
                if (n == AutumnEquinoxDay(y)) return "秋分の日";
                if (y >= 2003 ? IsNthMonday(d, 3) : y >= 1966 && n == 15) return "敬老の日";
                return null;
            case 10:
                if (y is 2020 or 2021) return null;
                if (y >= 2000 ? IsNthMonday(d, 2) : y >= 1966 && n == 10)
                    return y >= 2022 ? "スポーツの日" : "体育の日";
                return null;
            case 11:
                if (n == 3) return "文化の日";
                if (n == 23) return "勤労感謝の日";
                return null;
            case 12:
                return n == 23 && y is >= 1989 and <= 2018 ? "天皇誕生日" : null;
            default:
                return null;
        }
    }

    private static bool IsNthMonday(DateOnly d, int nth) => d.DayOfWeek == DayOfWeek.Monday && (d.Day - 1) / 7 + 1 == nth;

    /// <summary>日曜の祝日の次の「祝日でない日」。1973-04-12 から（〜2006 は翌日が月曜のときだけ）。</summary>
    private static bool IsSubstitute(DateOnly d)
    {
        if (d < new DateOnly(1973, 4, 12) || d.DayOfWeek == DayOfWeek.Sunday) return false;
        if (d.Year < 2007)
            return d.DayOfWeek == DayOfWeek.Monday && BaseName(d.AddDays(-1)) is not null;
        // 2007〜：直前から続く祝日の並びの中に日曜があれば、並びの次の日が振替休日
        for (var x = d.AddDays(-1); BaseName(x) is not null; x = x.AddDays(-1))
            if (x.DayOfWeek == DayOfWeek.Sunday) return true;
        return false;
    }

    /// <summary>前日と翌日がどちらも祝日に挟まれた平日（1985 年〜）。</summary>
    private static bool IsBridgeDay(DateOnly d) =>
        d.Year >= 1985 && d.DayOfWeek != DayOfWeek.Sunday
        && BaseName(d.AddDays(-1)) is not null && BaseName(d.AddDays(1)) is not null;

    private static int SpringEquinoxDay(int y) => EquinoxDay(y, y < 1980 ? 20.8357 : y < 2100 ? 20.8431 : 21.8510);

    private static int AutumnEquinoxDay(int y) => EquinoxDay(y, y < 1980 ? 23.2588 : y < 2100 ? 23.2488 : 24.2488);

    // 春分・秋分の近似式（1900〜2150 年）：floor(c + 0.242194 (y - 1980) - floor((y - base) / 4))。base は 1980 年以降が 1980、それ以前は 1983
    private static int EquinoxDay(int y, double c) =>
        (int)Math.Floor(c + 0.242194 * (y - 1980) - Math.Floor((y - (y < 1980 ? 1983 : 1980)) / 4.0));
}
