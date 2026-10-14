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

namespace Todokun.UiTests;

public sealed class KeysAndCloseTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();

    public void Dispose() => _t.Dispose();

    private static RawInputModifiers CommandRaw =>
        Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private (MainWindow W, MainViewModel Vm) Open(params string[] days)
    {
        var store = new DayStore(_t.Path);
        foreach (var d in days) store.Write(DateOnly.Parse(d), "- [ ] " + d);
        var vm = new MainViewModel(new DayIndex(_t.Path), store, Today, _t.Path, false, _ui, _t.Combine("p"));
        var w = new MainWindow { DataContext = vm };
        w.Show();
        Flush();
        return (w, vm);
    }

    private static void Flush()
    {
        Dispatcher.UIThread.RunJobs();
        Dispatcher.UIThread.RunJobs();
    }

    private static void Press(Window w, Key key, RawInputModifiers m = RawInputModifiers.None, PhysicalKey phys = PhysicalKey.None)
    {
        w.KeyPress(key, m, phys, null);
        Flush();
    }

    /// <summary>実際の入力と同じく、入力欄の文字を変える（VM の Draft だけを変えると、入力欄の遅れた TextChanged が戻してしまう）。</summary>
    private static void Type(Window w, MainViewModel vm, string text)
    {
        vm.Editor.BeginEdit();
        Flush();
        w.GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox").Text = text;
        Flush();
    }

    [AvaloniaFact]
    public void Arrows_move_days_wherever_the_focus_is_in_view_mode()
    {
        var (w, vm) = Open("2026-10-14", "2026-10-12", "2026-09-30");
        // フォーカスが一覧のボタンにあっても、ウィンドウの Tunnel で日の移動として効く
        var item = w.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("listitem"));
        item.Focus();
        Flush();
        Press(w, Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft);
        Assert.Equal(new DateOnly(2026, 10, 12), vm.LeftPane.Cursor.SelectedDay);
        Press(w, Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft);
        Assert.Equal(new DateOnly(2026, 9, 30), vm.LeftPane.Cursor.SelectedDay);
        Assert.Equal(new DateOnly(2026, 9, 1), vm.LeftPane.Cursor.DisplayedMonth);   // 月をまたぐと左も追従
        Press(w, Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft);           // 端：何もしない
        Assert.Equal(new DateOnly(2026, 9, 30), vm.LeftPane.Cursor.SelectedDay);
        Press(w, Key.Right, RawInputModifiers.None, PhysicalKey.ArrowRight);
        Assert.Equal(new DateOnly(2026, 10, 12), vm.LeftPane.Cursor.SelectedDay);
    }

    [AvaloniaFact]
    public void Arrows_in_edit_mode_move_the_caret_not_the_day()
    {
        var (w, vm) = Open("2026-10-14", "2026-10-12");
        vm.Editor.BeginEdit();
        Flush();
        var box = w.GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox");
        box.Focus();
        box.CaretIndex = 3;
        Flush();
        Press(w, Key.Left, RawInputModifiers.None, PhysicalKey.ArrowLeft);
        Assert.Equal(Today, vm.LeftPane.Cursor.SelectedDay);
        Assert.Equal(2, box.CaretIndex);
    }

    [AvaloniaFact]
    public void Mod_e_toggles_between_view_and_edit_exactly_once_per_press()
    {
        var (w, vm) = Open("2026-10-14");
        Press(w, Key.E, CommandRaw, PhysicalKey.E);
        Assert.True(vm.Editor.IsEditing);
        Thread.Sleep((int)MainViewModel.DoubleDeliveryWindowMs + 20);
        Press(w, Key.E, CommandRaw, PhysicalKey.E);
        Assert.True(vm.Editor.IsViewing);
    }

    [AvaloniaFact]
    public void Closing_clean_just_closes()
    {
        var (w, vm) = Open("2026-10-14");
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Close();
        Flush();
        Assert.True(closed);
        Assert.Equal(0, _ui.AskSaveCount);
    }

    [AvaloniaFact]
    public void Closing_dirty_asks_and_cancel_keeps_the_window()
    {
        var (w, vm) = Open("2026-10-14");
        Type(w, vm, "changed");
        _ui.SaveAnswer = SaveChoice.Cancel;
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Close();
        Flush();
        Assert.False(closed);
        Assert.Equal(1, _ui.AskSaveCount);
        Assert.Equal("changed", vm.Editor.Draft);
    }

    [AvaloniaFact]
    public void Closing_dirty_save_writes_then_closes()
    {
        var (w, vm) = Open("2026-10-14");
        Type(w, vm, "saved on close");
        _ui.SaveAnswer = SaveChoice.Save;
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Close();
        Flush();
        Assert.True(closed);
        Assert.Equal("saved on close", File.ReadAllText(new DayStore(_t.Path).PathOf(Today)));
    }

    [AvaloniaFact]
    public void Closing_dirty_discard_closes_without_writing()
    {
        var (w, vm) = Open("2026-10-14");
        var before = File.ReadAllText(new DayStore(_t.Path).PathOf(Today));
        Type(w, vm, "never saved");
        _ui.SaveAnswer = SaveChoice.Discard;
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Close();
        Flush();
        Assert.True(closed);
        Assert.Equal(before, File.ReadAllText(new DayStore(_t.Path).PathOf(Today)));
    }

    [AvaloniaFact]
    public void Closing_again_while_confirming_does_not_open_a_second_dialog()
    {
        var (w, vm) = Open("2026-10-14");
        Type(w, vm, "changed");
        _ui.SaveAnswer = SaveChoice.Cancel;
        _ui.WhileAsking = () => w.Close();   // 確認が出ている間に、もう一度閉じようとする
        w.Close();
        Flush();
        Assert.Equal(1, _ui.AskSaveCount);
    }
}
