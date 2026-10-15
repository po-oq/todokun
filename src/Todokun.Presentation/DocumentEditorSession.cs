using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Todokun.Core;

namespace Todokun.Presentation;

public enum EditorMode
{
    View,
    Edit,
}

/// <summary>保存の結果。</summary>
public enum SaveResult
{
    /// <summary>書いた。</summary>
    Saved,
    /// <summary>空にして保存し、ファイルを削除した。</summary>
    Deleted,
    /// <summary>何もしなかった（変更なし・ファイルが無い日を空で保存）。</summary>
    NoChange,
    /// <summary>削除の確認でキャンセルされた・確認中・編集中でない。</summary>
    Cancelled,
    /// <summary>書けなかった・消せなかった（理由は <see cref="DocumentEditorSession.Message"/>）。</summary>
    Failed,
}

/// <summary>
/// 1 つの文書（日ファイル・メモ）の編集の状態（計画 11.4）。「読めない」を「ファイルなし」と混ぜない：
/// 読めない文書は編集に入れない（空から書いて保存すると元のファイルを失うため）。
/// 保存先・表示文は派生クラスが決める。画面・ダイアログは <see cref="IUiServices"/> 越し。
/// </summary>
public abstract partial class DocumentEditorSession : ObservableObject
{
    private readonly IUiServices _ui;
    private readonly Func<(bool CanWrite, string? Reason)> _writable;
    protected int _generation;   // 状態が変わるたびに増やす。確認ダイアログの後で、状態が変わっていないかを見る

    [ObservableProperty]
    private DayContent _content = new DayContent.Missing();

    [ObservableProperty]
    private EditorMode _mode = EditorMode.View;

    /// <summary>編集欄の内容。</summary>
    [ObservableProperty]
    private string _draft = "";

    /// <summary>編集を始めた時点（または最後に保存した時点）の内容。</summary>
    [ObservableProperty]
    private string _base = "";

    /// <summary>確認ダイアログを出している間。日の移動・編集への切り替えは無視する。</summary>
    [ObservableProperty]
    private bool _isConfirming;

    /// <summary>画面に出す、保存・削除・編集に入れない理由。</summary>
    [ObservableProperty]
    private string? _message;

