using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

public class PresentationTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static (TempDir dir, DayIndex index, DayStore store) Make(params string[] days)
    {
        var t = new TempDir();
        var store = new DayStore(t.Path);
        foreach (var d in days) store.Write(DateOnly.Parse(d), "x");
        return (t, new DayIndex(t.Path), store);
    }

    // ── カレンダー ──
    [Fact]
    public void Grid_is_42_cells_starting_on_sunday_and_labels_the_month()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        var cells = vm.Calendar.Cells;
        Assert.Equal(42, cells.Count);
        Assert.Equal(DayOfWeek.Sunday, cells[0].Day.DayOfWeek);
        Assert.Equal(new DateOnly(2026, 9, 27), cells[0].Day);   // 2026-10-01 は木曜
        Assert.Equal("2026年 10月", vm.Calendar.MonthLabel);
        Assert.Equal(31, cells.Count(c => c.InCurrentMonth));
    }

    [Fact]
    public void Adjacent_month_cells_are_disabled_and_do_not_open()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        var requested = new List<DateOnly>();
        vm.Cursor.OpenDayRequested += requested.Add;
        var outside = vm.Calendar.Cells.First(c => !c.InCurrentMonth);
        Assert.False(outside.IsEnabled);
        Assert.True(outside.IsOutside);
        outside.OpenCommand.Execute(null);
        Assert.Empty(requested);
        var inside = vm.Calendar.Cells.First(c => c.InCurrentMonth);
        inside.OpenCommand.Execute(null);
        Assert.Equal([inside.Day], requested);
    }

    [Fact]
    public void Dots_follow_files_today_and_selected_flags()
    {
        var (t, index, _) = Make("2026-10-12", "2026-10-14", "2026-09-30");
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        CalendarCell Cell(string d) => vm.Calendar.Cells.First(c => c.Day == DateOnly.Parse(d));
        Assert.True(Cell("2026-10-12").HasFile);
        Assert.False(Cell("2026-10-13").HasFile);
        Assert.True(Cell("2026-10-14").IsToday);
        Assert.True(Cell("2026-10-14").IsSelected);
        Assert.False(Cell("2026-10-12").IsSelected);
        Assert.True(Cell("2026-09-30").HasFile);   // 前月の端にも点の情報はある（表示は View が出さない）
    }

    [Fact]
    public void Weekday_flags_for_colors()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        Assert.True(vm.Calendar.Cells[0].IsSunday);
        Assert.True(vm.Calendar.Cells[6].IsSaturday);
        Assert.False(vm.Calendar.Cells[3].IsSunday || vm.Calendar.Cells[3].IsSaturday);
    }

    [Fact]
    public void Month_navigation_changes_displayed_month_but_not_selection()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        vm.Calendar.NextMonthCommand.Execute(null);
        Assert.Equal("2026年 11月", vm.Calendar.MonthLabel);
        Assert.Equal(Today, vm.Cursor.SelectedDay);
        Assert.Equal(30, vm.Calendar.Cells.Count(c => c.InCurrentMonth));
        Assert.DoesNotContain(vm.Calendar.Cells, c => c.IsSelected);   // 選択が月外のとき強調なし
        vm.Calendar.PreviousMonthCommand.Execute(null);
        vm.Calendar.PreviousMonthCommand.Execute(null);
        Assert.Equal("2026年 9月", vm.Calendar.MonthLabel);
    }

    [Fact]
    public void Year_boundary_navigation()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, new DateOnly(2026, 12, 31));
        vm.Calendar.NextMonthCommand.Execute(null);
        Assert.Equal("2027年 1月", vm.Calendar.MonthLabel);
    }

    [Fact]
    public void Index_changes_rebuild_dots_and_list()
    {
        var (t, index, _) = Make();
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        var d = new DateOnly(2026, 10, 3);
        Assert.False(vm.Calendar.Cells.First(c => c.Day == d).HasFile);
        Assert.Empty(vm.List.Items);
        index.Add(d);
        Assert.True(vm.Calendar.Cells.First(c => c.Day == d).HasFile);
        Assert.Single(vm.List.Items);
        index.Remove(d);
        Assert.False(vm.Calendar.Cells.First(c => c.Day == d).HasFile);
        Assert.Empty(vm.List.Items);
    }

    // ── 一覧 ──
    [Fact]
    public void List_is_descending_with_weekday_text_and_selected_flag()
    {
        var (t, index, _) = Make("2026-10-02", "2026-10-14", "2026-10-12", "2026-09-30");
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        Assert.Equal(["10/14 (水)", "10/12 (月)", "10/2 (金)"], vm.List.Items.Select(i => i.Text));
        Assert.Equal([true, false, false], vm.List.Items.Select(i => i.IsSelected));
        Assert.Equal("10月のTODO（新しい順）", vm.List.Header);
    }

    [Fact]
    public void List_follows_displayed_month()
    {
        var (t, index, _) = Make("2026-10-14", "2026-09-30", "2026-09-01");
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        vm.Calendar.PreviousMonthCommand.Execute(null);
        Assert.Equal(["9/30 (水)", "9/1 (火)"], vm.List.Items.Select(i => i.Text));
        Assert.All(vm.List.Items, i => Assert.False(i.IsSelected));
    }

    [Fact]
    public void List_item_click_only_requests()
    {
        var (t, index, _) = Make("2026-10-12", "2026-10-14");
        using var _t = t;
        var vm = new LeftPaneViewModel(index, Today);
        var requested = new List<DateOnly>();
        vm.Cursor.OpenDayRequested += requested.Add;
        vm.List.Items[1].OpenCommand.Execute(null);
        Assert.Equal([new DateOnly(2026, 10, 12)], requested);
        Assert.Equal(Today, vm.Cursor.SelectedDay);   // 確定は持ち主
    }

    // ── MainViewModel ──
    [Fact]
    public async Task Main_confirms_requests_and_moves_displayed_month()
    {
        var (t, index, _) = Make("2026-09-30");
        using var _t = t;
        var vm = new MainViewModel(index, new DayStore(t.Path), Today, t.Path, true, new FakeUiServices(), t.Combine("preview"));
        Assert.True(await vm.OpenDayAsync(new DateOnly(2026, 9, 30)));
        Assert.Equal(new DateOnly(2026, 9, 30), vm.LeftPane.Cursor.SelectedDay);
        Assert.Equal("2026年 9月", vm.LeftPane.Calendar.MonthLabel);
    }

    [Fact]
    public void Title_shows_data_root_only_when_custom()
    {
        var (t, index, _) = Make();
        using var _t = t;
        Assert.Equal("todokun", new MainViewModel(index, new DayStore("/x/data"), Today, "/x/data", false, new FakeUiServices(), "/x/p").WindowTitle);
        Assert.Equal("todokun — /x/data", new MainViewModel(index, new DayStore("/x/data"), Today, "/x/data", true, new FakeUiServices(), "/x/p").WindowTitle);
    }
}
