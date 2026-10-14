using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Todokun.Core;

namespace Todokun.Presentation;

public sealed class DayListItem(DateOnly day, bool isSelected, Action<DateOnly> open)
{
    public DateOnly Day { get; } = day;
    /// <summary>「10/14 (水)」。曜日は配列で（culture に依らない）。</summary>
    public string Text { get; } = DayListViewModel.Format(day);
    public bool IsSelected { get; } = isSelected;
    public IRelayCommand OpenCommand { get; } = new RelayCommand(() => open(day));
}

/// <summary>表示月の、ファイルがある日の一覧（降順。要件 8.2、計画 11.3）。</summary>
public sealed partial class DayListViewModel : ObservableObject
{
    private static readonly string[] Weekdays = ["日", "月", "火", "水", "木", "金", "土"];

    private readonly DayCursor _cursor;
    private readonly DayIndex _index;

    [ObservableProperty]
    private IReadOnlyList<DayListItem> _items = [];

    [ObservableProperty]
    private string _header = "";

    public DayListViewModel(DayCursor cursor, DayIndex index)
    {
        _cursor = cursor;
        _index = index;
        cursor.PropertyChanged += OnCursorChanged;
        index.Changed += Rebuild;
        Rebuild();
    }

    public static string Format(DateOnly d) => $"{d.Month}/{d.Day} ({Weekdays[(int)d.DayOfWeek]})";

    private void OnCursorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DayCursor.SelectedDay) or nameof(DayCursor.DisplayedMonth)) Rebuild();
    }

    private void Rebuild()
    {
        var m = _cursor.DisplayedMonth;
        Items = [.. _index.DaysIn(m.Year, m.Month).Select(d => new DayListItem(d, d == _cursor.SelectedDay, _cursor.RequestOpenDay))];
        Header = $"{m.Month}月のTODO（新しい順）";
    }
}
