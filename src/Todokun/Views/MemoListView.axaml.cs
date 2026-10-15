using Avalonia.Controls;
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
            if (_vm is not null) _vm.FocusSearchRequested -= FocusSearch;
            _vm = DataContext as MemoListViewModel;
            if (_vm is not null) _vm.FocusSearchRequested += FocusSearch;
        };
    }

    /// <summary>Ctrl/Cmd+F：絞り込み欄にフォーカスして全選択。</summary>
    private void FocusSearch()
    {
        MemoSearch.Focus();
        MemoSearch.SelectAll();
    }
}
