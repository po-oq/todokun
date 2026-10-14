using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>
/// ウィンドウ全体の ViewModel。日の選択は「要求 → 確認 → 確定」（計画 11.3）：左ペイン・左右キーの要求を受け、
/// 編集中なら <see cref="DayEditorSession"/> の確認を通ってから <see cref="DayCursor"/> を更新する。
/// プレビュー（<see cref="IPreviewHost"/>）の持ち主でもある。
/// </summary>
public sealed class MainViewModel : IPreviewHost
{
    private readonly DayIndex _index;
    private readonly IUiServices _ui;
    private readonly Func<bool> _isDark;
    private readonly Func<long> _nowMs;
    private long _beganEditAt = long.MinValue;
    private PreviewSource? _current;

    /// <summary>別の経路から同じキーが届いたとみなす間（ms）。P0：2 経路の遅れは 0.2〜0.5ms（最初の数回は 5〜34ms）。</summary>
    public const long DoubleDeliveryWindowMs = 50;

    public MainViewModel(
        DayIndex index, DayStore store, DateOnly today, string dataRoot, bool isCustomDataRoot, IUiServices ui,
        string previewDir, Func<bool>? isDark = null, Func<DateOnly>? todayProvider = null,
        Func<(bool CanWrite, string? Reason)>? dataWritable = null, string? previewUnavailableReason = null,
        string? startupNotice = null, Func<long>? nowMs = null)
    {
        _index = index;
        _ui = ui;
        _isDark = isDark ?? (() => false);
        _nowMs = nowMs ?? (() => Environment.TickCount64);
        PreviewDir = previewDir;
        PreviewUnavailableReason = previewUnavailableReason;
        LeftPane = new LeftPaneViewModel(index, today, todayProvider);
        Editor = new DayEditorSession(store, index, ui, dataWritable);
        // 既定以外のデータルート（--data-dir 指定時）はタイトルバーに出す（要件 8.1）
        WindowTitle = isCustomDataRoot ? $"todokun — {dataRoot}" : "todokun";

        Editor.ContentChanged += reload =>
        {
            _current = Editor.Preview;
            PreviewChanged?.Invoke(_current, reload);
        };
        LeftPane.Cursor.OpenDayRequested += day => _ = OpenDayAsync(day);
        Editor.Open(today);
        // 起動時のお知らせ（コピー失敗・データルートに書けない）。次に日を開く・編集に入るまで出す
        if (startupNotice is not null) Editor.Message = startupNotice;
    }

    public LeftPaneViewModel LeftPane { get; }
    public DayEditorSession Editor { get; }
    public string WindowTitle { get; }

    /// <summary>日を開く：要求 → 確認 → 確定。キャンセルなら選択は変えない。</summary>
    public async Task<bool> OpenDayAsync(DateOnly day)
    {
        if (Editor.IsConfirming) return false;
        if (day == LeftPane.Cursor.SelectedDay && Editor.Day == day)
        {
            LeftPane.Cursor.Show(day);   // 表示月だけ戻す（同じ日を押したとき）
            return true;
        }
        if (!await Editor.TryOpenDayAsync(day)) return false;
        LeftPane.Cursor.Show(day);
        return true;
    }

    // ── IPreviewHost ──
    public string PreviewDir { get; }
    public bool IsDark => _isDark();
    public string? PreviewUnavailableReason { get; }
    public string RuntimeMissingNote => "編集と保存は、WebView2 Runtime が無くても使えます（ビューだけが出ません）。";
    public PreviewSource? CurrentPreview => _current ??= Editor.Preview;
    public event Action<PreviewSource?, bool>? PreviewChanged;

    public void Log(string message) => AppLog.Write(message);

    /// <summary>テーマが変わった：同じ内容を作り直す（スクロール位置は部品が保つ）。</summary>
    public void OnThemeChanged() => PreviewChanged?.Invoke(CurrentPreview, true);

    public void OpenLocalLink(string fullPath)
    {
        if (!LocalLinkRule.IsUsableLocalPath(fullPath, OperatingSystem.IsWindows())) return;
        switch (LocalLinkRule.Decide(fullPath, OperatingSystem.IsWindows(), LinkProbe.Real))
        {
            case LocalLinkAction.OpenWithDefaultApp:
                _ui.OpenWithDefaultApp(fullPath);
                break;
            case LocalLinkAction.Reveal:
                _ui.RevealInFileManager(fullPath);
                break;
        }
    }

    public void OnPageKey(PageKey key)
    {
        if (Editor.IsConfirming) return;
        switch (key)
        {
            case PageKey.BeginEdit:
                if (Editor.IsViewing && Editor.BeginEdit()) _beganEditAt = _nowMs();   // 冪等。ページにキーが届くのはビューモードのときだけ
                break;
            case PageKey.ArrowLeft or PageKey.ArrowRight when Editor.IsViewing:
                var cur = Editor.Day;
                var target = key == PageKey.ArrowLeft ? _index.Previous(cur) : _index.Next(cur);
                if (target is { } d) LeftPane.Cursor.RequestOpenDay(d);
                break;
        }
    }

    /// <summary>
    /// ウィンドウの Tunnel で受けたキー（計画 11.6）。処理したら true（画面は <c>Handled</c> にする）。
    /// <paramref name="fromWebView"/>：KeyDown の <c>e.Source</c> またはフォーカスが WebView。
    /// </summary>
    public bool OnWindowKey(AppKey key, bool fromWebView)
    {
        if (Editor.IsConfirming) return false;
        switch (key)
        {
            case AppKey.Left or AppKey.Right:
                // 編集モードの ←→ は TextBox に任せる。WebView にフォーカスがあるときはページ経路だけ（一本化の A）
                if (!Editor.IsViewing || fromWebView) return false;
                var cur = Editor.Day;
                var target = key == AppKey.Left ? _index.Previous(cur) : _index.Next(cur);
                if (target is { } d) LeftPane.Cursor.RequestOpenDay(d);
                return true;   // 端でも食う（一覧・ボタンのフォーカス移動に使わせない）
            case AppKey.ModE:
                if (Editor.IsViewing)
                {
                    // ビューモードの Mod+E は常に「編集に入る」（冪等）。mac ではページに届かず、この経路だけが頼り（P0）
                    if (Editor.BeginEdit()) _beganEditAt = _nowMs();
                    return true;
                }
                // 編集中：ページ経路の BeginEdit の直後（同じ押下の二重到達）なら、戻さない
                if (_nowMs() - _beganEditAt < DoubleDeliveryWindowMs) return true;
                _ = Editor.LeaveEditAsync();
                return true;
        }
        return false;
    }
}

/// <summary>ウィンドウ全体で受けるキー。</summary>
public enum AppKey
{
    Left,
    Right,
    /// <summary>Ctrl+E（mac は Cmd+E）。</summary>
    ModE,
}
