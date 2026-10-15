using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Todokun.Core;
using Todokun.Presentation;
using Todokun.Tests;
using Todokun.Views;

namespace Todokun.UiTests;

/// <summary>計画 3-1：メモタブの左ペイン（一覧・検索・件数・知らせ）。ヘッドレスで見える範囲を確かめる。</summary>
public sealed class MemoListTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();

    public void Dispose() => _t.Dispose();

    private (MainWindow W, MainViewModel Vm) Open(params (string Title, string Text, int MinutesAgo)[] memos)
    {
        var lib = new MemoLibrary(_t.Path);
        foreach (var (title, text, ago) in memos)
        {
            lib.Write(title, text);
            File.SetLastWriteTimeUtc(lib.PathOf(title), DateTime.UtcNow.AddMinutes(-ago));
        }
        var vm = new MainViewModel(new DayIndex(_t.Path), new DayStore(_t.Path), Today, _t.Path, false, _ui, _t.Combine("p"));
        var w = new MainWindow { DataContext = vm };
        w.Show();
        vm.SelectTab(AppTab.Memo);
        Flush();
        return (w, vm);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    private static string[] ItemTitles(Window w) =>
        [.. Find<ItemsControl>(w, "MemoItems").Items.OfType<MemoListItem>().Select(i => i.Title)];

    [AvaloniaFact]
    public void Memo_tab_shows_the_list_next_to_the_pane()
    {
        var (w, _) = Open(("買い物", "# 買い物\n- [ ] 牛乳", 5), ("会議", "議事", 60));

        Assert.True(Find<Grid>(w, "MemoRoot").IsVisible);
        Assert.False(Find<Grid>(w, "Root").IsVisible);
        Assert.Equal(["買い物", "会議"], ItemTitles(w));
        Assert.Equal("2 件", Find<TextBlock>(w, "MemoCount").Text);
        Assert.False(Find<TextBlock>(w, "MemoEmpty").IsVisible);
    }

    [AvaloniaFact]
    public void Todo_tab_hides_the_memo_area()
    {
        var (w, vm) = Open(("a", "x", 1));
        vm.SelectTab(AppTab.Todo);
        Flush();
        Assert.False(Find<Grid>(w, "MemoRoot").IsVisible);
        Assert.True(Find<Grid>(w, "Root").IsVisible);
    }

    [AvaloniaFact]
    public void Clicking_an_item_opens_that_memo_in_the_right_pane()
    {
        var (w, vm) = Open(("a", "最初", 1), ("b", "二つ目", 10));
        Assert.Equal("a", vm.Memo.Editor.Title);

        var item = Find<ItemsControl>(w, "MemoItems").Items.OfType<MemoListItem>().Single(i => i.Title == "b");
        item.OpenCommand.Execute(null);
        Flush();

        Assert.Equal("b", vm.Memo.Editor.Title);
        var header = Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBlock>().First(x => x.Name == "DayText");
        Assert.Equal("b", header.Text);
        Assert.Contains(Find<ItemsControl>(w, "MemoItems").Items.OfType<MemoListItem>(), i => i.Title == "b" && i.IsSelected);
    }

    [AvaloniaFact]
    public void Plus_button_adds_a_memo()
    {
        var (w, vm) = Open(("a", "x", 1));
        var add = Find<Button>(w, "MemoAdd");

        add.Command!.Execute(null);
        Flush();

        Assert.Equal("無題のメモ", vm.Memo.Editor.Title);
        Assert.Contains("無題のメモ", ItemTitles(w));
        Assert.Equal("2 件", Find<TextBlock>(w, "MemoCount").Text);
    }

    [AvaloniaFact]
    public void Typing_in_the_search_box_filters_the_list()
    {
        var (w, _) = Open(("買い物", "牛乳", 1), ("会議", "予算", 10));
        Find<TextBox>(w, "MemoSearch").Text = "予算";
        Flush();

        Assert.Equal(["会議"], ItemTitles(w));
        Assert.Equal("1 / 2 件", Find<TextBlock>(w, "MemoCount").Text);

        Find<TextBox>(w, "MemoSearch").Text = "zzz";
        Flush();
        Assert.True(Find<TextBlock>(w, "MemoNoMatch").IsVisible);
        Assert.False(Find<TextBlock>(w, "MemoEmpty").IsVisible);
    }

    [AvaloniaFact]
    public void Empty_state_shows_the_guide()
    {
        var (w, _) = Open();
        Assert.True(Find<TextBlock>(w, "MemoEmpty").IsVisible);
        Assert.Equal("0 件", Find<TextBlock>(w, "MemoCount").Text);
        Assert.False(Find<Border>(w, "MemoNoticeBar").IsVisible);
    }

    [AvaloniaFact]
    public void A_notice_is_shown_under_the_header()
    {
        var (w, vm) = Open(("a", "x", 1), ("b", "y", 10));
        File.Delete(new MemoLibrary(_t.Path).PathOf("b"));

        vm.Memo.List.OpenAsync("b").GetAwaiter().GetResult();   // 開こうとしたが見つからない
        Flush();

        Assert.True(Find<Border>(w, "MemoNoticeBar").IsVisible);
        Assert.Contains("『b』が見つかりません", Find<TextBlock>(w, "MemoNotice").Text);
    }

    [AvaloniaFact]
    public void Find_request_focuses_the_search_box()
    {
        var (w, vm) = Open(("a", "x", 1));
        vm.Memo.List.RequestFocusSearch();
        Flush();
        Assert.True(Find<TextBox>(w, "MemoSearch").IsFocused);
    }

    // ── 3-2：追加・名前の変更 ──

    private static void Press(Window w, Key key)
    {
        var physical = key switch
        {
            Key.Enter => PhysicalKey.Enter,
            Key.Escape => PhysicalKey.Escape,
            Key.F2 => PhysicalKey.F2,
            Key.Delete => PhysicalKey.Delete,
            Key.N => PhysicalKey.N,
            Key.F => PhysicalKey.F,
            Key.Down => PhysicalKey.ArrowDown,
            Key.Up => PhysicalKey.ArrowUp,
            _ => PhysicalKey.None,
        };
        w.KeyPress(key, RawInputModifiers.None, physical, null);
    }

    private static RawInputModifiers CommandRaw =>
        Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void PressCommand(Window w, Key key, PhysicalKey physical) => w.KeyPress(key, CommandRaw, physical, null);

    private static TextBox RenameBox(Window w) => Find<TextBox>(w, "RenameBox");

    [AvaloniaFact]
    public void Plus_shows_the_rename_box_with_the_default_name()
    {
        var (w, vm) = Open(("a", "x", 1));

        Find<Button>(w, "MemoAdd").Command!.Execute(null);
        Flush();

        Assert.Equal("無題のメモ", vm.Memo.List.RenamingTitle);
        var box = RenameBox(w);
        Assert.True(box.IsVisible);
        Assert.Equal("無題のメモ", box.Text);
        Assert.True(box.IsFocused);
    }

    [AvaloniaFact]
    public void Enter_in_the_rename_box_commits_and_enters_edit_mode()
    {
        var (w, vm) = Open(("a", "x", 1));
        Find<Button>(w, "MemoAdd").Command!.Execute(null);
        Flush();

        RenameBox(w).Text = "買い物";
        Flush();
        Press(w, Key.Enter);
        Flush();

        Assert.Equal("買い物", vm.Memo.Editor.Title);
        Assert.True(new MemoLibrary(_t.Path).Exists("買い物"));
        Assert.Null(vm.Memo.List.RenamingTitle);
        Assert.True(vm.Memo.Editor.IsEditing);
        Assert.DoesNotContain(w.GetVisualDescendants().OfType<TextBox>(), x => x.Name == "RenameBox" && x.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public void Escape_in_the_rename_box_keeps_the_default_name_and_enters_edit_mode()
    {
        var (w, vm) = Open(("a", "x", 1));
        Find<Button>(w, "MemoAdd").Command!.Execute(null);
        Flush();

        RenameBox(w).Text = "捨てる";
        Flush();
        Press(w, Key.Escape);
        Flush();

        Assert.Equal("無題のメモ", vm.Memo.Editor.Title);
        Assert.True(vm.Memo.Editor.IsEditing);
        Assert.Null(vm.Memo.List.RenamingTitle);
    }

    [AvaloniaFact]
    public void Losing_focus_commits_the_rename_once()
    {
        var (w, vm) = Open(("a", "x", 1));
        Find<Button>(w, "MemoAdd").Command!.Execute(null);
        Flush();

        RenameBox(w).Text = "会議";
        Flush();
        Find<TextBox>(w, "MemoSearch").Focus();
        Flush();

        Assert.Equal("会議", vm.Memo.Editor.Title);
        Assert.Equal(["a", "会議"], new MemoLibrary(_t.Path).EnumerateTitles().Order(StringComparer.Ordinal).ToArray());
        Assert.Null(vm.Memo.List.RenamingTitle);
    }

    [AvaloniaFact]
    public void Rename_box_shows_the_preview_only_when_the_name_changes()
    {
        var (w, _) = Open(("a", "x", 1));
        Find<Button>(w, "MemoAdd").Command!.Execute(null);
        Flush();

        var preview = Find<TextBlock>(w, "RenamePreviewText");
        Assert.False(preview.IsVisible);

        RenameBox(w).Text = "A/B:テスト?";
        Flush();

        Assert.True(preview.IsVisible);
        Assert.Equal("→ 『A／B：テスト？』で保存されます", preview.Text);
    }

    [AvaloniaFact]
    public void F2_renames_the_open_memo_but_not_from_a_text_box()
    {
        var (w, vm) = Open(("a", "x", 1));

        Find<TextBox>(w, "MemoSearch").Focus();
        Flush();
        Press(w, Key.F2);
        Flush();
        Assert.Null(vm.Memo.List.RenamingTitle);

        Find<Button>(w, "MemoAdd").Focus();
        Flush();
        Press(w, Key.F2);
        Flush();
        Assert.Equal("a", vm.Memo.List.RenamingTitle);
        Assert.True(RenameBox(w).IsVisible);
    }

    // ── 3-3：削除 ──

    private static Button DeleteButton(Window w, string title) =>
        w.GetVisualDescendants().OfType<Button>().First(b => b.Name == "MemoDeleteButton" && (b.DataContext as MemoListItem)?.Title == title);

    private static Button ItemButton(Window w, string title) =>
        Find<ItemsControl>(w, "MemoItems").GetVisualDescendants().OfType<Button>()
            .First(b => b.Classes.Contains("listitem") && (b.DataContext as MemoListItem)?.Title == title);

    [AvaloniaFact]
    public void The_x_button_deletes_after_confirming_and_opens_the_neighbor()
    {
        var (w, vm) = Open(("a", "x", 1), ("b", "y", 10));

        DeleteButton(w, "a").Command!.Execute(null);
        Flush();

        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Contains("『a』", _ui.LastConfirmDeleteMessage);
        Assert.False(new MemoLibrary(_t.Path).Exists("a"));
        Assert.Equal("b", vm.Memo.Editor.Title);
        Assert.Equal(["b"], ItemTitles(w));
    }

    [AvaloniaFact]
    public void Cancelling_the_dialog_keeps_the_memo()
    {
        _ui.DeleteAnswer = false;
        var (w, _) = Open(("a", "x", 1));

        DeleteButton(w, "a").Command!.Execute(null);
        Flush();

        Assert.True(new MemoLibrary(_t.Path).Exists("a"));
        Assert.Equal(["a"], ItemTitles(w));
    }

    [AvaloniaFact]
    public void Delete_key_on_a_focused_item_deletes_that_item_not_the_open_memo()
    {
        var (w, vm) = Open(("a", "x", 1), ("b", "y", 10));
        Assert.Equal("a", vm.Memo.Editor.Title);

        ItemButton(w, "b").Focus();
        Flush();
        Press(w, Key.Delete);
        Flush();

        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Contains("『b』", _ui.LastConfirmDeleteMessage);
        Assert.False(new MemoLibrary(_t.Path).Exists("b"));
        Assert.True(new MemoLibrary(_t.Path).Exists("a"));
        Assert.Equal("a", vm.Memo.Editor.Title);
    }

    [AvaloniaFact]
    public void Delete_key_in_a_text_box_does_not_delete_the_memo()
    {
        var (w, vm) = Open(("a", "x", 1));
        Find<TextBox>(w, "MemoSearch").Focus();
        Flush();

        Press(w, Key.Delete);
        Flush();

        Assert.Equal(0, _ui.ConfirmDeleteCount);
        Assert.True(new MemoLibrary(_t.Path).Exists("a"));

        // 編集欄の Delete も、文字を消すだけでメモは消さない
        vm.Memo.Editor.BeginEdit();
        Flush();
        var edit = Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox");
        edit.Focus();
        Flush();
        Press(w, Key.Delete);
        Flush();
        Assert.Equal(0, _ui.ConfirmDeleteCount);
    }

    [AvaloniaFact]
    public void Delete_key_while_nothing_special_is_focused_targets_the_open_memo()
    {
        var (w, vm) = Open(("a", "x", 1), ("b", "y", 10));
        Find<Button>(w, "MemoAdd").Focus();   // 一覧の項目ではなく、テキスト入力でもない
        Flush();

        Press(w, Key.Delete);
        Flush();

        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Contains("『a』", _ui.LastConfirmDeleteMessage);
        Assert.False(new MemoLibrary(_t.Path).Exists("a"));
        Assert.Equal("b", vm.Memo.Editor.Title);
    }

    [AvaloniaFact]
    public void Cmd_backspace_deletes_on_mac()
    {
        if (!OperatingSystem.IsMacOS()) return;
        var (w, _) = Open(("a", "x", 1));
        Find<Button>(w, "MemoAdd").Focus();
        Flush();

        w.KeyPress(Key.Back, RawInputModifiers.Meta, PhysicalKey.Backspace, null);
        Flush();

        Assert.Equal(1, _ui.ConfirmDeleteCount);
    }

    [AvaloniaFact]
    public void Deleting_the_last_memo_shows_the_empty_state()
    {
        var (w, vm) = Open(("a", "x", 1));

        DeleteButton(w, "a").Command!.Execute(null);
        Flush();

        Assert.Null(vm.Memo.Editor.Title);
        Assert.True(Find<TextBlock>(w, "MemoEmpty").IsVisible);
        Assert.Equal("メモなし", vm.Memo.Editor.HeaderText);
    }

    // ── 3-4：キー操作 ──

    [AvaloniaFact]
    public void Command_n_adds_a_memo_even_while_typing_in_the_edit_box()
    {
        var (w, vm) = Open(("a", "x", 1));
        vm.Memo.Editor.BeginEdit();
        Flush();
        var edit = Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox");
        edit.Focus();
        Flush();

        PressCommand(w, Key.N, PhysicalKey.N);
        Flush();

        Assert.Equal(["a", "無題のメモ"], new MemoLibrary(_t.Path).EnumerateTitles().Order(StringComparer.Ordinal).ToArray());
        Assert.Equal("無題のメモ", vm.Memo.List.RenamingTitle);
    }

    [AvaloniaFact]
    public void Command_n_does_nothing_on_the_todo_tab()
    {
        var (w, vm) = Open(("a", "x", 1));
        vm.SelectTab(AppTab.Todo);
        Flush();

        PressCommand(w, Key.N, PhysicalKey.N);
        Flush();

        Assert.Equal(["a"], new MemoLibrary(_t.Path).EnumerateTitles().ToArray());
    }

    [AvaloniaFact]
    public void Command_f_focuses_the_search_box_and_selects_it()
    {
        var (w, _) = Open(("a", "x", 1));
        var search = Find<TextBox>(w, "MemoSearch");
        search.Text = "abc";
        Find<Button>(w, "MemoAdd").Focus();
        Flush();

        PressCommand(w, Key.F, PhysicalKey.F);
        Flush();

        Assert.True(search.IsFocused);
        Assert.Equal(0, search.SelectionStart);
        Assert.Equal(3, search.SelectionEnd);
    }

    [AvaloniaFact]
    public void Down_arrow_moves_focus_between_items()
    {
        var (w, _) = Open(("a", "x", 1), ("b", "y", 10), ("c", "z", 20));
        ItemButton(w, "a").Focus();
        Flush();

        Press(w, Key.Down);
        Flush();

        var focused = TopLevel.GetTopLevel(w)!.FocusManager!.GetFocusedElement();
        Assert.Equal("b", (focused as Control)?.DataContext is MemoListItem item ? item.Title : null);
    }

    [AvaloniaFact]
    public void Keyboard_only_flow_add_rename_switch_delete()
    {
        var (w, vm) = Open(("a", "x", 1));

        PressCommand(w, Key.N, PhysicalKey.N);          // 追加（入力状態）
        Flush();
        RenameBox(w).Text = "k";
        Flush();
        Press(w, Key.Enter);                             // 確定 → 編集モード
        Flush();
        Assert.Equal("k", vm.Memo.Editor.Title);
        Assert.True(vm.Memo.Editor.IsEditing);

        w.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);   // ビューに戻る
        Flush();
        Assert.True(vm.Memo.Editor.IsViewing);

        ItemButton(w, "a").Focus();
        Flush();
        Press(w, Key.Delete);                            // 項目 a を削除（確認）
        Flush();
        Assert.False(new MemoLibrary(_t.Path).Exists("a"));
        Assert.Equal("k", vm.Memo.Editor.Title);
    }
}
