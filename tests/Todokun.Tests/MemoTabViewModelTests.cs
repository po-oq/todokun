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
}