    protected DocumentEditorSession(IUiServices ui, Func<(bool CanWrite, string? Reason)>? writable)
    {
        _ui = ui;
        _writable = writable ?? (() => (true, null));
        BeginEditCommand = new RelayCommand(() => BeginEdit(), () => CanBeginEdit);
        SaveCommand = new AsyncRelayCommand(async () => await SaveAsync());
        BackToViewCommand = new AsyncRelayCommand(async () => await LeaveEditAsync());
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CanBeginEdit)) BeginEditCommand.NotifyCanExecuteChanged();
        };
    }

    // ── 派生が決めるもの ──
    /// <summary>バーの見出し（日なら「2026-10-14 (水)」、メモなら「メモ」）。</summary>
    public abstract string HeaderText { get; }

    /// <summary>プレビューに出すもの。</summary>
    public abstract PreviewSource Preview { get; }

    protected abstract DayContent StoreRead();
    protected abstract bool StoreExists();
    /// <summary>書く。失敗は <see cref="IOException"/>・<see cref="UnauthorizedAccessException"/> のまま上げる。</summary>
    protected abstract void StoreWrite(string text);
    protected abstract void StoreDelete();
    /// <summary>書けた直後（<see cref="Content"/> を更新した後）。日なら索引に足す。</summary>
    protected virtual void AfterWritten() { }
    /// <summary>消せた直後。日なら索引から外す。</summary>
    protected virtual void AfterDeleted() { }
    /// <summary>読めない文書を編集に入ろうとしたときの文。</summary>
    protected abstract string UnreadableMessage(string reason);
    protected abstract string AskSaveText { get; }
    protected abstract string ConfirmDeleteText { get; }

    /// <summary>空（空白だけ）で保存したら、ファイルを削除する（確認あり）。false なら空のまま書いて残す（メモ）。</summary>
    protected virtual bool DeleteWhenBlank => true;

    /// <summary>開いている文書があるか。false なら編集・保存・チェックの切り替えは何もしない（「メモなし」）。</summary>
    protected virtual bool HasDocument => true;

    /// <summary><see cref="HasDocument"/> が変わったときに派生が呼ぶ（「編集」ボタンの押せる／押せないを直す）。</summary>
    protected void RefreshCanBeginEdit() => OnPropertyChanged(nameof(CanBeginEdit));

    // ── コマンド（画面の部品用） ──
    public IRelayCommand BeginEditCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand BackToViewCommand { get; }

    /// <summary>表示が変わった（開いた・保存した・削除した）。引数 true は同じものの作り直し。UI スレッドで呼ばれる。</summary>
    public event Action<bool>? ContentChanged;

    /// <summary>
    /// ビューのチェックボックスのクリックで内容が変わった（ページは先に見た目を変えているので、作り直さない）。
    /// 購読側は「いまのプレビュー」を持ち直すだけでよい。
    /// </summary>
    public event Action? ContentUpdatedInPlace;

    // ── 表示用 ──
    public bool IsEditing => Mode == EditorMode.Edit;
    public bool IsViewing => Mode == EditorMode.View;
    public bool IsDirty => Mode == EditorMode.Edit && Norm(Draft) != Norm(Base);
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public string ModeText => IsEditing ? "編集中" : "ビュー";

    /// <summary>「✎ 編集」を押せるか。読めない・データルートが書けないとき・確認中は押せない。</summary>
    public bool CanBeginEdit => HasDocument && IsViewing && !IsConfirming && Content is not DayContent.Failed && _writable().CanWrite;

    public static string Norm(string s) => s.Replace("\r\n", "\n");

    private static bool IsBlank(string s) => string.IsNullOrWhiteSpace(s);

    partial void OnModeChanged(EditorMode value) => RefreshDerived();
    partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnBaseChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnContentChanged(DayContent value) => OnPropertyChanged(nameof(CanBeginEdit));
    partial void OnIsConfirmingChanged(bool value) => OnPropertyChanged(nameof(CanBeginEdit));
    partial void OnMessageChanged(string? value) => OnPropertyChanged(nameof(HasMessage));

    private void RefreshDerived()
    {
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(IsViewing));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(ModeText));
        OnPropertyChanged(nameof(CanBeginEdit));
    }

    /// <summary>読んだ内容をビューモードで出す。派生の「開く」「読み込む」から呼ぶ（呼ぶ側が <c>_generation++</c> を済ませる）。</summary>
    protected void ShowContent(DayContent content)
    {
        Content = content;
        Mode = EditorMode.View;
        Draft = Base = "";
        Message = null;
        ContentChanged?.Invoke(false);
    }

    // ── 編集へ ──

    /// <summary>編集に入る（冪等。既に編集中・確認中なら何もしない）。入れなければ理由を <see cref="Message"/> に出す。</summary>
    public bool BeginEdit()
    {
        if (IsConfirming) return false;
        if (!HasDocument)
        {
            Message = "メモがありません。＋で追加してください";
            return false;
        }
        if (Mode == EditorMode.Edit) return true;
        if (Content is DayContent.Failed failed)
        {
            Message = UnreadableMessage(failed.Reason);
            return false;
        }
        var (canWrite, reason) = _writable();
        if (!canWrite)
        {
            Message = $"データの保存先に書き込めないため、編集できません：{reason}";
            return false;
        }
        _generation++;
        var text = Content is DayContent.Loaded l ? l.Text : "";
        Base = Draft = text;
        Message = null;
        Mode = EditorMode.Edit;
        return true;
    }

    public void SetDraft(string text) => Draft = text;

    // ── 保存 ──

    public async Task<SaveResult> SaveAsync()
    {
        if (IsConfirming) return SaveResult.Cancelled;
        return await SaveCoreAsync();
    }

    private async Task<SaveResult> SaveCoreAsync()
    {
        if (!HasDocument || Mode != EditorMode.Edit) return SaveResult.Cancelled;
        if (!IsDirty) return SaveResult.NoChange;

        if (!IsBlank(Draft) || !DeleteWhenBlank)
        {
            try
            {
                AppLog.Write($"保存: 書き込み開始 ({Draft.Length} 文字)");
                StoreWrite(Draft);
                AppLog.Write("保存: 書き込み完了");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Message = $"保存できませんでした：{ex.Message}";
                return SaveResult.Failed;
            }
            _generation++;
            Content = new DayContent.Loaded(Norm(Draft));
            Base = Draft;
            Message = null;
            AfterWritten();
            AppLog.Write("保存: 表示の更新へ");
            ContentChanged?.Invoke(true);
            AppLog.Write("保存: 表示の更新を依頼した");
            return SaveResult.Saved;
        }

        // 空（空白・改行だけ）で保存
        if (!StoreExists())
        {
            Base = Draft;   // 何も作らない。確認も出さない
            return SaveResult.NoChange;
        }

        var gen = _generation;
        IsConfirming = true;
        bool ok;
        try
        {
            ok = await _ui.ConfirmDeleteAsync(ConfirmDeleteText);
        }
        finally
        {
            IsConfirming = false;
        }
        if (!ok || gen != _generation) return SaveResult.Cancelled;

        try
        {
            StoreDelete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"削除できませんでした：{ex.Message}";
            return SaveResult.Failed;   // Content・索引・Base は変えない
        }
        _generation++;
        Content = new DayContent.Missing();
        Base = Draft;
        Message = null;
        AfterDeleted();
        ContentChanged?.Invoke(true);
        return SaveResult.Deleted;
    }

    // ── ビューのチェックボックス（楽観的更新の裏側） ──

    /// <summary>
    /// ビューのチェックボックスのクリック（<paramref name="line"/> は md の 0 始まりの行、<paramref name="newChecked"/> は押した後の状態）。
    /// ページは先に見た目を変えている。ここでは<b>ファイルを読み直して</b>その行を確かめ、印の 1 文字だけを書く
    /// （表示した後に外で変わっていても、古い内容で上書きしない）。書けたら true。
    /// 書けなかったら理由を <see cref="Message"/> に出し、ページを作り直して見た目を戻す。ビューのときだけ効く。
    /// </summary>
    public bool ToggleCheck(int line, bool newChecked)
    {
        if (!HasDocument || IsConfirming || Mode != EditorMode.View) return false;

        var (canWrite, reason) = _writable();
        if (!canWrite)
        {
            Message = $"データの保存先に書き込めないため、チェックを変えられません：{reason}";
            ContentChanged?.Invoke(true);
            return false;
        }

        var fresh = StoreRead();
        if (fresh is not DayContent.Loaded loaded)
        {
            // 読めない・ファイルが無い（外で変わった）：何も書かず、いまの状態を見せる
            _generation++;
            Content = fresh;
            Message = fresh is DayContent.Failed failed
                ? $"チェックボックスを更新できませんでした。ファイルを読めません：{failed.Reason}"
                : "チェックボックスを更新できませんでした。ファイルが見つかりません";
            ContentChanged?.Invoke(true);
            return false;
        }

        var result = TaskToggle.Toggle(loaded.Text, line, newChecked);
        switch (result.Status)
        {
            case TaskToggleStatus.Toggled:
                try
                {
                    StoreWrite(result.Text);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Message = $"保存できませんでした：{ex.Message}";
                    ContentChanged?.Invoke(true);
                    return false;
                }
                _generation++;
                Content = new DayContent.Loaded(Norm(result.Text));
                Message = null;
                AfterWritten();
                ContentUpdatedInPlace?.Invoke();
                return true;

            case TaskToggleStatus.AlreadyInState:
                // 外で先に同じ状態になっていた：書かない。ページの見た目は合っている
                var text = Norm(loaded.Text);
                if (Content is not DayContent.Loaded current || current.Text != text)
                {
                    _generation++;
                    Content = new DayContent.Loaded(text);
                }
                Message = null;
                ContentUpdatedInPlace?.Invoke();
                return true;

            default:
                // その行はチェック項目でなくなっている（外で変わった）：書かず、ファイルの内容で描き直す
                _generation++;
                Content = new DayContent.Loaded(Norm(loaded.Text));
                Message = "チェックボックスを更新できませんでした。ファイルが外で変更されています";
                ContentChanged?.Invoke(true);
                return false;
        }
    }

    // ── ビューに戻る・閉じる ──

    /// <summary>
    /// 編集から出る（Esc・Mod+E・ボタン・別の日・閉じる）。変更が無ければそのまま。未保存なら「保存しますか？」。
    /// ビューモードになった（または元からビュー）なら true。キャンセル・保存の失敗・確認中なら false（編集に留まる）。
    /// </summary>
    public async Task<bool> LeaveEditAsync()
    {
        if (IsConfirming) return false;
        if (Mode == EditorMode.View) return true;
        if (!IsDirty)
        {
            ToView();
            return true;
        }

        var gen = _generation;
        IsConfirming = true;
        SaveChoice choice;
        try
        {
            choice = await _ui.AskSaveAsync(AskSaveText);
        }
        finally
        {
            IsConfirming = false;
        }
        if (gen != _generation || Mode != EditorMode.Edit) return false;   // 確認の間に状態が変わっていたら何もしない

        switch (choice)
        {
            case SaveChoice.Discard:
                Draft = Base;
                ToView();
                return true;
            case SaveChoice.Save:
                var r = await SaveCoreAsync();
                if (r is SaveResult.Saved or SaveResult.Deleted or SaveResult.NoChange)
                {
                    ToView();
                    return true;
                }
                return false;   // 失敗・途中のキャンセル：編集に留まる
            default:
                return false;
        }
    }

    private void ToView()
    {
        _generation++;
        Mode = EditorMode.View;
    }

    /// <summary>ウィンドウを閉じてよいか（「ビューに戻る」と同じ確認。保存・削除に失敗したら閉じない）。</summary>
    public Task<bool> CanCloseAsync() => LeaveEditAsync();
}
