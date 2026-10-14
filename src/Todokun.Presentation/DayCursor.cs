using CommunityToolkit.Mvvm.ComponentModel;

namespace Todokun.Presentation;

/// <summary>
/// 選択中の日と、左ペインに表示している月（計画 11.3）。カレンダー・一覧は選択を直接書き換えず、
/// <see cref="RequestOpenDay"/> で「要求」を出す（要求 → 確認 → 確定。確認は P5 の持ち主が行う）。
/// </summary>
public sealed partial class DayCursor : ObservableObject
{
    [ObservableProperty]
    private DateOnly _selectedDay;

    /// <summary>表示している月の 1 日。</summary>
    [ObservableProperty]
    private DateOnly _displayedMonth;

    public DayCursor(DateOnly today)
    {
        _selectedDay = today;
        _displayedMonth = FirstOfMonth(today);
    }

    /// <summary>日を開く要求（カレンダーの日・一覧の項目・左右キー）。確定は持ち主が <see cref="Show"/> で行う。</summary>
    public event Action<DateOnly>? OpenDayRequested;

    public void RequestOpenDay(DateOnly day) => OpenDayRequested?.Invoke(day);

    /// <summary>確定：選択をその日にし、表示月もその日の月にする。</summary>
    public void Show(DateOnly day)
    {
        SelectedDay = day;
        DisplayedMonth = FirstOfMonth(day);
    }

    /// <summary>◀▶：表示月だけを変える（選択は変えない）。</summary>
    public void ChangeMonth(int delta) => DisplayedMonth = DisplayedMonth.AddMonths(delta);

    public static DateOnly FirstOfMonth(DateOnly d) => new(d.Year, d.Month, 1);
}
