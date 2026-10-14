using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.UiTests;

public sealed class LeftPaneTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "todokun-ui-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private (MainWindow Window, MainViewModel Vm) Open(bool custom = false, params string[] days)
    {
        var store = new DayStore(_dir);
        foreach (var d in days) store.Write(DateOnly.Parse(d), "x");
        var vm = new MainViewModel(new DayIndex(_dir), store, Today, _dir, custom, new Todokun.Tests.FakeUiServices(), Path.Combine(_dir, "preview"));
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    private static void Click(Button b) => b.Command!.Execute(b.CommandParameter);

    private static List<Button> VisibleDayButtons(Window w) =>
        [.. w.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("day") && b.IsVisible && b.IsEffectivelyVisible)];

    private static List<Button> ListButtons(Window w) =>
        [.. w.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("listitem"))];

    [AvaloniaFact]
    public void Calendar_shows_only_current_month_days_as_buttons()
    {
        var (w, vm) = Open();
        Assert.Equal(31, VisibleDayButtons(w).Count);
        Click(Named<Button>(w, "NextMonth"));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(30, VisibleDayButtons(w).Count);
        Assert.Equal("2026年 11月", Named<TextBlock>(w, "MonthLabel").Text);
    }

    [AvaloniaFact]
    public void List_reflects_month_and_highlights_selection()
    {
        var (w, vm) = Open(false, "2026-10-02", "2026-10-14", "2026-09-30");
        var items = ListButtons(w);
        Assert.Equal(2, items.Count);
        Assert.Equal("10/14 (水)", items[0].Content);
        Assert.Contains("selected", items[0].Classes);
        Assert.DoesNotContain("selected", items[1].Classes);

        Click(Named<Button>(w, "PreviousMonth"));
        Dispatcher.UIThread.RunJobs();
        var sep = ListButtons(w);
        Assert.Single(sep);
        Assert.Equal("9/30 (水)", sep[0].Content);
    }

    [AvaloniaFact]
    public void Clicking_a_list_item_opens_that_day_and_switches_highlight()
    {
        var (w, vm) = Open(false, "2026-10-02", "2026-10-14");
        Click(ListButtons(w)[1]);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new DateOnly(2026, 10, 2), vm.LeftPane.Cursor.SelectedDay);
        var items = ListButtons(w);
        Assert.Contains("selected", items[1].Classes);
        Assert.DoesNotContain("selected", items[0].Classes);
    }

    [AvaloniaFact]
    public void Clicking_a_calendar_day_opens_it()
    {
        var (w, vm) = Open();
        var btn = VisibleDayButtons(w).First(b => ((CalendarCell)b.DataContext!).Day == new DateOnly(2026, 10, 3));
        Click(btn);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new DateOnly(2026, 10, 3), vm.LeftPane.Cursor.SelectedDay);
    }

    [AvaloniaFact]
    public void Dot_is_shown_only_for_days_with_files()
    {
        var (w, _) = Open(false, "2026-10-12");
        var dots = w.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Where(e => e.Classes.Contains("dot") && e.IsVisible).ToList();
        Assert.Single(dots);
    }

    [AvaloniaFact]
    public void Splitter_has_min_width_that_fits_the_calendar()
    {
        var (w, _) = Open();
        var cells = Named<ItemsControl>(w, "Cells");
        // 7 × 36 の月グリッド（余白込み）が、最小幅に収まる
        cells.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        // DesiredSize は余白（8 + 8）を含む
        Assert.True(cells.DesiredSize.Width >= 7 * 36 + 16 - 0.5);
        Assert.True(cells.DesiredSize.Width <= MainWindow.LeftPaneMinWidth + 0.5);
        var root = Named<Grid>(w, "Root");
        Assert.Equal(MainWindow.LeftPaneMinWidth, root.ColumnDefinitions[0].MinWidth);
        Assert.NotNull(w.GetVisualDescendants().OfType<GridSplitter>().FirstOrDefault());
    }

    [AvaloniaFact]
    public void Title_shows_data_root_only_for_custom_root()
    {
        var (w1, _) = Open(false);
        Assert.Equal("todokun", w1.Title);
        var (w2, _) = Open(true);
        Assert.Contains(_dir, w2.Title);
    }

    [AvaloniaFact]
    public void Follows_light_and_dark()
    {
        var (w, _) = Open();
        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Light;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#FFFFFF"), ((ISolidColorBrush)w.Background!).Color);
        app.RequestedThemeVariant = ThemeVariant.Dark;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.Parse("#1E1E1E"), ((ISolidColorBrush)w.Background!).Color);
        app.RequestedThemeVariant = ThemeVariant.Default;
    }

    /// <summary>目視確認用：環境変数 TODOKUN_SHOT_DIR があるときだけ、ライト・ダークの画像を書き出す。</summary>
    [AvaloniaFact]
    public void Screenshots_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("TODOKUN_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        var (w, vm) = Open(false, "2026-10-01", "2026-10-02", "2026-10-09", "2026-10-12", "2026-10-13", "2026-10-14", "2026-09-30");
        var app = Application.Current!;
        foreach (var (name, v) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            app.RequestedThemeVariant = v;
            Dispatcher.UIThread.RunJobs();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
#pragma warning disable CS0618
            w.CaptureRenderedFrame()!.Save(Path.Combine(dir, $"left-{name}.png"));
#pragma warning restore CS0618
        }
        app.RequestedThemeVariant = ThemeVariant.Default;
    }
}
