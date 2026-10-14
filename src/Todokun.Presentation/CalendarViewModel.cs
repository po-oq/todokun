using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>月グリッドの 1 マス。</summary>
public sealed class CalendarCell(DateOnly day, bool inMonth, bool hasFile, bool isToday, bool isSelected, Action<DateOnly> open)
{
    public DateOnly Day { get; } = day;
    public string Text { get; } = day.Day.ToString();
    public bool InCurrentMonth { get; } = inMonth;
    /// <summary>前後の月の日は押せない（要件 8.2）。</summary>
    public bool IsOutside => !InCurrentMonth;
    public bool IsEnabled => InCurrentMonth;
    public bool HasFile { get; } = hasFile;
    public bool IsToday { get; } = isToday;
    /// <summary>選択中の強調。表示月の外の日は強調しない。</summary>
    public bool IsSelected { get; } = isSelected;
    public bool IsSunday => Day.DayOfWeek == DayOfWeek.Sunday;
    public bool IsSaturday => Day.DayOfWeek == DayOfWeek.Saturday;
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => { if (inMonth) open(day); });
}

/// <summary>カレンダー（日曜始まり・6 行 × 7 列。要件 8.2、計画 11.3）。</summary>
public sealed partial class CalendarViewModel : ObservableObject
{
    private readonly DayCursor _cursor;
    private readonly DayIndex _index;
    private readonly Func<DateOnly> _today;

    [ObservableProperty]
    private IReadOnlyList<CalendarCell> _cells = [];

    [ObservableProperty]
    private string _monthLabel = "";

    public CalendarViewModel(DayCursor cursor, DayIndex index, Func<DateOnly> today)
    {
        _cursor = cursor;
        _index = index;
        _today = today;
        cursor.PropertyChanged += OnCursorChanged;
        index.Changed += Rebuild;
        PreviousMonthCommand = new RelayCommand(() => cursor.ChangeMonth(-1));
        NextMonthCommand = new RelayCommand(() => cursor.ChangeMonth(1));
        Rebuild();
    }

    public IRelayCommand PreviousMonthCommand { get; }
    public IRelayCommand NextMonthCommand { get; }

    private void OnCursorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DayCursor.SelectedDay) or nameof(DayCursor.DisplayedMonth)) Rebuild();
    }

    private void Rebuild()
    {
        var month = _cursor.DisplayedMonth;
        var start = month.AddDays(-(int)month.DayOfWeek);
        var today = _today();
        var cells = new CalendarCell[42];
        for (var i = 0; i < cells.Length; i++)
        {
            var d = start.AddDays(i);
            var inMonth = d.Year == month.Year && d.Month == month.Month;
            cells[i] = new CalendarCell(d, inMonth, _index.Contains(d), d == today, inMonth && d == _cursor.SelectedDay, _cursor.RequestOpenDay);
        }
        Cells = cells;
        MonthLabel = $"{month.Year}年 {month.Month}月";
    }
}
