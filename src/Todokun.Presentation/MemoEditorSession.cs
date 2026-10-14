using Todokun.Core;

namespace Todokun.Presentation;

/// <summary>メモ（アプリ全体で 1 つ）の編集の状態。共通の状態機械は <see cref="DocumentEditorSession"/>。</summary>
public sealed class MemoEditorSession : DocumentEditorSession
{
    private readonly MemoStore _store;

    public MemoEditorSession(MemoStore store, IUiServices ui, Func<(bool CanWrite, string? Reason)>? writable = null)
        : base(ui, writable)
    {
        _store = store;
    }

    public override string HeaderText => "メモ";

    /// <summary>メモを読んで、ビューモードで出す。呼ぶ前に、編集中の確認は済んでいること。</summary>
    public void Load()
    {
        _generation++;
        ShowContent(_store.Read());
    }

    protected override bool StoreExists() => _store.Exists();
    protected override void StoreWrite(string text) => _store.Write(text);
    protected override void StoreDelete() => _store.Delete();
    protected override string UnreadableMessage(string reason) => $"メモのファイルは読めないため、編集できません：{reason}";
    protected override string AskSaveText => "メモが保存されていません。保存しますか？";
    protected override string ConfirmDeleteText => "メモのファイルを削除します。よろしいですか？";

    public override PreviewSource Preview => Content switch
    {
        DayContent.Loaded l => new PreviewSource.Markdown(l.Text, _store.Root, _store.Path, MemoStore.FileName),
        DayContent.Failed f => new PreviewSource.Message($"メモのファイルを読めません：{f.Reason}"),
        _ => new PreviewSource.Message("メモはまだありません。『編集』で書けます"),
    };
}
