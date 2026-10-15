using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

public sealed class MemoTabViewModelTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private long _now = 1000;

    public void Dispose() => _t.Dispose();

    private MainViewModel Vm(Func<(bool CanWrite, string? Reason)>? writable = null, params string[] days)
    {
        var store = new DayStore(_t.Path);
        foreach (var d in days) store.Write(DateOnly.Parse(d), "- [ ] " + d);
        var memos = new MemoLibrary(_t.Path);
        if (memos.EnumerateTitles().Count == 0) memos.Create("メモ");   // 開いているメモがある状態から始める
        return new MainViewModel(new DayIndex(_t.Path), store, Today, _t.Path, false, _ui, _t.Combine("p"),
            dataWritable: writable, nowMs: () => _now);
    }

    [Fact]
    public void Starts_on_the_todo_tab_with_the_memo_loaded()
    {
        new MemoLibrary(_t.Path).Write("メモ", "# memo");
        var vm = Vm();
        Assert.Equal(AppTab.Todo, vm.SelectedTab);
        Assert.Equal("# memo", Assert.IsType<PreviewSource.Markdown>(vm.Memo.CurrentPreview).Text);
    }

    [Fact]
    public void SelectTab_raises_the_event_only_on_change()
    {
        var vm = Vm();
        var seen = new List<AppTab>();
        vm.SelectedTabChanged += seen.Add;
        vm.SelectTab(AppTab.Memo);
        vm.SelectTab(AppTab.Memo);
        vm.SelectTab(AppTab.Todo);
        Assert.Equal([AppTab.Memo, AppTab.Todo], seen);
    }

    [Fact]
    public void Arrows_do_not_move_the_day_on_the_memo_tab()
    {
        var vm = Vm(null, "2026-10-14", "2026-10-12");
        vm.SelectTab(AppTab.Memo);
        Assert.False(vm.OnWindowKey(AppKey.Left, fromWebView: false));
        Assert.Equal(Today, vm.LeftPane.Cursor.SelectedDay);
        vm.SelectTab(AppTab.Todo);
        Assert.True(vm.OnWindowKey(AppKey.Left, fromWebView: false));
    }

    [Fact]
    public void ModE_goes_only_to_the_visible_tab()
    {
        var vm = Vm();
        vm.SelectTab(AppTab.Memo);
        Assert.True(vm.OnWindowKey(AppKey.ModE, fromWebView: false));
        Assert.True(vm.Memo.Editor.IsEditing);
        Assert.True(vm.Editor.IsViewing);   // TODO は触らない

        vm.SelectTab(AppTab.Todo);
        Assert.True(vm.OnWindowKey(AppKey.ModE, fromWebView: false));
        Assert.True(vm.Editor.IsEditing);
        Assert.True(vm.Memo.Editor.IsEditing);   // メモは編集のまま（タブを移っても入力は残る）
    }

    [Fact]
    public void ModE_on_the_memo_tab_bounces_back_only_after_the_window()
    {
        var vm = Vm();
        vm.SelectTab(AppTab.Memo);
        vm.OnWindowKey(AppKey.ModE, false);
        _now += MainViewModel.DoubleDeliveryWindowMs - 1;
        vm.OnWindowKey(AppKey.ModE, false);
        Assert.True(vm.Memo.Editor.IsEditing);
        _now += MainViewModel.DoubleDeliveryWindowMs + 1;
        vm.OnWindowKey(AppKey.ModE, false);
        Assert.True(vm.Memo.Editor.IsViewing);
    }

    [Fact]
    public void Memo_page_key_begins_edit_but_ignores_arrows()
    {
        var vm = Vm(null, "2026-10-14", "2026-10-12");
        vm.Memo.OnPageKey(PageKey.ArrowLeft);
        Assert.Equal(Today, vm.LeftPane.Cursor.SelectedDay);
        vm.Memo.OnPageKey(PageKey.BeginEdit);
        Assert.True(vm.Memo.Editor.IsEditing);
    }

    [Fact]
    public void Unwritable_data_root_blocks_memo_editing_too()
    {
        var vm = Vm(() => (false, "読み取り専用"));
        Assert.False(vm.Memo.Editor.CanBeginEdit);
    }

    [Fact]
    public async Task Closing_asks_todo_then_memo_and_stops_at_a_cancel()
    {
        var vm = Vm();
        vm.Editor.BeginEdit();
        vm.Editor.SetDraft("todo changed");
        vm.Memo.Editor.BeginEdit();
        vm.Memo.Editor.SetDraft("memo changed");
        Assert.True(vm.AnyDirty);

        var asked = new List<string>();
        var answers = new Queue<SaveChoice>([SaveChoice.Save, SaveChoice.Cancel]);
        _ui.WhileAsking = () =>
        {
            asked.Add(_ui.LastAskSaveMessage!);
            _ui.SaveAnswer = answers.Dequeue();
        };
        Assert.False(await vm.CanCloseAsync());
        Assert.Equal(["この日のメモが保存されていません。保存しますか？", "メモが保存されていません。保存しますか？"], asked);
        Assert.False(vm.Editor.IsDirty);            // TODO は保存できた
        Assert.True(vm.Memo.Editor.IsDirty);        // メモはキャンセルで編集のまま
        Assert.Equal(AppTab.Memo, vm.SelectedTab);  // 未保存の側を見せる
    }

    [Fact]
    public async Task Closing_with_nothing_dirty_is_allowed_without_asking()
    {
        var vm = Vm();
        vm.Memo.Editor.BeginEdit();   // 編集に入っただけ（変更なし）
        Assert.False(vm.AnyDirty);
        Assert.True(await vm.CanCloseAsync());
        Assert.Equal(0, _ui.AskSaveCount);
    }

    [Fact]
    public async Task AnyConfirming_reflects_either_session()
    {
        var vm = Vm();
        vm.Memo.Editor.BeginEdit();
        vm.Memo.Editor.SetDraft("x");
        var seen = false;
        _ui.WhileAsking = () => seen = vm.AnyConfirming;
        await vm.Memo.Editor.LeaveEditAsync();
        Assert.True(seen);
        Assert.False(vm.AnyConfirming);
    }

    [Fact]
    public void Theme_change_rebuilds_both_previews()
    {
        var vm = Vm();
        var todo = new List<bool>();
        var memo = new List<bool>();
        vm.PreviewChanged += (_, reload) => todo.Add(reload);
        vm.Memo.PreviewChanged += (_, reload) => memo.Add(reload);
        vm.OnThemeChanged();
        Assert.Equal([true], todo);
        Assert.Equal([true], memo);
    }

    [Fact]
    public void Todo_checkbox_click_writes_without_redrawing_and_keeps_the_next_preview_current()
    {
        var vm = Vm(null, "2026-10-14");
        var redraws = 0;
        vm.PreviewChanged += (_, _) => redraws++;
        vm.OnCheckboxToggled(0, true);
        Assert.Equal(0, redraws);   // 楽観的更新：ページは作り直さない
        Assert.Equal("- [x] 2026-10-14", File.ReadAllText(new DayStore(_t.Path).PathOf(Today)));
        Assert.Equal("- [x] 2026-10-14", Assert.IsType<PreviewSource.Markdown>(vm.CurrentPreview).Text);
    }

    [Fact]
    public void Memo_checkbox_click_writes_the_memo_file()
    {
        new MemoLibrary(_t.Path).Write("メモ", "- [ ] m");
        var vm = Vm();
        var redraws = 0;
        vm.Memo.PreviewChanged += (_, _) => redraws++;
        vm.Memo.OnCheckboxToggled(0, true);
        Assert.Equal(0, redraws);
        Assert.Equal("- [x] m", File.ReadAllText(Path.Combine(_t.Path, "memos", "メモ.md")));
        Assert.Equal("- [x] m", Assert.IsType<PreviewSource.Markdown>(vm.Memo.CurrentPreview).Text);
    }

    [Fact]
    public void Failed_checkbox_click_redraws_to_revert_the_page()
    {
        var vm = Vm(null, "2026-10-14");
        var redraws = new List<bool>();
        vm.PreviewChanged += (_, reload) => redraws.Add(reload);
        File.WriteAllText(new DayStore(_t.Path).PathOf(Today), "changed outside");
        vm.OnCheckboxToggled(0, true);
        Assert.Equal([true], redraws);
        Assert.StartsWith("チェックボックスを更新できませんでした", vm.Editor.Message);
    }

    // ── メモの一覧（2-4） ──

    private MainViewModel VmWithMemos(string? memoNotice = null, params (string Title, int MinutesAgo)[] memos)
    {
        var lib = new MemoLibrary(_t.Path);
        foreach (var (title, ago) in memos)
        {
            lib.Write(title, "# " + title);
            File.SetLastWriteTimeUtc(lib.PathOf(title), DateTime.UtcNow.AddMinutes(-ago));
        }
        return new MainViewModel(new DayIndex(_t.Path), new DayStore(_t.Path), Today, _t.Path, false, _ui, _t.Combine("p"),
            nowMs: () => _now, memoNotice: memoNotice);
    }

    private string[] MemoFiles() =>
        Directory.Exists(_t.Combine("memos"))
            ? [.. Directory.GetFiles(_t.Combine("memos"), "*.md").Select(f => Path.GetFileNameWithoutExtension(f)).Order(StringComparer.Ordinal)]
            : [];

    [Fact]
    public void Starts_with_the_newest_memo_open_and_selected_in_the_list()
    {
        var vm = VmWithMemos(null, ("古い", 60), ("新しい", 1));
        Assert.Equal("新しい", vm.Memo.Editor.Title);
        Assert.Equal(["新しい", "古い"], vm.Memo.List.Items.Select(i => i.Title).ToArray());
        Assert.True(vm.Memo.List.Items[0].IsSelected);
    }

    [Fact]
    public void Starts_with_no_memo_when_there_are_none()
    {
        var vm = VmWithMemos();
        Assert.Null(vm.Memo.Editor.Title);
        Assert.True(vm.Memo.List.IsEmpty);
        Assert.Equal("メモなし", vm.Memo.Editor.HeaderText);
    }

    [Fact]
    public void Startup_memo_notice_is_shown_on_the_memo_tab_not_the_todo_tab()
    {
        var vm = VmWithMemos("古い memo.md が残っています（消して構いません）", ("a", 1));
        Assert.Equal("古い memo.md が残っています（消して構いません）", vm.Memo.Editor.Message);
        Assert.Null(vm.Editor.Message);
    }

    [Fact]
    public void New_memo_key_creates_a_memo_on_the_memo_tab_only()
    {
        var vm = VmWithMemos(null, ("a", 1));

        Assert.False(vm.OnWindowKey(AppKey.NewMemo, fromWebView: false));   // TODO タブ
        Assert.Equal(["a"], MemoFiles());

        vm.SelectTab(AppTab.Memo);
        Assert.True(vm.OnWindowKey(AppKey.NewMemo, fromWebView: false));

        Assert.Equal(["a", "無題のメモ"], MemoFiles());
        Assert.Equal("無題のメモ", vm.Memo.Editor.Title);
        Assert.Equal("無題のメモ", vm.Memo.List.RenamingTitle);
    }

    [Fact]
    public void New_memo_from_the_webview_counts_once_when_both_routes_deliver()
    {
        var vm = VmWithMemos(null, ("a", 1));
        vm.SelectTab(AppTab.Memo);

        _now = 5000;
        Assert.True(vm.OnWindowKey(AppKey.NewMemo, fromWebView: true));   // Avalonia 経路
        _now = 5001;
        vm.Memo.OnPageKey(PageKey.NewMemo);                               // ページ経路（同じ押下）

        Assert.Equal(["a", "無題のメモ"], MemoFiles());
    }

    [Fact]
    public void New_memo_again_after_the_window_creates_another()
    {
        var vm = VmWithMemos(null, ("a", 1));
        vm.SelectTab(AppTab.Memo);

        _now = 5000;
        vm.OnWindowKey(AppKey.NewMemo, false);
        vm.Memo.List.CancelRename();
        _now = 5000 + ModEToggle.WindowMs + 1;
        vm.OnWindowKey(AppKey.NewMemo, false);

        Assert.Equal(["a", "無題のメモ", "無題のメモ (2)"], MemoFiles());
    }

    [Fact]
    public void Find_memo_key_asks_the_view_to_focus_the_search_box()
    {
        var vm = VmWithMemos(null, ("a", 1));
        var asked = 0;
        vm.Memo.List.FocusSearchRequested += () => asked++;

        Assert.False(vm.OnWindowKey(AppKey.FindMemo, false));   // TODO タブでは効かない
        vm.SelectTab(AppTab.Memo);
        Assert.True(vm.OnWindowKey(AppKey.FindMemo, fromWebView: true));

        Assert.Equal(1, asked);
    }

    [Fact]
    public void Find_memo_from_both_routes_asks_once()
    {
        var vm = VmWithMemos(null, ("a", 1));
        var asked = 0;
        vm.Memo.List.FocusSearchRequested += () => asked++;
        vm.SelectTab(AppTab.Memo);

        _now = 9000;
        vm.OnWindowKey(AppKey.FindMemo, fromWebView: true);
        _now = 9001;
        vm.Memo.OnPageKey(PageKey.FindMemo);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void Rename_key_starts_renaming_the_open_memo_but_not_from_the_webview()
    {
        var vm = VmWithMemos(null, ("a", 1));
        vm.SelectTab(AppTab.Memo);

        Assert.False(vm.OnWindowKey(AppKey.RenameMemo, fromWebView: true));
        Assert.Null(vm.Memo.List.RenamingTitle);

        Assert.True(vm.OnWindowKey(AppKey.RenameMemo, fromWebView: false));
        Assert.Equal("a", vm.Memo.List.RenamingTitle);
    }

    [Fact]
    public void Rename_key_targets_the_focused_item_when_given()
    {
        var vm = VmWithMemos(null, ("a", 1), ("b", 5));
        vm.SelectTab(AppTab.Memo);

        vm.OnWindowKey(AppKey.RenameMemo, false, memoTarget: "b");

        Assert.Equal("b", vm.Memo.List.RenamingTitle);
    }

    [Fact]
    public void Delete_key_asks_then_deletes_the_target_not_the_open_memo()
    {
        var vm = VmWithMemos(null, ("a", 1), ("b", 5));
        vm.SelectTab(AppTab.Memo);

        Assert.False(vm.OnWindowKey(AppKey.DeleteMemo, fromWebView: true));
        Assert.True(vm.OnWindowKey(AppKey.DeleteMemo, false, memoTarget: "b"));

        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Equal(["a"], MemoFiles());
        Assert.Equal("a", vm.Memo.Editor.Title);
    }

    [Fact]
    public async Task Memo_keys_are_ignored_while_a_dialog_is_up()
    {
        var vm = VmWithMemos(null, ("a", 1));
        vm.SelectTab(AppTab.Memo);
        vm.Memo.Editor.BeginEdit();
        vm.Memo.Editor.SetDraft("changed");
        bool? handled = null;
        _ui.SaveAnswer = SaveChoice.Cancel;
        _ui.WhileAsking = () => handled = vm.OnWindowKey(AppKey.NewMemo, false);

        await vm.Memo.Editor.LeaveEditAsync();

        Assert.False(handled);
        Assert.Equal(["a"], MemoFiles());
    }

    [Fact]
    public async Task Any_confirming_is_true_while_the_list_asks_to_delete()
    {
        var vm = VmWithMemos(null, ("a", 1));
        bool? during = null;
        _ui.WhileAsking = () => during = vm.AnyConfirming;

        await vm.Memo.List.DeleteAsync("a");

        Assert.True(during);
        Assert.False(vm.AnyConfirming);
    }

    [Fact]
    public async Task Closing_the_window_still_asks_about_an_unsaved_memo()
    {
        var vm = VmWithMemos(null, ("a", 1));
        vm.Memo.Editor.BeginEdit();
        vm.Memo.Editor.SetDraft("changed");
        _ui.SaveAnswer = SaveChoice.Cancel;

        Assert.False(await vm.CanCloseAsync());
        Assert.Equal(AppTab.Memo, vm.SelectedTab);
    }

    // ── 最後に開いていたメモ（2-5） ──

    private MainViewModel VmWithLast(MemoLastOpened last, params (string Title, int MinutesAgo)[] memos)
    {
        var lib = new MemoLibrary(_t.Path);
        foreach (var (title, ago) in memos)
        {
            lib.Write(title, "# " + title);
            File.SetLastWriteTimeUtc(lib.PathOf(title), DateTime.UtcNow.AddMinutes(-ago));
        }
        return new MainViewModel(new DayIndex(_t.Path), new DayStore(_t.Path), Today, _t.Path, false, _ui, _t.Combine("p"),
            nowMs: () => _now, lastOpened: last);
    }

    private MemoLastOpened Last() => new(_t.Combine("cache", "last-memo.txt"));

    [Fact]
    public void Starts_with_the_last_opened_memo_even_when_it_is_not_the_newest()
    {
        var last = Last();
        last.Write("古い");

        var vm = VmWithLast(last, ("古い", 60), ("新しい", 1));

        Assert.Equal("古い", vm.Memo.Editor.Title);
        Assert.True(vm.Memo.List.Items.Single(i => i.Title == "古い").IsSelected);
    }

    [Fact]
    public void Falls_back_to_the_first_when_the_last_memo_is_gone()
    {
        var last = Last();
        last.Write("消えた");

        var vm = VmWithLast(last, ("a", 60), ("b", 1));

        Assert.Equal("b", vm.Memo.Editor.Title);
    }

    [Fact]
    public async Task Opening_a_memo_remembers_it()
    {
        var last = Last();
        var vm = VmWithLast(last, ("a", 60), ("b", 1));
        Assert.Equal("b", last.Read());   // 起動時に開いたもの

        await vm.Memo.List.OpenAsync("a");

        Assert.Equal("a", last.Read());
    }

    [Fact]
    public void Renaming_the_open_memo_updates_the_remembered_title()
    {
        var last = Last();
        var vm = VmWithLast(last, ("a", 1));

        vm.Memo.List.BeginRename("a");
        vm.Memo.List.RenameText = "z";
        vm.Memo.List.CommitRename();

        Assert.Equal("z", last.Read());
    }

    [Fact]
    public async Task Deleting_the_last_memo_forgets_it_and_deleting_the_open_one_remembers_the_neighbor()
    {
        var last = Last();
        var vm = VmWithLast(last, ("a", 60), ("b", 1));

        await vm.Memo.List.DeleteAsync("b");
        Assert.Equal("a", last.Read());

        await vm.Memo.List.DeleteAsync("a");
        Assert.Null(last.Read());
    }

    [Fact]
    public void An_unwritable_cache_does_not_stop_the_memo_tab()
    {
        File.WriteAllText(_t.Combine("cache"), "邪魔");
        var vm = VmWithLast(Last(), ("a", 1));
        Assert.Equal("a", vm.Memo.Editor.Title);
    }
}
