namespace Todokun.Presentation;

/// <summary>編集のセッションを持つプレビューのホスト。文書のペイン（<c>DocumentPaneView</c>）の DataContext。</summary>
public interface IDocumentPane : IPreviewHost
{
    DocumentEditorSession Editor { get; }

    /// <summary>見出し（バーの題名）のクリックで名前を変えられるか。メモのときだけ true。</summary>
    bool CanRenameHeader { get; }

    /// <summary>見出しのクリック：名前の変更を始める（<see cref="CanRenameHeader"/> が true のときだけ効く）。</summary>
    void BeginRenameHeader();
}
