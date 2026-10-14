using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>計画 6-2：起動の流れ（一時フォルダ。exe の置き場も一時フォルダにする）。</summary>
public sealed class AppStartupTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly List<SingleInstance> _instances = [];

    public void Dispose()
    {
        foreach (var i in _instances) i.Dispose();
        _t.Dispose();
    }

    private StartupResult Run(string? dataDir = null, string? exe = null)
    {
        var r = AppStartup.Run(new LaunchOptions(dataDir ?? _t.Combine("data")), exe ?? _t.Combine("exe"), Today);
        _instances.Add(r.Instance);
        return r;
    }

    private void Seed(string day, string text, string? dataDir = null) =>
        new DayStore(dataDir ?? _t.Combine("data")).Write(DateOnly.Parse(day), text);

    [Fact]
    public void Copies_latest_past_day_and_today_is_in_the_index()
    {
        Seed("2026-10-12", "- [ ] a\n");
        var r = Run();
        var copied = Assert.IsType<StartupCopyResult.Copied>(r.Copy);
        Assert.Equal(new DateOnly(2026, 10, 12), copied.From);
        Assert.True(r.Index.Contains(Today));
        Assert.Null(r.Notice);
        Assert.True(r.DataWritable && r.CacheWritable);
    }

    [Fact]
    public void No_source_creates_nothing()
    {
        var r = Run();
        Assert.IsType<StartupCopyResult.NoSource>(r.Copy);
        Assert.False(File.Exists(new DayStore(_t.Combine("data")).PathOf(Today)));
        Assert.Null(r.Notice);
    }

    [Fact]
    public void Today_already_exists_is_not_overwritten()
    {
        Seed("2026-10-14", "mine");
        Seed("2026-10-13", "old");
        var r = Run();
        Assert.IsType<StartupCopyResult.NotNeeded>(r.Copy);
        Assert.Equal("mine", File.ReadAllText(new DayStore(_t.Combine("data")).PathOf(Today)));
    }

    [Fact]
    public void Copy_failure_continues_with_a_notice()
    {
        // 元は読めない（UTF-8 でない）ものだけ
        var store = new DayStore(_t.Combine("data"));
        var p = store.PathOf(new DateOnly(2026, 10, 13));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, [0x82, 0xA0]);
        var r = Run();
        Assert.IsType<StartupCopyResult.Failed>(r.Copy);
        Assert.NotNull(r.Notice);
        Assert.True(r.DataWritable);
    }

    [Fact]
    public void Data_root_that_is_a_file_does_not_crash_and_does_not_copy()
    {
        var f = _t.Combine("data");
        File.WriteAllText(f, "x");
        var r = Run(f);
        Assert.False(r.DataWritable);
        Assert.Null(r.Copy);   // コピーは呼ばれない。理由は「書けない」の 1 つだけ
        Assert.Contains("書き込めません", r.Notice);
        Assert.Equal(0, r.Index.Count);
    }

    [Fact]
    public void Read_only_data_root_is_viewable_but_not_copied()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        Seed("2026-10-12", "old");
        var data = _t.Combine("data");
        File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            var r = Run();
            Assert.False(r.DataWritable);
            Assert.Null(r.Copy);
            Assert.NotNull(r.Notice);
            Assert.True(r.Index.Contains(new DateOnly(2026, 10, 12)));   // 閲覧はできる
            Assert.False(r.Index.Contains(Today));
        }
        finally
        {
            File.SetUnixFileMode(data, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void Unwritable_cache_is_reported_but_data_is_still_usable()
    {
        var exe = _t.Combine("exe");
        Directory.CreateDirectory(exe);
        File.WriteAllText(Path.Combine(exe, "cache"), "x");   // cache がファイル
        var r = Run(exe: exe);
        Assert.False(r.CacheWritable);
        Assert.NotNull(r.CacheReason);
        Assert.True(r.DataWritable);
    }

    [Fact]
    public void Second_launch_for_same_root_is_already_running_and_other_root_is_not()
    {
        var first = Run();
        if (first.Instance.Status != SingleInstanceStatus.Acquired) return;
        var second = Run();
        Assert.True(second.AlreadyRunning);
        Assert.Null(second.Copy);   // 何も書かない
        var other = Run(_t.Combine("data2"));
        Assert.False(other.AlreadyRunning);
    }

    [Fact]
    public void Log_never_contains_todo_text()
    {
        Seed("2026-10-12", "ひみつのTODO本文");
        var r = Run();
        var log = Path.Combine(r.Paths.CacheRoot, "logs", "app.log");
        var text = File.Exists(log) ? File.ReadAllText(log) : "";
        Assert.DoesNotContain("ひみつ", text);
    }
}

/// <summary>計画 6-1：ウィンドウの Tunnel で受けるキーの判断（A の一本化・Mod+E）。</summary>
public sealed class WindowKeyTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private long _now;

    public void Dispose() => _t.Dispose();

    private MainViewModel Make(FakeUiServices? ui = null, params string[] days)
    {
        var store = new DayStore(_t.Path);
        foreach (var d in days) store.Write(DateOnly.Parse(d), "x");
        return new MainViewModel(new DayIndex(_t.Path), store, Today, _t.Path, false, ui ?? new FakeUiServices(), _t.Combine("p"),
            nowMs: () => _now);
    }

    [Fact]
    public void Arrows_from_outside_webview_move_to_neighbors()
    {
        var vm = Make(null, "2026-10-14", "2026-10-12", "2026-09-30");
        var requested = new List<DateOnly>();
        vm.LeftPane.Cursor.OpenDayRequested += requested.Add;
        Assert.True(vm.OnWindowKey(AppKey.Left, fromWebView: false));
        Assert.Equal([new DateOnly(2026, 10, 12)], requested);
    }

    [Fact]
    public void Arrows_from_webview_are_left_to_the_page_route()
    {
        var vm = Make(null, "2026-10-14", "2026-10-12");
        var requested = new List<DateOnly>();
        vm.LeftPane.Cursor.OpenDayRequested += requested.Add;
        Assert.False(vm.OnWindowKey(AppKey.Left, fromWebView: true));
        Assert.Empty(requested);   // 二重に届いても、片方の経路だけが動く
        vm.OnPageKey(PageKey.ArrowLeft);
        Assert.Single(requested);
    }

    [Fact]
    public void Arrow_at_the_end_does_nothing_but_is_consumed()
    {
        var vm = Make(null, "2026-10-14");
        var requested = new List<DateOnly>();
        vm.LeftPane.Cursor.OpenDayRequested += requested.Add;
        Assert.True(vm.OnWindowKey(AppKey.Right, false));
        Assert.True(vm.OnWindowKey(AppKey.Left, false));
        Assert.Empty(requested);
    }

    [Fact]
    public void Arrows_in_edit_mode_belong_to_the_textbox()
    {
        var vm = Make(null, "2026-10-14", "2026-10-12");
        vm.Editor.BeginEdit();
        Assert.False(vm.OnWindowKey(AppKey.Left, false));
    }

    [Fact]
    public void Arrow_from_a_day_without_file_jumps_to_neighbors_and_follows_month()
    {
        var vm = Make(null, "2026-09-30", "2026-10-20");
        Assert.True(vm.OnWindowKey(AppKey.Left, false));   // 今日（10/14）にはファイルが無い
        Assert.Equal(new DateOnly(2026, 9, 30), vm.LeftPane.Cursor.SelectedDay);
        Assert.Equal(new DateOnly(2026, 9, 1), vm.LeftPane.Cursor.DisplayedMonth);
    }

    [Fact]
    public void ModE_enters_edit_from_view_even_when_it_came_via_webview_and_leaves_from_edit()
    {
        var vm = Make(null, "2026-10-14");
        Assert.True(vm.OnWindowKey(AppKey.ModE, fromWebView: true));
        Assert.True(vm.Editor.IsEditing);
        _now += 1000;
        Assert.True(vm.OnWindowKey(AppKey.ModE, false));
        Assert.True(vm.Editor.IsViewing);
    }

    [Fact]
    public void Double_delivery_page_begin_edit_then_window_mod_e_does_not_bounce_back()
    {
        var vm = Make(null, "2026-10-14");
        vm.OnPageKey(PageKey.BeginEdit);           // ページ経路（先に届く）
        _now += 1;
        vm.OnWindowKey(AppKey.ModE, false);        // 同じ押下の Avalonia 経路
        Assert.True(vm.Editor.IsEditing);
        _now += 500;
        vm.OnWindowKey(AppKey.ModE, false);        // 次の押下は戻す
        Assert.True(vm.Editor.IsViewing);
    }

    [Fact]
    public void Everything_is_ignored_while_confirming()
    {
        var ui = new FakeUiServices { SaveAnswer = SaveChoice.Cancel };
        var vm = Make(ui, "2026-10-14", "2026-10-12");
        vm.Editor.BeginEdit();
        vm.Editor.SetDraft("changed");
        var results = new List<bool>();
        ui.WhileAsking = () =>
        {
            results.Add(vm.OnWindowKey(AppKey.ModE, false));
            results.Add(vm.OnWindowKey(AppKey.Left, false));
        };
        _ = vm.Editor.LeaveEditAsync();
        Assert.Equal([false, false], results);
        Assert.True(vm.Editor.IsEditing);
    }

    [Fact]
    public void Startup_notice_is_shown_until_next_action()
    {
        var store = new DayStore(_t.Path);
        var vm = new MainViewModel(new DayIndex(_t.Path), store, Today, _t.Path, false, new FakeUiServices(), _t.Combine("p"),
            startupNotice: "コピーに失敗しました");
        Assert.Equal("コピーに失敗しました", vm.Editor.Message);
        Assert.True(vm.Editor.IsViewing);
    }
}

