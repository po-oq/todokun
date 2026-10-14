using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>メモタブの ViewModel。プレビュー（<see cref="IPreviewHost"/>）の持ち主でもある。</summary>
public sealed class MemoPaneViewModel : IDocumentPane
{
    private readonly Func<bool> _isDark;
    private readonly Action<string> _openLocalLink;
    private readonly ModEToggle _modE;
    private PreviewSource? _current;

    public MemoPaneViewModel(MemoEditorSession editor, string previewDir, Func<bool> isDark,
        string? previewUnavailableReason, Action<string> openLocalLink, Func<long> nowMs)
    {
        Editor = editor;
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
    }

    public MemoEditorSession Editor { get; }
    DocumentEditorSession IDocumentPane.Editor => Editor;

    // ── IPreviewHost ──
    public string PreviewDir { get; }
    public bool IsDark => _isDark();
    public string? PreviewUnavailableReason { get; }
    public string RuntimeMissingNote => "編集と保存は、WebView2 Runtime が無くても使えます（ビューだけが出ません）。";
    public PreviewSource? CurrentPreview => _current ??= Editor.Preview;
    public event Action<PreviewSource?, bool>? PreviewChanged;

    public void Log(string message) => AppLog.Write(message);

    public void OpenLocalLink(string fullPath) => _openLocalLink(fullPath);

    /// <summary>メモタブのページからのキー。<c>←→</c> は日の移動なので無視し、編集に入るだけを受ける。</summary>
    public void OnPageKey(PageKey key)
    {
        if (Editor.IsConfirming) return;
        if (key == PageKey.BeginEdit) _modE.OnPageBeginEdit();
    }

    /// <summary>ウィンドウの Mod+E（メモタブが見えているとき）。</summary>
    public bool OnModE() => _modE.OnModE();

    /// <summary>テーマが変わった：同じ内容を作り直す。</summary>
    public void OnThemeChanged() => PreviewChanged?.Invoke(CurrentPreview, true);
}
