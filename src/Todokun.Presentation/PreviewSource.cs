namespace Todokun.Presentation;

/// <summary>プレビューに出すもの（計画 11.7）。</summary>
public abstract record PreviewSource
{
    /// <summary>md の文字。<paramref name="CacheKey"/> は一時 HTML の名前の元（日ファイルのフルパス）。</summary>
    public sealed record Markdown(string Text, string BaseFolder, string CacheKey, string Title) : PreviewSource;

    /// <summary>WebView を出さずに、案内の文字だけ出す。</summary>
    public sealed record Message(string Text) : PreviewSource;
}

/// <summary>ページ（WebView）から届いたキー（Todokun.Docs の <c>PreviewKey</c> を写したもの。Presentation は Docs を参照しない）。</summary>
public enum PageKey
{
    ArrowLeft,
    ArrowRight,
    BeginEdit,
    /// <summary>Ctrl/Cmd+N（メモの追加。WebView にフォーカスがあるとき Windows はページから転送する）。</summary>
    NewMemo,
    /// <summary>Ctrl/Cmd+F（メモの絞り込み欄へ）。</summary>
    FindMemo,
}

/// <summary>プレビューの部品（画面の TodoPreview）を使う側（計画 11.7）。</summary>
public interface IPreviewHost
{
    /// <summary>md の一時 HTML の置き場（<c>cache/preview</c>）。</summary>
    string PreviewDir { get; }

    bool IsDark { get; }

    /// <summary>プレビューを出せない理由（<c>cache/</c> が書けない）。出せるなら null。</summary>
    string? PreviewUnavailableReason { get; }

    /// <summary>WebView2 Runtime が無いときの案内の 2 行目。</summary>
    string RuntimeMissingNote { get; }

    /// <summary>プレビューの表示が変わる。第 2 引数が true なら、同じものでも作り直す。UI スレッドで呼ばれる。</summary>
    event Action<PreviewSource?, bool>? PreviewChanged;

    /// <summary>いま出すべきもの（部品が作られた後に、最初の表示を得るため）。</summary>
    PreviewSource? CurrentPreview { get; }

    void Log(string message);

    /// <summary>プレビュー内のリンク（ローカルのファイル・フォルダ）を開く。</summary>
    void OpenLocalLink(string fullPath);

    /// <summary>ページからキーが届いた。UI スレッドで呼ばれる。</summary>
    void OnPageKey(PageKey key);

    /// <summary>
    /// ページのチェックボックスがクリックされた（<paramref name="line"/> は md の 0 始まりの行、<paramref name="isChecked"/> は押した後の状態）。
    /// UI スレッドで呼ばれる。見た目はページが先に変えている。
    /// </summary>
    void OnCheckboxToggled(int line, bool isChecked);
}
