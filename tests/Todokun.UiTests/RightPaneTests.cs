using Avalonia;
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

public sealed class RightPaneTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly DayStore _store;

    public RightPaneTests() => _store = new DayStore(_t.Path);

    public void Dispose() => _t.Dispose();

    private static RawInputModifiers CommandRaw =>
        (Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta)
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private (MainWindow W, MainViewModel Vm) Open(string? todayText, bool badBytes = false)
    {
        if (todayText is not null) _store.Write(Today, todayText);
        if (badBytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_store.PathOf(Today))!);
            File.WriteAllBytes(_store.PathOf(Today), [0x82, 0xA0]);
        }
        var vm = new MainViewModel(new DayIndex(_t.Path), _store, Today, _t.Path, false, _ui, _t.Combine("preview"));
        var w = new MainWindow { DataContext = vm };
        w.Show();
        Dispatcher.UIThread.RunJobs();
        return (w, vm);
    }

    private static T Named<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static TextBox Enter(Window w, MainViewModel vm)
    {
        vm.Editor.BeginEdit();
        Flush();
        var box = Named<TextBox>(w, "EditBox");
        box.Focus();
        Flush();
        return box;
    }

    private static void Key(Window w, Key key, RawInputModifiers m = RawInputModifiers.None, PhysicalKey phys = PhysicalKey.None)
    {
        w.KeyPress(key, m, phys, null);
        Flush();
    }

    // ── 切り替え・表示 ──
    [AvaloniaFact]
    public void View_mode_shows_date_preview_and_edit_button_only()
    {
        var (w, vm) = Open("- [ ] a");
        Assert.Equal("2026-10-14 (水)", Named<TextBlock>(w, "DayText").Text);
        Assert.Equal("ビュー", Named<TextBlock>(w, "ModeText").Text);
        Assert.True(Named<TodoPreview>(w, "Preview").IsVisible);
        Assert.False(Named<TextBox>(w, "EditBox").IsVisible);
        Assert.True(Named<Button>(w, "BeginEditButton").IsVisible);
        Assert.False(Named<Button>(w, "SaveButton").IsVisible);
        Assert.False(Named<Button>(w, "BackButton").IsVisible);
        Assert.False(Named<TextBlock>(w, "DirtyText").IsVisible);
    }

    [AvaloniaFact]
    public void Missing_day_shows_guidance_text()
    {
        var (w, _) = Open(null);
        var msg = Named<TodoPreview>(w, "Preview").GetVisualDescendants().OfType<TextBlock>().First(x => x.Name == "MessageText");
        Assert.Contains("まだありません", msg.Text);
    }

    [AvaloniaFact]
    public void Edit_button_swaps_preview_and_editor_in_the_same_place()
    {
        var (w, vm) = Open("hello");
        Named<Button>(w, "BeginEditButton").Command!.Execute(null);
        Flush();
        var box = Named<TextBox>(w, "EditBox");
        Assert.True(box.IsVisible);
        Assert.False(Named<TodoPreview>(w, "Preview").IsVisible);
        Assert.Equal("hello", box.Text);
        Assert.Equal(0, box.CaretIndex);
        Assert.True(Named<Button>(w, "SaveButton").IsVisible);
        Assert.True(Named<Button>(w, "BackButton").IsVisible);
        Assert.False(Named<Button>(w, "BeginEditButton").IsVisible);
        Assert.Equal("編集中", Named<TextBlock>(w, "ModeText").Text);
    }

    [AvaloniaFact]
    public void Failed_day_cannot_be_edited_and_shows_reason()
    {
        var (w, vm) = Open(null, badBytes: true);
        Assert.False(Named<Button>(w, "BeginEditButton").Command!.CanExecute(null));
        var msg = Named<TodoPreview>(w, "Preview").GetVisualDescendants().OfType<TextBlock>().First(x => x.Name == "MessageText");
        Assert.Contains("読めません", msg.Text);
    }

    // ── 未保存・保存 ──
    [AvaloniaFact]
    public void Typing_shows_unsaved_and_ctrl_s_saves_and_stays_in_edit()
    {
        var (w, vm) = Open("a");
        var box = Enter(w, vm);
        box.Text = "a\nb";
        Flush();
        Assert.True(Named<TextBlock>(w, "DirtyText").IsVisible);

        Key(w, Avalonia.Input.Key.S, CommandRaw, PhysicalKey.S);
        Assert.Equal("a\nb", File.ReadAllText(_store.PathOf(Today)));
        Assert.False(Named<TextBlock>(w, "DirtyText").IsVisible);
        Assert.True(vm.Editor.IsEditing);
    }

    [AvaloniaFact]
    public void Ctrl_s_does_nothing_in_view_mode()
    {
        var (w, vm) = Open("a");
        Key(w, Avalonia.Input.Key.S, CommandRaw, PhysicalKey.S);
        Assert.Equal(0, _ui.AskSaveCount + _ui.ConfirmDeleteCount);
        Assert.True(vm.Editor.IsViewing);
    }

    [AvaloniaFact]
    public void Esc_returns_to_view_when_clean()
    {
        var (w, vm) = Open("a");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.True(vm.Editor.IsViewing);
        Assert.False(box.IsVisible);
    }

    [AvaloniaFact]
    public void Esc_when_dirty_asks_and_cancel_keeps_editing()
    {
        var (w, vm) = Open("a");
        var box = Enter(w, vm);
        box.Text = "changed";
        Flush();
        _ui.SaveAnswer = SaveChoice.Cancel;
        Key(w, Avalonia.Input.Key.Escape, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.Equal(1, _ui.AskSaveCount);
        Assert.True(vm.Editor.IsEditing);
        Assert.Equal("changed", box.Text);
    }

    [AvaloniaFact]
    public void Ime_processed_escape_does_not_leave_edit()
    {
        var (w, vm) = Open("a");
        Enter(w, vm);
        // IME に処理されたキーは Key.ImeProcessed で届く。変換を取り消す Esc で編集を抜けない
        Key(w, Avalonia.Input.Key.ImeProcessed, RawInputModifiers.None, PhysicalKey.Escape);
        Assert.True(vm.Editor.IsEditing);
    }

    // ── 編集補助（実際のキー → テキスト） ──
    [AvaloniaFact]
    public void Tab_indents_without_moving_focus_and_undo_restores_in_one_step()
    {
        var (w, vm) = Open("- [ ] a\n- [ ] b");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
        Assert.Equal("  - [ ] a\n- [ ] b", box.Text);
        Assert.Equal(2, box.CaretIndex);
        Assert.True(box.IsFocused);
        Key(w, Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
        Assert.Equal("    - [ ] a\n- [ ] b", box.Text);
        box.Undo();
        Assert.Equal("  - [ ] a\n- [ ] b", box.Text);
        box.Undo();
        Assert.Equal("- [ ] a\n- [ ] b", box.Text);
    }

    [AvaloniaFact]
    public void Shift_tab_outdents()
    {
        var (w, vm) = Open("    - a");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Tab, RawInputModifiers.Shift, PhysicalKey.Tab);
        Assert.Equal("  - a", box.Text);
    }

    [AvaloniaFact]
    public void Mod_enter_toggles_check_and_inserts_no_newline()
    {
        var (w, vm) = Open("- [ ] a");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Enter, CommandRaw, PhysicalKey.Enter);
        Assert.Equal("- [x] a", box.Text);
        Key(w, Avalonia.Input.Key.Enter, CommandRaw, PhysicalKey.Enter);
        Assert.Equal("- [ ] a", box.Text);
    }

    [AvaloniaFact]
    public void Mod_enter_on_plain_line_eats_key_without_newline()
    {
        var (w, vm) = Open("plain");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Enter, CommandRaw, PhysicalKey.Enter);
        Assert.Equal("plain", box.Text);
    }

    [AvaloniaFact]
    public void Enter_continues_check_item_and_leaves_list_on_empty_item()
    {
        var (w, vm) = Open("  - [ ] a");
        var box = Enter(w, vm);
        box.CaretIndex = box.Text!.Length;
        Key(w, Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        Assert.Equal("  - [ ] a\n  - [ ] ", box.Text);
        Assert.Equal(box.Text.Length, box.CaretIndex);
        Key(w, Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        Assert.Equal("  - [ ] a\n", box.Text);
    }

    [AvaloniaFact]
    public void Enter_on_plain_line_is_left_to_the_textbox()
    {
        var (w, vm) = Open("plain");
        var box = Enter(w, vm);
        box.CaretIndex = 5;
        Key(w, Avalonia.Input.Key.Enter, RawInputModifiers.None, PhysicalKey.Enter);
        // 補助は何もしない（TextBox の通常の改行。ヘッドレスではキー入力の文字が付かないので、補助が項目を足していないことだけを見る）
        Assert.DoesNotContain("- [ ]", box.Text);
    }

    [AvaloniaFact]
    public void Ime_processed_enter_does_not_continue_item()
    {
        var (w, vm) = Open("- [ ] あ");
        var box = Enter(w, vm);
        box.CaretIndex = box.Text!.Length;
        Key(w, Avalonia.Input.Key.ImeProcessed, RawInputModifiers.None, PhysicalKey.Enter);
        Assert.Equal("- [ ] あ", box.Text);
    }

    [AvaloniaFact]
    public void Edits_are_synced_to_the_session_draft()
    {
        var (w, vm) = Open("- [ ] a");
        var box = Enter(w, vm);
        Key(w, Avalonia.Input.Key.Tab, RawInputModifiers.None, PhysicalKey.Tab);
        Assert.Equal("  - [ ] a", vm.Editor.Draft);
        Assert.True(vm.Editor.IsDirty);
    }
}
