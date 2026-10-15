using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Todokun.Presentation;

namespace Todokun.Views;

public partial class MemoListView : UserControl
{
    private MemoListViewModel? _vm;

    public MemoListView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (_vm is not null)
            {
                _vm.FocusSearchRequested -= FocusSearch;
                _vm.PropertyChanged -= OnVmChanged;
            }
            _vm = DataContext as MemoListViewModel;
            if (_vm is not null)
            {
                _vm.FocusSearchRequested += FocusSearch;
                _vm.PropertyChanged += OnVmChanged;
            }
        };

        // 名前の入力欄（項目のテンプレートの中）のキー・フォーカス・項目のダブルクリックは、ここでまとめて受ける。
        // Enter／Esc は Bubble・未処理のときだけ、IME が処理したキー（変換確定の Enter・変換取消の Esc）は扱わない
        // （編集欄の Esc と同じ流儀）。
        MemoItems.AddHandler(KeyDownEvent, OnRenameKeyDown, RoutingStrategies.Bubble);
        MemoItems.AddHandler(LostFocusEvent, OnRenameLostFocus, RoutingStrategies.Bubble);
        MemoItems.AddHandler(InputElement.DoubleTappedEvent, OnItemDoubleTapped, RoutingStrategies.Bubble);
    }

    /// <summary>Ctrl/Cmd+F：絞り込み欄にフォーカスして全選択。</summary>
    private void FocusSearch()
    {
        MemoSearch.Focus();
        MemoSearch.SelectAll();
    }

    // 名前の変更が始まったら、入力欄にフォーカスして全選択（項目の作り直しが済んだ後）
    private void OnVmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MemoListViewModel.RenamingTitle) || _vm?.RenamingTitle is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            var box = this.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(t => t.Name == "RenameBox" && t.IsVisible);
            if (box is null) return;
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    private static bool IsRenameBox(object? source) => source is TextBox { Name: "RenameBox" };

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Handled || e.Key == Key.ImeProcessed || !IsRenameBox(e.Source)) return;
        if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _vm.CommitRename();
        }
        else if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            e.Handled = true;
            _vm.CancelRename();
        }
    }

    // フォーカスが外れたら確定（打った文字を失わない）。確定は冪等なので、Enter の後に来ても 2 回目は何もしない
    private void OnRenameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (IsRenameBox(e.Source)) _vm?.CommitRename();
    }

    // ダブルクリック：1 回目のクリックでそのメモを開けていたとき（確認でキャンセルされていないとき）だけ名前の変更を始める
    private void OnItemDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null || e.Source is not Control c) return;
        if (c.DataContext is MemoListItem item) _vm.BeginRenameOfOpen(item.Title);
    }
}
