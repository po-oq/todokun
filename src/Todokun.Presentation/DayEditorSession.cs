using System.Globalization;
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
    /// <summary>書けなかった・消せなかった（理由は <see cref="DayEditorSession.Message"/>）。</summary>
    Failed,
}

/// <summary>
/// 1 日分の編集の状態（計画 11.4）。「読めない」を「ファイルなし」と混ぜない：読めない日は編集に入れない
/// （空から書いて保存すると元のファイルを失うため）。画面・ダイアログは <see cref="IUiServices"/> 越し。
/// </summary>
public sealed partial class DayEditorSession : ObservableObject
{
    private static readonly string[] Weekdays = ["日", "月", "火", "水", "木", "金", "土"];

    private readonly DayStore _store;
    private readonly DayIndex _index;
    private readonly IUiServices _ui;
    private readonly Func<(bool CanWrite, string? Reason)> _writable;
    private int _generation;   // 状態が変わるたびに増やす。確認ダイアログの後で、状態が変わっていないかを見る

    [ObservableProperty]
    private DateOnly _day;

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

    public DayEditorSession(DayStore store, DayIndex index, IUiServices ui, Func<(bool CanWrite, string? Reason)>? writable = null)
    {
        _store = store;
        _index = index;
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

    // ── コマンド（画面の部品用） ──
    public IRelayCommand BeginEditCommand { get; }
    public IAsyncRelayCommand SaveCommand { get; }
    public IAsyncRelayCommand BackToViewCommand { get; }

    /// <summary>表示が変わった（日を開いた・保存した・削除した）。引数 true は同じ日の作り直し。UI スレッドで呼ばれる。</summary>
    public event Action<bool>? ContentChanged;

    // ── 表示用 ──
    public bool IsEditing => Mode == EditorMode.Edit;
    public bool IsViewing => Mode == EditorMode.View;
    public bool IsDirty => Mode == EditorMode.Edit && Norm(Draft) != Norm(Base);
    public bool HasMessage => !string.IsNullOrEmpty(Message);
    public string DayText => $"{Day:yyyy-MM-dd} ({Weekdays[(int)Day.DayOfWeek]})";
    public string ModeText => IsEditing ? "編集中" : "ビュー";

    /// <summary>「✎ 編集」を押せるか。読めない日・データルートが書けないとき・確認中は押せない。</summary>
    public bool CanBeginEdit => IsViewing && !IsConfirming && Content is not DayContent.Failed && _writable().CanWrite;

    public static string Norm(string s) => s.Replace("\r\n", "\n");

    private static bool IsBlank(string s) => string.IsNullOrWhiteSpace(s);

    partial void OnModeChanged(EditorMode value) => RefreshDerived();
    partial void OnDraftChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnBaseChanged(string value) => OnPropertyChanged(nameof(IsDirty));
    partial void OnDayChanged(DateOnly value) => OnPropertyChanged(nameof(DayText));
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

    // ── 日を開く（View） ──

    /// <summary>その日を読んで、ビューモードで開く。呼ぶ前に、編集中の確認は済んでいること（<see cref="TryOpenDayAsync"/>）。</summary>
    public void Open(DateOnly day)
    {
        _generation++;
        Day = day;
        Content = _store.Read(day);
        Mode = EditorMode.View;
        Draft = Base = "";
        Message = null;
        ContentChanged?.Invoke(false);
    }

    /// <summary>
    /// 別の日を開く（要求 → 確認 → 確定の「確認」と「開く」）。編集中で未保存なら「保存しますか？」を出す。
    /// 開けたら true（呼び出し側は選択を確定する）。確認中・キャンセル・保存の失敗なら false。
    /// </summary>
    public async Task<bool> TryOpenDayAsync(DateOnly day)
    {
        if (IsConfirming) return false;
        if (!await LeaveEditAsync()) return false;
        Open(day);
        return true;
    }

    // ── 編集へ ──

    /// <summary>編集に入る（冪等。既に編集中・確認中なら何もしない）。入れなければ理由を <see cref="Message"/> に出す。</summary>
    public bool BeginEdit()
    {
        if (IsConfirming) return false;
        if (Mode == EditorMode.Edit) return true;
        if (Content is DayContent.Failed failed)
        {
            Message = $"この日のファイルは読めないため、編集できません：{failed.Reason}";
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
        if (Mode != EditorMode.Edit) return SaveResult.Cancelled;
        if (!IsDirty) return SaveResult.NoChange;

        if (!IsBlank(Draft))
        {
            try
            {
                _store.Write(Day, Draft);
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
            _index.Add(Day);
            ContentChanged?.Invoke(true);
            return SaveResult.Saved;
        }

        // 空（空白・改行だけ）で保存
        if (!_store.Exists(Day))
        {
            Base = Draft;   // 何も作らない。確認も出さない
            return SaveResult.NoChange;
        }

        var gen = _generation;
        IsConfirming = true;
        bool ok;
        try
        {
            ok = await _ui.ConfirmDeleteAsync();
        }
        finally
        {
            IsConfirming = false;
        }
        if (!ok || gen != _generation) return SaveResult.Cancelled;

        try
        {
            _store.Delete(Day);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Message = $"削除できませんでした：{ex.Message}";
            return SaveResult.Failed;   // Content・DayIndex・Base は変えない
        }
        _generation++;
        Content = new DayContent.Missing();
        Base = Draft;
        Message = null;
        _index.Remove(Day);
        ContentChanged?.Invoke(true);
        return SaveResult.Deleted;
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
            choice = await _ui.AskSaveAsync();
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

    // ── プレビューに出すもの ──

    public PreviewSource Preview => Content switch
    {
        DayContent.Loaded l => new PreviewSource.Markdown(l.Text, Path.GetDirectoryName(_store.PathOf(Day))!, _store.PathOf(Day),
            Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".md"),
        DayContent.Failed f => new PreviewSource.Message($"この日のファイルを読めません：{f.Reason}"),
        _ => new PreviewSource.Message("この日のTODOはまだありません。『編集』で書けます"),
    };
}
