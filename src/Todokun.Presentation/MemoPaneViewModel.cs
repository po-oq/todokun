using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>メモタブの ViewModel。プレビュー（<see cref="IPreviewHost"/>）の持ち主でもある。</summary>
public sealed class MemoPaneViewModel : IDocumentPane
{
    private readonly Func<bool> _isDark;
    private readonly Action<string> _openLocalLink;
    private readonly ModEToggle _modE;
    private PreviewSource? _current;

    private readonly Func<long> _nowMs;
    private long? _lastNewMemoAt;
    private long? _lastFindMemoAt;

    public MemoPaneViewModel(MemoEditorSession editor, MemoListViewModel list, string previewDir, Func<bool> isDark,
        string? previewUnavailableReason, Action<string> openLocalLink, Func<long> nowMs)
    {
        Editor = editor;
        List = list;
        _nowMs = nowMs;
        PreviewDir = previewDir;
        PreviewUnavailableReason = previewUnavailableReason;
        _isDark = isDark;
        _openLocalLink = openLocalLink;
        _modE = new ModEToggle(editor, nowMs);
        editor.ContentChanged += reload =>
        {
            _current = editor.Preview;
            PreviewChanged?.Invoke(_current, reload);
        };
        // チェックボックスのクリック：ページは作り直さない（見た目は先に変わっている）。次の描画のために持ち直すだけ
        editor.ContentUpdatedInPlace += () => _current = editor.Preview;
    }

    public MemoEditorSession Editor { get; }

    /// <summary>左ペインの一覧。</summary>
    public MemoListViewModel List { get; }
    DocumentEditorSession IDocumentPane.Editor => Editor;

    /// <summary>見出しのクリックで名前を変えられる（開いているメモがあるとき）。</summary>
    public bool CanRenameHeader => Editor.Title is not null;

    /// <summary>見出しのクリック：開いているメモの名前の変更を始める（一覧の項目がその場で入力欄になる）。</summary>
    public void BeginRenameHeader()
    {
        if (Editor.Title is { } title) List.BeginRename(title);
    }

    // ── IPreviewHost ──
    public string PreviewDir { get; }
    public bool IsDark => _isDark();
    public string? PreviewUnavailableReason { get; }
    public string RuntimeMissingNote => "編集と保存は、WebView2 Runtime が無くても使えます（ビューだけが出ません）。";
    public PreviewSource? CurrentPreview => _current ??= Editor.Preview;
    public event Action<PreviewSource?, bool>? PreviewChanged;

    public void Log(string message) => AppLog.Write(message);

    public void OpenLocalLink(string fullPath) => _openLocalLink(fullPath);

    /// <summary>メモタブのページからのキー。<c>←→</c> は日の移動なので無視し、編集に入る・追加・絞り込みを受ける。</summary>
    public void OnPageKey(PageKey key)
    {
        if (Editor.IsConfirming) return;
        switch (key)
        {
            case PageKey.BeginEdit:
                _modE.OnPageBeginEdit();
                break;
            case PageKey.NewMemo:
                NewMemo();
                break;
            case PageKey.FindMemo:
                FindMemo();
                break;
        }
    }

    /// <summary>ウィンドウで受けたメモのキー（メモタブが見えているとき）。処理したら true。</summary>
    public bool OnWindowKey(AppKey key, bool fromWebView, string? target)
    {
        if (Editor.IsConfirming) return false;
        switch (key)
        {
            case AppKey.NewMemo:
                NewMemo();   // WebView にフォーカスがあっても効く（ページ経路との重複は NewMemo が捨てる）
                return true;
            case AppKey.FindMemo:
                FindMemo();
                return true;
            case AppKey.RenameMemo or AppKey.DeleteMemo:
                if (fromWebView) return false;   // 一覧の項目をクリックしてから使う
                var title = target ?? Editor.Title;
                if (title is null) return false;
                if (key == AppKey.RenameMemo) List.BeginRename(title);
                else _ = List.DeleteAsync(title);
                return true;
        }
        return false;
    }

    // Ctrl/Cmd+N は冪等でない（2 経路から届くと 2 つできる）ので、短い間の同じキーは 1 回とみなす（ModEToggle と同じ間）。
    private void NewMemo()
    {
        var now = _nowMs();
        if (now - _lastNewMemoAt < ModEToggle.WindowMs) return;   // 初回は null（比べない）
        _lastNewMemoAt = now;
        _ = List.AddAsync();
    }

    private void FindMemo()
    {
        var now = _nowMs();
        if (now - _lastFindMemoAt < ModEToggle.WindowMs) return;
        _lastFindMemoAt = now;
        List.RequestFocusSearch();
    }

    public void OnCheckboxToggled(int line, bool isChecked) => Editor.ToggleCheck(line, isChecked);

    /// <summary>ウィンドウの Mod+E（メモタブが見えているとき）。</summary>
    public bool OnModE() => _modE.OnModE();

    /// <summary>テーマが変わった：同じ内容を作り直す。</summary>
    public void OnThemeChanged() => PreviewChanged?.Invoke(CurrentPreview, true);
}
