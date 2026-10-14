using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>左ペイン（カレンダー＋一覧）。選択と表示月は <see cref="Cursor"/> を共有する。</summary>
public sealed class LeftPaneViewModel
{
    public LeftPaneViewModel(DayIndex index, DateOnly today, Func<DateOnly>? todayProvider = null)
    {
        Cursor = new DayCursor(today);
        Calendar = new CalendarViewModel(Cursor, index, todayProvider ?? (() => today));
        List = new DayListViewModel(Cursor, index);
    }

    public DayCursor Cursor { get; }
    public CalendarViewModel Calendar { get; }
    public DayListViewModel List { get; }
}