/// <summary>要件 12 章：1 年分（365 ファイル）・壊れたファイル・ログに本文を書かない。</summary>
public sealed class NonFunctionalTests : IDisposable
{
    private readonly TempDir _t = new();

    public void Dispose() => _t.Dispose();

    [Fact]
    public void A_year_of_files_with_broken_ones_starts_fast_and_other_days_stay_usable()
    {
        var data = _t.Combine("data");
        var store = new DayStore(data);
        var start = new DateOnly(2025, 10, 15);
        for (var i = 0; i < 365; i++) store.Write(start.AddDays(i), $"- [ ] 項目 {i}\n  - [x] 子\n");
        // 壊れた（UTF-8 でない）日と、名前が形に合わない・拡張子違いのファイル
        var broken = start.AddDays(100);
        File.WriteAllBytes(store.PathOf(broken), [0x82, 0xA0, 0x82, 0xA2]);
        File.WriteAllText(Path.Combine(data, "2025", "memo.md"), "x");
        File.WriteAllText(Path.Combine(data, "2025", "10", "2025-10-15.md.bak"), "x");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = AppStartup.Run(new LaunchOptions(data), _t.Combine("exe"), new DateOnly(2026, 10, 15));
        var elapsed = sw.ElapsedMilliseconds;
        using var _ = r.Instance;

        Assert.True(elapsed < 3000, $"起動の準備に {elapsed}ms かかった");
        Assert.Equal(365, r.Index.Count + (r.Index.Contains(new DateOnly(2026, 10, 15)) ? -1 : 0));
        Assert.IsType<DayContent.Failed>(store.Read(broken));
        Assert.IsType<DayContent.Loaded>(store.Read(start.AddDays(101)));
        // 壊れた日はコピー元にならない（今日の前日は 2026-10-14 = 読める日）
        var copied = Assert.IsType<StartupCopyResult.Copied>(r.Copy);
        Assert.Equal(new DateOnly(2026, 10, 14), copied.From);
        // 認識しないファイルは消さない・書き換えない
        Assert.Equal("x", File.ReadAllText(Path.Combine(data, "2025", "memo.md")));
        Assert.True(File.Exists(Path.Combine(data, "2025", "10", "2025-10-15.md.bak")));
    }
}
