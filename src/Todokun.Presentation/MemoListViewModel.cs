using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>一覧の 1 件（表示用。状態は <see cref="MemoListViewModel"/> が持ち、変わるたびに作り直す）。</summary>
public sealed class MemoListItem
{
    public MemoListItem(MemoEntry entry, string modifiedText, bool isSelected, bool isRenaming,
        Func<string, Task> open, Func<string, Task> delete, Action<string> beginRename)
    {
        Title = entry.Title;
        Snippet = entry.Snippet;
        ModifiedText = modifiedText;
        IsSelected = isSelected;
        IsRenaming = isRenaming;
        OpenCommand = new AsyncRelayCommand(() => open(Title));
        DeleteCommand = new AsyncRelayCommand(() => delete(Title));
        BeginRenameCommand = new RelayCommand(() => beginRename(Title));
    }

    public string Title { get; }
    public string Snippet { get; }
    public string ModifiedText { get; }
    public bool IsSelected { get; }
    public bool IsRenaming { get; }
    public IAsyncRelayCommand OpenCommand { get; }
    public IAsyncRelayCommand DeleteCommand { get; }
    public IRelayCommand BeginRenameCommand { get; }
}

/// <summary>
/// メモタブの左ペイン：一覧・追加・名前の変更・削除・絞り込み（計画 2-3・7.4）。
/// 選択（開く）は「要求 → 確認 → 確定」：一覧の見た目は、確認が通ってから変える。
/// 一覧が出す確認の間も <see cref="DocumentEditorSession.IsConfirming"/> を立てる。
/// 失敗・置き換えの知らせは <see cref="Notice"/>（開いているメモの読み書きは <c>Editor.Message</c>）。
/// </summary>
public sealed partial class MemoListViewModel : ObservableObject
{
    private readonly MemoLibrary _library;
    private readonly MemoIndex _index;
    private readonly MemoEditorSession _editor;
    private readonly IUiServices _ui;
    private readonly Func<DateTime> _nowUtc;
    private readonly Dictionary<(string Title, DateTime Modified), string?> _bodyCache = [];
    private bool _renamingIsNew;

    [ObservableProperty]
    private IReadOnlyList<MemoListItem> _items = [];

    /// <summary>絞り込み（タイトル・本文。大文字小文字を無視）。</summary>
    [ObservableProperty]
    private string _query = "";

    /// <summary>一覧の知らせ。次の一覧の操作（開く・追加・名前の変更・削除）で消える。</summary>
    [ObservableProperty]
    private string? _notice;

    /// <summary>名前を変えている最中のメモ（一度に 1 つ）。</summary>
    [ObservableProperty]
    private string? _renamingTitle;

    /// <summary>名前の入力欄の内容。</summary>
    [ObservableProperty]
    private string _renameText = "";

