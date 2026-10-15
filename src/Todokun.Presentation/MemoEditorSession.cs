using CommunityToolkit.Mvvm.ComponentModel;
using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>
/// いま開いているメモ（1 つ）の編集の状態。共通の状態機械は <see cref="DocumentEditorSession"/>。
/// 開いているメモが無い状態（「メモなし」）がある。空で保存しても消さずに残す。
/// </summary>
public sealed partial class MemoEditorSession : DocumentEditorSession
{
    public const string NoMemoHeader = "メモなし";

    private readonly MemoLibrary _library;
    private readonly MemoIndex _index;

    /// <summary>開いているメモのタイトル。無ければ null（「メモなし」）。</summary>
    [ObservableProperty]
    private string? _title;

    public MemoEditorSession(MemoLibrary library, MemoIndex index, IUiServices ui, Func<(bool CanWrite, string? Reason)>? writable = null)
        : base(ui, writable)
    {
        _library = library;
        _index = index;
    }

    public override string HeaderText => Title ?? NoMemoHeader;

    protected override bool HasDocument => Title is not null;

    protected override bool DeleteWhenBlank => false;

    partial void OnTitleChanged(string? value)
    {
        OnPropertyChanged(nameof(HeaderText));
        RefreshCanBeginEdit();
    }

    protected override DayContent StoreRead() => Title is null ? new DayContent.Missing() : _library.Read(Title);
    protected override bool StoreExists() => Title is not null && _library.Exists(Title);
    protected override void StoreWrite(string text) => _library.Write(Title!, text);
    protected override void StoreDelete()
    {
        if (Title is not null) _library.Delete(Title);
    }

    protected override void AfterWritten()
    {
        if (Title is not null && Content is DayContent.Loaded l) _index.Touch(Title, l.Text);
    }

    protected override string UnreadableMessage(string reason) => $"メモのファイルは読めないため、編集できません：{reason}";
    protected override string AskSaveText => "メモが保存されていません。保存しますか？";
    protected override string ConfirmDeleteText => "メモのファイルを削除します。よろしいですか？";

    // ── メモを開く ──

    /// <summary>そのメモを読んで、ビューモードで開く。呼ぶ前に、編集中の確認は済んでいること（<see cref="TryOpenAsync"/>）。</summary>
    public void Open(string title)
    {
        _generation++;
        Title = title;
        ShowContent(_library.Read(title));
    }

    /// <summary>
    /// 別のメモを開く（要求 → 確認 → 確定の「確認」と「開く」）。編集中で未保存なら「保存しますか？」を出す。
    /// 開けたら true。確認中・キャンセル・保存の失敗なら false。
    /// </summary>
    public async Task<bool> TryOpenAsync(string title)
    {
        if (IsConfirming) return false;
        if (!await LeaveEditAsync()) return false;
        Open(title);
        return true;
    }

    /// <summary>
    /// 開いているメモの名前が変わった（ファイルは呼び出し側が移し終えている）。保存先と見出しだけを替える。
    /// 未保存の入力・モードは変えず、確認の世代も進めない。ビューのときはプレビューを作り直す。
    /// </summary>
    public void RenameTo(string finalTitle)
    {
        Title = finalTitle;
        if (Mode == EditorMode.View) RaiseContentChanged(true);
    }

    /// <summary>開いているメモが無い状態にする（「メモなし」）。呼ぶ前に、編集中の確認は済んでいること。</summary>
    public void Clear()
    {
        _generation++;
        Title = null;
        ShowContent(new DayContent.Missing());
    }

    // ── プレビューに出すもの ──

    public override PreviewSource Preview => (Title, Content) switch
    {
        (null, _) => new PreviewSource.Message("メモがありません。＋で追加してください"),
        (_, DayContent.Failed f) => new PreviewSource.Message($"メモのファイルを読めません：{f.Reason}"),
        (_, DayContent.Loaded l) when string.IsNullOrWhiteSpace(l.Text) => new PreviewSource.Message("本文がありません。『編集』で書けます"),
        (var t, DayContent.Loaded l) => new PreviewSource.Markdown(l.Text, _library.Folder, _library.PathOf(t), t + ".md"),
        _ => new PreviewSource.Message("メモのファイルが見つかりません"),
    };
}
