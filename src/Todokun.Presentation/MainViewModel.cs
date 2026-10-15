using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>
/// ウィンドウ全体の ViewModel。日の選択は「要求 → 確認 → 確定」（計画 11.3）：左ペイン・左右キーの要求を受け、
/// 編集中なら <see cref="DayEditorSession"/> の確認を通ってから <see cref="DayCursor"/> を更新する。
/// プレビュー（<see cref="IPreviewHost"/>）の持ち主でもある。
/// </summary>
public sealed class MainViewModel : IDocumentPane
{
    private readonly DayIndex _index;
    private readonly IUiServices _ui;
    private readonly Func<bool> _isDark;
    private readonly ModEToggle _modE;
    private PreviewSource? _current;
    private AppTab _selectedTab = AppTab.Todo;

    /// <summary>別の経路から同じキーが届いたとみなす間（ms）。P0：2 経路の遅れは 0.2〜0.5ms（最初の数回は 5〜34ms）。</summary>
    public const long DoubleDeliveryWindowMs = ModEToggle.WindowMs;

    public MainViewModel(
        DayIndex index, DayStore store, DateOnly today, string dataRoot, bool isCustomDataRoot, IUiServices ui,
        string previewDir, Func<bool>? isDark = null, Func<DateOnly>? todayProvider = null,
        Func<(bool CanWrite, string? Reason)>? dataWritable = null, string? previewUnavailableReason = null,
        string? startupNotice = null, Func<long>? nowMs = null, MemoLibrary? memoLibrary = null, MemoIndex? memoIndex = null,
        string? memoNotice = null)
    {
        _index = index;
        _ui = ui;
        _isDark = isDark ?? (() => false);
        var now = nowMs ?? (() => Environment.TickCount64);
        PreviewDir = previewDir;
        PreviewUnavailableReason = previewUnavailableReason;
        LeftPane = new LeftPaneViewModel(index, today, todayProvider);
        Editor = new DayEditorSession(store, index, ui, dataWritable);
        _modE = new ModEToggle(Editor, now);
        // 既定以外のデータルート（--data-dir 指定時）はタイトルバーに出す（要件 8.1）
        WindowTitle = isCustomDataRoot ? $"todokun — {dataRoot}" : "todokun";

        var library = memoLibrary ?? new MemoLibrary(dataRoot);
        var memos = memoIndex ?? new MemoIndex(library);
        var memoEditor = new MemoEditorSession(library, memos, ui, dataWritable);
        var memoList = new MemoListViewModel(library, memos, memoEditor, ui);
        Memo = new MemoPaneViewModel(memoEditor, memoList, previewDir, _isDark, previewUnavailableReason, OpenLocalLink, now);

        Editor.ContentChanged += reload =>
        {
            _current = Editor.Preview;
            PreviewChanged?.Invoke(_current, reload);
        };
        Editor.ContentUpdatedInPlace += () => _current = Editor.Preview;   // チェックボックスのクリック（作り直さない）
        LeftPane.Cursor.OpenDayRequested += day => _ = OpenDayAsync(day);
        Editor.Open(today);
        // 起動時は一覧の先頭（更新が新しいメモ）。無ければ「メモなし」。（最後に開いたメモは 2-5）
        var first = memos.Items.FirstOrDefault();
        if (first is not null) memoEditor.Open(first.Title);
        else memoEditor.Clear();
        // 起動時のお知らせ（コピー失敗・データルートに書けない）。次に日を開く・編集に入るまで出す
        if (startupNotice is not null) Editor.Message = startupNotice;
        // メモの引き継ぎのお知らせはメモタブに出す（TODO 側のお知らせとは別）
        if (memoNotice is not null) memoEditor.Message = memoNotice;
    }

    public LeftPaneViewModel LeftPane { get; }
    public DayEditorSession Editor { get; }
    DocumentEditorSession IDocumentPane.Editor => Editor;
    public MemoPaneViewModel Memo { get; }
    public string WindowTitle { get; }

    /// <summary>いま見えているタブ（保存しない。起動時は常に TODO）。</summary>
    public AppTab SelectedTab => _selectedTab;

