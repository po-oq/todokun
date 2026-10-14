using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Todokun.Core;
using Todokun.Presentation;
using Todokun.Tests;
using Todokun.Views;

namespace Todokun.UiTests;

public sealed class MemoTabTests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();

    public void Dispose() => _t.Dispose();

    private static RawInputModifiers CommandRaw =>
        Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers == KeyModifiers.Meta
            ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private (MainWindow W, MainViewModel Vm) Open()
    {
        var store = new DayStore(_t.Path);
        store.Write(Today, "- [ ] a");
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

    private static T Find<T>(Window w, string name) where T : Control =>
        w.GetVisualDescendants().OfType<T>().First(x => x.Name == name);

    [AvaloniaFact]
    public void Starts_on_the_todo_tab_with_the_memo_hidden()
    {
        var (w, _) = Open();
        Assert.True(Find<Grid>(w, "Root").IsVisible);
        Assert.False(Find<DocumentPaneView>(w, "MemoPane").IsVisible);
        Assert.Equal(0, Find<TabStrip>(w, "MainTabs").SelectedIndex);
    }

    [AvaloniaFact]
    public void Selecting_the_memo_tab_swaps_the_content_and_keeps_both_alive()
    {
        var (w, vm) = Open();
        Find<TabStrip>(w, "MainTabs").SelectedIndex = 1;
        Flush();
        Assert.Equal(AppTab.Memo, vm.SelectedTab);
        Assert.False(Find<Grid>(w, "Root").IsVisible);
        Assert.True(Find<DocumentPaneView>(w, "MemoPane").IsVisible);
        Find<TabStrip>(w, "MainTabs").SelectedIndex = 0;
        Flush();
        Assert.True(Find<Grid>(w, "Root").IsVisible);
        Assert.False(Find<DocumentPaneView>(w, "MemoPane").IsVisible);
    }

    [AvaloniaFact]
    public void Viewmodel_tab_change_moves_the_strip()
    {
        var (w, vm) = Open();
        vm.SelectTab(AppTab.Memo);
        Flush();
        Assert.Equal(1, Find<TabStrip>(w, "MainTabs").SelectedIndex);
        Assert.True(Find<DocumentPaneView>(w, "MemoPane").IsVisible);
    }

    [AvaloniaFact]
    public void Memo_edit_and_save_stay_in_edit_and_write_the_file()
    {
        var (w, vm) = Open();
        vm.SelectTab(AppTab.Memo);
        Flush();
        vm.Memo.Editor.BeginEdit();
        Flush();
        var box = Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox");
        box.Text = "- [ ] buy milk";
        Flush();
        w.KeyPress(Key.S, CommandRaw, PhysicalKey.S, null);
        Flush();
        Assert.True(vm.Memo.Editor.IsEditing);
        Assert.Equal("- [ ] buy milk", File.ReadAllText(Path.Combine(_t.Path, "memo.md")));
    }

    [AvaloniaFact]
    public void Memo_pane_shows_the_memo_header()
    {
        var (w, vm) = Open();
        vm.SelectTab(AppTab.Memo);
        Flush();
        var header = Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBlock>().First(x => x.Name == "DayText");
        Assert.Equal("メモ", header.Text);
    }

    [AvaloniaFact]
    public void Closing_with_a_dirty_memo_asks_and_cancel_keeps_the_window()
    {
        var (w, vm) = Open();
        vm.SelectTab(AppTab.Memo);
        Flush();
        vm.Memo.Editor.BeginEdit();
        Flush();
        // 実際の入力と同じく、入力欄の文字を変える（Draft だけを変えると、入力欄の遅れた TextChanged が戻してしまう）
        Find<DocumentPaneView>(w, "MemoPane").GetVisualDescendants().OfType<TextBox>().First(x => x.Name == "EditBox").Text = "changed";
        Flush();
        Assert.True(vm.Memo.Editor.IsDirty);
        _ui.SaveAnswer = SaveChoice.Cancel;
        var closed = false;
        w.Closed += (_, _) => closed = true;
        w.Close();
        Flush();
        Assert.False(closed);
        Assert.Equal(1, _ui.AskSaveCount);
        Assert.Equal("メモが保存されていません。保存しますか？", _ui.LastAskSaveMessage);

        _ui.SaveAnswer = SaveChoice.Discard;
        w.Close();
        Flush();
        Assert.True(closed);
    }

    /// <summary>目視確認用：環境変数 TODOKUN_SHOT_DIR があるときだけ、メモタブのライト・ダークの画像を書き出す。</summary>
    [AvaloniaFact]
    public void Screenshots_when_requested()
    {
        var dir = Environment.GetEnvironmentVariable("TODOKUN_SHOT_DIR");
        if (string.IsNullOrEmpty(dir)) return;
        new MemoStore(_t.Path).Write("# メモ\n\n- [ ] 牛乳を買う\n- [x] 荷造り\n\n普通の文章です。");
        var (w, vm) = Open();
        vm.SelectTab(AppTab.Memo);
        var app = Application.Current!;
        foreach (var (name, v) in new[] { ("light", ThemeVariant.Light), ("dark", ThemeVariant.Dark) })
        {
            app.RequestedThemeVariant = v;
            vm.Memo.Editor.LeaveEditAsync().GetAwaiter().GetResult();
            Flush();
            Save(w, Path.Combine(dir, $"memo-view-{name}.png"));
            vm.Memo.Editor.BeginEdit();
            Flush();
            Save(w, Path.Combine(dir, $"memo-edit-{name}.png"));
        }
        app.RequestedThemeVariant = ThemeVariant.Default;
    }

#pragma warning disable CS0618
    private static void Save(Window w, string path)
    {
        Flush();
        w.CaptureRenderedFrame()!.Save(path);
    }
#pragma warning restore CS0618
}
