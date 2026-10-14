namespace Todokun.Core;

/// <summary>「ファイルがある日」の一覧（計画 1-4）。起動時に全走査し、保存・削除・コピーで更新する。</summary>
public sealed class DayIndex
{
    private readonly SortedSet<DateOnly> _days = [];

    public event Action? Changed;

    public DayIndex(string root)
    {
        try
        {
            if (!Directory.Exists(root)) return;
            foreach (var file in Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories))
            {
                if (DayFile.TryParse(root, file, out var day)) _days.Add(day);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 列挙できなかったところまでを採用する（落とさない）
        }
    }

    public int Count => _days.Count;

    public bool Contains(DateOnly day) => _days.Contains(day);

    /// <summary>その月の日。降順（新しい日が先）。</summary>
    public IReadOnlyList<DateOnly> DaysIn(int year, int month) =>
        [.. _days.Where(d => d.Year == year && d.Month == month).Reverse()];

    /// <summary>今より前の、ファイルがある日（降順）。</summary>
    public IEnumerable<DateOnly> Before(DateOnly day) => _days.Where(d => d < day).Reverse();

    /// <summary><paramref name="day"/> より前の最も近い日。無ければ null。<paramref name="day"/> 自体はファイルが無くてもよい。</summary>
    public DateOnly? Previous(DateOnly day)
    {
        if (_days.Count == 0 || _days.Min >= day) return null;
        return _days.GetViewBetween(_days.Min, day.AddDays(-1)).Max;
    }

    public DateOnly? Next(DateOnly day)
    {
        if (_days.Count == 0 || _days.Max <= day) return null;
        return _days.GetViewBetween(day.AddDays(1), _days.Max).Min;
    }

    public void Add(DateOnly day)
    {
        if (_days.Add(day)) Changed?.Invoke();
    }

    public void Remove(DateOnly day)
    {
        if (_days.Remove(day)) Changed?.Invoke();
    }
}