    public MemoListViewModel(MemoLibrary library, MemoIndex index, MemoEditorSession editor, IUiServices ui, Func<DateTime>? nowUtc = null)
    {
        _library = library;
        _index = index;
        _editor = editor;
        _ui = ui;
        _nowUtc = nowUtc ?? (() => DateTime.UtcNow);
        index.Changed += Rebuild;
        editor.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MemoEditorSession.Title)) Rebuild();
        };
        Rebuild();
    }

    /// <summary>検索欄にフォーカスを移してほしい（Ctrl/Cmd+F）。画面が受ける。</summary>
    public event Action? FocusSearchRequested;

    public void RequestFocusSearch() => FocusSearchRequested?.Invoke();

    public int Count => _index.Count;
    public bool IsEmpty => _index.Count == 0;
    public bool NoMatch => _index.Count > 0 && Items.Count == 0;
    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    /// <summary>「3 件」。絞り込み中は「2 / 5 件」。</summary>
    public string CountText => Query.Trim().Length == 0 ? $"{Count} 件" : $"{Items.Count} / {Count} 件";

    /// <summary>入力中の名前が正規化で変わるときだけ「→ 『◯◯』で保存されます」。</summary>
    public string? RenamePreview
    {
        get
        {
            if (RenamingTitle is null) return null;
            var normalized = MemoTitle.Normalize(RenameText);
            return normalized.Length == 0 || normalized == RenameText ? null : $"→ 『{normalized}』で保存されます";
        }
    }

    partial void OnQueryChanged(string value) => Rebuild();
    partial void OnRenamingTitleChanged(string? value)
    {
        OnPropertyChanged(nameof(RenamePreview));
        Rebuild();
    }
    partial void OnRenameTextChanged(string value) => OnPropertyChanged(nameof(RenamePreview));
    partial void OnNoticeChanged(string? value) => OnPropertyChanged(nameof(HasNotice));

    // ── 開く ──

    /// <summary>そのメモを開く。開いているメモなら何もしない。未保存なら「保存しますか？」。</summary>
    public async Task OpenAsync(string title)
    {
        if (_editor.IsConfirming) return;
        Notice = null;
        CommitRename();
        if (Same(title, _editor.Title)) return;

        if (!_library.Exists(title))
        {
            Vanished(title);
            return;
        }
        if (await _editor.TryOpenAsync(title) && _editor.Content is DayContent.Missing) Vanished(title);
    }

    // ── 追加 ──

    /// <summary>空のメモを作ってすぐ開き、その行を名前の入力状態にする（確定・Esc で編集モードに入る）。</summary>
    public async Task AddAsync()
    {
        if (_editor.IsConfirming) return;
        Notice = null;
        CommitRename();
        if (!await _editor.LeaveEditAsync()) return;

        string title;
        try
        {
            title = _library.Create(MemoLibrary.DefaultTitle);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = $"メモを作れませんでした：{ex.Message}";
            return;
        }

        Query = "";   // 作ったメモが絞り込みで隠れないように
        _index.Add(title);
        _editor.Open(title);
        _renamingIsNew = true;
        RenameText = title;
        RenamingTitle = title;
    }

    // ── 名前の変更 ──

    public void BeginRename(string title)
    {
        if (_editor.IsConfirming) return;
        Notice = null;
        CommitRename();
        if (!_library.Exists(title))
        {
            Vanished(title);
            return;
        }
        _renamingIsNew = false;
        RenameText = title;
        RenamingTitle = title;
    }

    /// <summary>
    /// 名前を確定する（冪等：Enter で確定 → 入力欄が消える → フォーカスが外れる、で 2 回来ても 2 回目は何もしない）。
    /// 正規化して空・同じ名前なら何もしない。置き換え・番号が付いたときは <see cref="Notice"/> に出す。
    /// </summary>
    public void CommitRename()
    {
        if (RenamingTitle is not { } old) return;
        var raw = RenameText;
        var isNew = _renamingIsNew;
        _renamingIsNew = false;
        RenamingTitle = null;   // 先に下ろす（再入しても 2 回目は何もしない）

        var current = old;
        if (MemoTitle.Normalize(raw).Length > 0)
        {
            if (!_library.Exists(old))
            {
                Vanished(old);
                return;
            }
            try
            {
                var final = _library.Rename(old, raw);
                if (!string.Equals(final, old, StringComparison.Ordinal))
                {
                    _index.Rename(old, final);
                    if (Same(_editor.Title, old)) _editor.RenameTo(final);
                    current = final;
                    var typed = raw.Trim();
                    if (typed != final) Notice = $"『{typed}』を『{final}』にしました";
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Notice = $"『{old}』の名前を変えられませんでした：{ex.Message}";
            }
        }

        if (isNew && Same(_editor.Title, current)) _editor.BeginEdit();
    }

    /// <summary>名前の変更をやめる（Esc）。追加した直後なら、無題のまま編集モードに入る。</summary>
    public void CancelRename()
    {
        var title = RenamingTitle;
        var isNew = _renamingIsNew;
        _renamingIsNew = false;
        RenamingTitle = null;
        if (isNew && title is not null && Same(_editor.Title, title)) _editor.BeginEdit();
    }

    // ── 削除 ──

    /// <summary>確認 → 消す。開いているメモなら、見えている並びの隣を開く（無ければ「メモなし」）。</summary>
    public async Task DeleteAsync(string title)
    {
        if (_editor.IsConfirming) return;
        Notice = null;
        if (RenamingTitle is { } renaming && Same(renaming, title))
        {
            // 消す対象が名前の変更中なら、確定せず取り消す
            _renamingIsNew = false;
            RenamingTitle = null;
        }
        else
        {
            CommitRename();
        }

        if (!_library.Exists(title))
        {
            _index.Remove(title);   // 外で消えていた：一覧から外すだけ
            return;
        }

        var isOpen = Same(title, _editor.Title);
        var message = $"『{title}』を削除します。元に戻せません。";
        if (isOpen && _editor.IsDirty) message += "保存していない変更も失われます。";

        if (!await _editor.RunConfirmingAsync(() => _ui.ConfirmDeleteAsync(message))) return;

        if (!_library.Exists(title))
        {
            _index.Remove(title);   // 確認の間に外で消えた
            return;
        }

        var neighbor = NeighborOf(title);
        try
        {
            _library.Delete(title);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = $"『{title}』を削除できませんでした：{ex.Message}";
            return;
        }

        _index.Remove(title);
        if (!isOpen) return;
        if (neighbor is not null) _editor.Open(neighbor);
        else _editor.Clear();
    }

    // ── 内部 ──

    private static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(
            a.Normalize(System.Text.NormalizationForm.FormC), b.Normalize(System.Text.NormalizationForm.FormC),
            StringComparison.OrdinalIgnoreCase);

    private void Vanished(string title)
    {
        _index.Remove(title);
        Notice = $"『{title}』が見つかりません";
    }

    /// <summary>いま見えている並びで次（無ければ前）。</summary>
    private string? NeighborOf(string title)
    {
        var visible = Items.Select(i => i.Title).ToList();
        var at = visible.FindIndex(t => Same(t, title));
        if (at < 0) return null;
        if (at + 1 < visible.Count) return visible[at + 1];
        return at > 0 ? visible[at - 1] : null;
    }

    private string? ReadBody(string title)
    {
        var modified = _index.Find(title)?.ModifiedUtc ?? DateTime.MinValue;
        if (_bodyCache.TryGetValue((title, modified), out var cached)) return cached;
        var body = _library.Read(title) is DayContent.Loaded l ? l.Text : null;
        _bodyCache[(title, modified)] = body;
        return body;
    }

    private void Rebuild()
    {
        var now = _nowUtc();
        Items =
        [
            .. _index.Search(Query, ReadBody).Select(e => new MemoListItem(
                e, FormatModified(e.ModifiedUtc, now),
                isSelected: Same(e.Title, _editor.Title),
                isRenaming: RenamingTitle is not null && Same(e.Title, RenamingTitle),
                open: OpenAsync, delete: DeleteAsync, beginRename: BeginRename))
        ];
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(NoMatch));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>「たった今」「5 分前」「3 時間前」、それより古ければ「10/14」（culture に依らない）。</summary>
    public static string FormatModified(DateTime modifiedUtc, DateTime nowUtc)
    {
        var age = nowUtc - modifiedUtc;
        if (age < TimeSpan.FromMinutes(1)) return "たった今";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} 分前";
        if (age < TimeSpan.FromDays(1)) return $"{(int)age.TotalHours} 時間前";
        return modifiedUtc.ToLocalTime().ToString("M/d", CultureInfo.InvariantCulture);
    }
}
