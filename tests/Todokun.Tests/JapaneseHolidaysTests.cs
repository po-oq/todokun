using Todokun.Core;

namespace Todokun.Tests;

public class JapaneseHolidaysTests
{
    private static string? Name(int y, int m, int d) => JapaneseHolidays.GetName(new DateOnly(y, m, d));

    // 内閣府の「国民の祝日」一覧と照らした年ごとの全祝日（月, 日）。
    public static TheoryData<int, string> Years => new()
    {
        { 2026, "1/1 1/12 2/11 2/23 3/20 4/29 5/3 5/4 5/5 5/6 7/20 8/11 9/21 9/22 9/23 10/12 11/3 11/23" },
        { 2025, "1/1 1/13 2/11 2/23 2/24 3/20 4/29 5/3 5/4 5/5 5/6 7/21 8/11 9/15 9/23 10/13 11/3 11/23 11/24" },
        { 2024, "1/1 1/8 2/11 2/12 2/23 3/20 4/29 5/3 5/4 5/5 5/6 7/15 8/11 8/12 9/16 9/22 9/23 10/14 11/3 11/4 11/23" },
        { 2023, "1/1 1/2 1/9 2/11 2/23 3/21 4/29 5/3 5/4 5/5 7/17 8/11 9/18 9/23 10/9 11/3 11/23" },
        { 2022, "1/1 1/10 2/11 2/23 3/21 4/29 5/3 5/4 5/5 7/18 8/11 9/19 9/23 10/10 11/3 11/23" },
        { 2021, "1/1 1/11 2/11 2/23 3/20 4/29 5/3 5/4 5/5 7/22 7/23 8/8 8/9 9/20 9/23 11/3 11/23" },
        { 2020, "1/1 1/13 2/11 2/23 2/24 3/20 4/29 5/3 5/4 5/5 5/6 7/23 7/24 8/10 9/21 9/22 11/3 11/23" },
        { 2019, "1/1 1/14 2/11 3/21 4/29 4/30 5/1 5/2 5/3 5/4 5/5 5/6 7/15 8/11 8/12 9/16 9/23 10/14 10/22 11/3 11/4 11/23" },
        { 2018, "1/1 1/8 2/11 2/12 3/21 4/29 4/30 5/3 5/4 5/5 7/16 8/11 9/17 9/23 9/24 10/8 11/3 11/23 12/23 12/24" },
        { 2009, "1/1 1/12 2/11 3/20 4/29 5/3 5/4 5/5 5/6 7/20 9/21 9/22 9/23 10/12 11/3 11/23 12/23" },
        { 2006, "1/1 1/2 1/9 2/11 3/21 4/29 5/3 5/4 5/5 7/17 9/18 9/23 10/9 11/3 11/23 12/23" },
        { 2000, "1/1 1/10 2/11 3/20 4/29 5/3 5/4 5/5 7/20 9/15 9/23 10/9 11/3 11/23 12/23" },
        { 1989, "1/1 1/2 1/15 1/16 2/11 2/24 3/21 4/29 5/3 5/4 5/5 9/15 9/23 10/10 11/3 11/23 12/23" },
    };

    [Theory]
    [MemberData(nameof(Years))]
    public void Lists_exactly_the_holidays_of_the_year(int year, string expected)
    {
        var actual = new List<string>();
        for (var d = new DateOnly(year, 1, 1); d.Year == year; d = d.AddDays(1))
            if (JapaneseHolidays.IsHoliday(d)) actual.Add($"{d.Month}/{d.Day}");
        Assert.Equal(expected, string.Join(' ', actual));
    }

    [Fact]
    public void Names_are_returned_for_each_kind()
    {
        Assert.Equal("元日", Name(2026, 1, 1));
        Assert.Equal("成人の日", Name(2026, 1, 12));
        Assert.Equal("みどりの日", Name(2026, 5, 4));
        Assert.Equal("振替休日", Name(2026, 5, 6));
        Assert.Equal("国民の休日", Name(2026, 9, 22));
        Assert.Equal("秋分の日", Name(2026, 9, 23));
        Assert.Equal("スポーツの日", Name(2021, 7, 23));
        Assert.Equal("即位礼正殿の儀", Name(2019, 10, 22));
        Assert.Null(Name(2026, 1, 2));
    }

    [Fact]
    public void Dates_before_the_holiday_law_are_not_holidays()
    {
        Assert.False(JapaneseHolidays.IsHoliday(new DateOnly(1947, 1, 1)));
        Assert.False(JapaneseHolidays.IsHoliday(new DateOnly(1900, 5, 5)));
    }
}
