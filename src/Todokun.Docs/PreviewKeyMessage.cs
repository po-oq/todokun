namespace Todokun.Docs;

/// <summary>ページから <c>invokeCSharpAction</c> で届くキーの知らせ。</summary>
public enum PreviewKey
{
    ArrowLeft,
    ArrowRight,
    /// <summary>編集に入る（冪等。トグルではない。計画 11.6）。</summary>
    BeginEdit,
}

public static class PreviewKeyMessage
{
    /// <summary>
    /// <c>key:ArrowLeft</c>・<c>key:ArrowRight</c>・<c>key:BeginEdit</c> だけを受ける。Windows は JSON の文字列の形
    /// （前後が <c>"</c>）で来ることがあるので、それも受ける。他（旧名 <c>key:ToggleEdit</c>・<c>key:Escape</c> を含む）は null。
    /// </summary>
    public static PreviewKey? Parse(string? body) => body switch
    {
        "key:ArrowLeft" or "\"key:ArrowLeft\"" => PreviewKey.ArrowLeft,
        "key:ArrowRight" or "\"key:ArrowRight\"" => PreviewKey.ArrowRight,
        "key:BeginEdit" or "\"key:BeginEdit\"" => PreviewKey.BeginEdit,
        _ => null,
    };
}