    public event Action<AppTab>? SelectedTabChanged;

    public void SelectTab(AppTab tab)
    {
        if (_selectedTab == tab) return;
        _selectedTab = tab;
        SelectedTabChanged?.Invoke(tab);
    }

    /// <summary>どちらかの確認ダイアログが出ている。</summary>
    public bool AnyConfirming => Editor.IsConfirming || Memo.Editor.IsConfirming;

    /// <summary>どちらかに未保存の変更がある。</summary>
    public bool AnyDirty => Editor.IsDirty || Memo.Editor.IsDirty;

    /// <summary>
    /// ウィンドウを閉じてよいか。TODO → メモの順に「保存しますか？」を出す（未保存の側のタブを先に見せる）。
    /// どちらかがキャンセル・保存の失敗なら false。
    /// </summary>
    public async Task<bool> CanCloseAsync()
    {
        if (Editor.IsDirty)
        {
            SelectTab(AppTab.Todo);
            if (!await Editor.CanCloseAsync()) return false;
        }
        if (Memo.Editor.IsDirty)
        {
            SelectTab(AppTab.Memo);
            if (!await Memo.Editor.CanCloseAsync()) return false;
        }
        return true;
    }

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
    public void OnThemeChanged()
    {
        PreviewChanged?.Invoke(CurrentPreview, true);
        Memo.OnThemeChanged();
    }

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

    public void OnCheckboxToggled(int line, bool isChecked) => Editor.ToggleCheck(line, isChecked);

    public void OnPageKey(PageKey key)
    {
        if (Editor.IsConfirming) return;
        switch (key)
        {
            case PageKey.BeginEdit:
                _modE.OnPageBeginEdit();   // 冪等。ページにキーが届くのはビューモードのときだけ
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
    /// <paramref name="memoTarget"/>：F2・Delete の対象（フォーカスのある一覧の項目のタイトル。無ければ開いているメモ）。
    /// </summary>
    public bool OnWindowKey(AppKey key, bool fromWebView, string? memoTarget = null)
    {
        var onMemo = SelectedTab == AppTab.Memo;
        if ((onMemo ? Memo.Editor : (DocumentEditorSession)Editor).IsConfirming) return false;
        switch (key)
        {
            case AppKey.Left or AppKey.Right:
                if (onMemo) return false;   // 日の移動は TODO タブだけ
                // 編集モードの ←→ は TextBox に任せる。WebView にフォーカスがあるときはページ経路だけ（一本化の A）
                if (!Editor.IsViewing || fromWebView) return false;
                var cur = Editor.Day;
                var target = key == AppKey.Left ? _index.Previous(cur) : _index.Next(cur);
                if (target is { } d) LeftPane.Cursor.RequestOpenDay(d);
                return true;   // 端でも食う（一覧・ボタンのフォーカス移動に使わせない）
            case AppKey.NewMemo or AppKey.FindMemo or AppKey.RenameMemo or AppKey.DeleteMemo:
                return onMemo && Memo.OnWindowKey(key, fromWebView, memoTarget);
            case AppKey.ModE:
                // 見えているタブだけに送る。ビューモードでは常に「編集に入る」（冪等。mac ではページに届かず、この経路だけが頼り：P0）
                return onMemo ? Memo.OnModE() : _modE.OnModE();
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
    /// <summary>Ctrl/Cmd+N：メモを追加（メモタブだけ。WebView にフォーカスがあっても扱う）。</summary>
    NewMemo,
    /// <summary>Ctrl/Cmd+F：メモの絞り込み欄へ（メモタブだけ。WebView にフォーカスがあっても扱う）。</summary>
    FindMemo,
    /// <summary>F2：メモの名前の変更（メモタブだけ。WebView にフォーカスがあるときは扱わない）。</summary>
    RenameMemo,
    /// <summary>Delete（mac は Cmd+⌫ も）：メモの削除（メモタブだけ。WebView にフォーカスがあるときは扱わない）。</summary>
    DeleteMemo,
}

/// <summary>ウィンドウ上部のタブ。</summary>
public enum AppTab
{
    Todo,
    Memo,
}
