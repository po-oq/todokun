namespace Todokun.Presentation;

/// <summary>編集のセッションを持つプレビューのホスト。文書のペイン（<c>DocumentPaneView</c>）の DataContext。</summary>
public interface IDocumentPane : IPreviewHost
{
    DocumentEditorSession Editor { get; }
}
