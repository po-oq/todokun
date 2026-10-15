using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Todokun.Presentation;

namespace Todokun;

public partial class MainWindow : Window
{
    /// <summary>左ペインの最小幅（pt）。月グリッド 7 × 36 ＋ 余白がちょうど収まる幅。</summary>
    public const double LeftPaneMinWidth = 272;

    /// <summary>メモの一覧（左ペイン）の最小幅（pt）。</summary>
    public const double MemoListMinWidth = 200;

    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        Root.ColumnDefinitions[0].MinWidth = LeftPaneMinWidth;
        MemoRoot.ColumnDefinitions[0].MinWidth = MemoListMinWidth;
        // Tunnel：ListBox・ボタン・カレンダーが矢印キーを先に食わないように、ウィンドウ全体で先に受ける（計画 11.6）。
        // Miharikun の既存の受け口は Bubble（Esc だけ）なので先例にならない。
        AddHandler(KeyDownEvent, OnKeyDownTunnel, RoutingStrategies.Tunnel);
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.SelectedTabChanged += OnVmTabChanged;
        };
        // 起動時間の記録（要件 12 章：3 秒以内）。プロセスの開始からウィンドウが出るまで。ログに本文は書かない
        Opened += (_, _) =>
        {
            try
            {
                var ms = (long)(DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalMilliseconds;
                Todokun.Core.AppLog.Write($"起動: ウィンドウが出るまで {ms} ms");
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        };
    }

    // タブの切替：画面 → VM と VM → 画面（閉じる確認で未保存の側を見せるとき）。値が同じなら何もしないので再入しない
    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.SelectTab(MainTabs.SelectedIndex == 1 ? AppTab.Memo : AppTab.Todo);
        ApplyTab(vm.SelectedTab);
    }

    private void OnVmTabChanged(AppTab tab)
    {
        ApplyTab(tab);
        var index = tab == AppTab.Memo ? 1 : 0;
        if (MainTabs.SelectedIndex != index) MainTabs.SelectedIndex = index;
    }

    private void ApplyTab(AppTab tab)
    {
        Root.IsVisible = tab == AppTab.Todo;
        MemoRoot.IsVisible = tab == AppTab.Memo;
        MemoPane.IsVisible = tab == AppTab.Memo;
    }

    private static KeyModifiers CommandModifiers =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    private void OnKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (e.Key == Key.ImeProcessed) return;   // IME に処理されたキーは扱わない

        AppKey? key = null;
        if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Left) key = AppKey.Left;
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.Right) key = AppKey.Right;
        else if (e.KeyModifiers == CommandModifiers && e.Key == Key.E) key = AppKey.ModE;
        else if (e.KeyModifiers == KeyModifiers.None && e.Key == Key.F2) key = AppKey.RenameMemo;
        if (key is null) return;

        // WebView にフォーカスがあるか：e.Source または FocusManager のどちらかが NativeWebView（P0 で両方とも確認済み）
        var focused = FocusManager?.GetFocusedElement();
        var fromWebView = e.Source is NativeWebView || focused is NativeWebView;

        // F2 は、テキスト入力（編集欄・絞り込み欄・名前の入力欄）では食わない。対象はフォーカスのある一覧の項目、無ければ開いているメモ
        string? memoTarget = null;
        if (key == AppKey.RenameMemo)
        {
            if (focused is TextBox) return;
            memoTarget = (focused as Control)?.DataContext is MemoListItem item ? item.Title : null;
        }
        if (vm.OnWindowKey(key.Value, fromWebView, memoTarget)) e.Handled = true;
    }

    // 閉じる（計画 11.4・6-3）：未保存なら「保存しますか？」。ダイアログは Post で出す（Closing の中で await しない）。
    // Closing は確認が通った後の Close() でも来るので、確定のフラグで 2 回目を通す。
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainViewModel vm) return;
        if (vm.AnyConfirming)
        {
            e.Cancel = true;   // 確認中にもう一度閉じようとした：取り消すだけ（2 つ目のダイアログは出さない）
            return;
        }
        if (!vm.AnyDirty) return;   // 変更が無ければ確認なしで閉じる
        e.Cancel = true;
        Dispatcher.UIThread.Post(async () =>
        {
            if (await vm.CanCloseAsync())
            {
                _closeConfirmed = true;
                Close();
            }
        });
    }
}
