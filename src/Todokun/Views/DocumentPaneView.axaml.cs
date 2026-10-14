using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Todokun.Core.Editing;
using Todokun.Presentation;

namespace Todokun.Views;

/// <summary>
/// 文書のペイン（TODO の右ペイン・メモタブ共通。要件 8.3・8.4・10）。編集補助のキー（Tab・Shift+Tab・Ctrl/Cmd+Enter・Enter）は <c>TextBox</c> の Tunnel で受けて、
/// Core の純粋関数の結果を<b>範囲の置換</b>（<c>SelectedText</c>）で適用する（Undo が 1 操作で戻る。P0）。
/// </summary>
public partial class DocumentPaneView : UserControl
{
    private DocumentEditorSession? _session;
    private bool _syncing;   // 画面→VM の TextChanged を、VM→画面の書き込みのときは無視する

    public DocumentPaneView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        AddHandler(KeyDownEvent, OnPaneKeyDown, RoutingStrategies.Tunnel);
        EditBox.AddHandler(KeyDownEvent, OnEditKeyDownTunnel, RoutingStrategies.Tunnel);
        EditBox.AddHandler(KeyDownEvent, OnEditKeyDownBubble, RoutingStrategies.Bubble);
        EditBox.TextChanged += OnTextChanged;
        SaveLabel.Text = $"保存　{(CommandModifiers == KeyModifiers.Meta ? "Cmd" : "Ctrl")}+S";
    }

    private static KeyModifiers CommandModifiers =>
        Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_session is not null) _session.PropertyChanged -= OnSessionChanged;
        _session = (DataContext as IDocumentPane)?.Editor;
        if (_session is not null) _session.PropertyChanged += OnSessionChanged;
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(DocumentEditorSession.Mode) || _session is null || !_session.IsEditing) return;
        // 編集に入った：入力欄に内容を入れ、フォーカス・カーソル先頭
        _syncing = true;
        EditBox.Text = _session.Draft;
        _syncing = false;
        EditBox.CaretIndex = 0;
        Dispatcher.UIThread.Post(() => EditBox.Focus());
    }

    private void OnTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncing || _session is null || !_session.IsEditing) return;
        _session.SetDraft(EditBox.Text ?? "");
    }

    // 保存：編集中だけ（Ctrl+S／mac は Cmd+S）。TextBox が先に食わないよう Tunnel で受ける
    private void OnPaneKeyDown(object? sender, KeyEventArgs e)
    {
        if (_session is null || !_session.IsEditing) return;
        if (e.Key == Key.S && e.KeyModifiers == CommandModifiers)
        {
            e.Handled = true;
            _session.SaveCommand.Execute(null);
        }
    }

    // Esc：ビューに戻る。Bubble・未処理のときだけ。IME の変換を取り消す Esc は、IME が食うか ImeProcessed で届くので抜けない
    private void OnEditKeyDownBubble(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled && _session is not null)
        {
            e.Handled = true;
            _session.BackToViewCommand.Execute(null);
        }
    }

    private void OnEditKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.ImeProcessed) return;   // IME が処理したキーは扱わない（変換確定の Enter で項目を増やさない）
        var cmd = CommandModifiers;
        var mods = e.KeyModifiers;
        EditResult? result;
        bool consume;   // 結果が無くてもキーを食うか
        var s = new EditState(EditBox.Text ?? "", EditBox.CaretIndex, EditBox.SelectionStart, EditBox.SelectionEnd);

        if (e.Key == Key.Tab && mods == KeyModifiers.None)
        {
            result = ListEditing.Indent(s);
            consume = true;   // フォーカス移動はしない
        }
        else if (e.Key == Key.Tab && mods == KeyModifiers.Shift)
        {
            result = ListEditing.Outdent(s);
            consume = true;
        }
        else if (e.Key == Key.Enter && mods == cmd)
        {
            result = ListEditing.ToggleCheck(s);
            consume = true;   // 改行を入れない
        }
        else if (e.Key == Key.Enter && mods == KeyModifiers.None)
        {
            result = ListEditing.ContinueItem(s);
            consume = false;   // null なら通常の改行（TextBox に任せる）
        }
        else
        {
            return;
        }

        if (result is not null) Apply(result);
        if (result is not null || consume) e.Handled = true;
    }

    /// <summary>連続した 1 か所の置換を、選択範囲の置換として適用する。</summary>
    private void Apply(EditResult r)
    {
        EditBox.SelectionStart = r.Start;
        EditBox.SelectionEnd = r.Start + r.Length;
        EditBox.SelectedText = r.Replacement;
        if (r.SelStart == r.SelEnd)
        {
            EditBox.CaretIndex = r.Caret;
        }
        else
        {
            EditBox.SelectionStart = r.SelStart;
            EditBox.SelectionEnd = r.SelEnd;
            EditBox.CaretIndex = r.Caret;
        }
    }
}
