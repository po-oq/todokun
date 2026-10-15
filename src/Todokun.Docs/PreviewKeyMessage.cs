namespace Todokun.Docs;

/// <summary>ページから <c>invokeCSharpAction</c> で届くキーの知らせ。</summary>
public enum PreviewKey
{
    ArrowLeft,
    ArrowRight,
    /// <summary>編集に入る（冪等。トグルではない。計画 11.6）。</summary>
    BeginEdit,
    /// <summary>メモを追加（メモタブ）。</summary>
    NewMemo,
    /// <summary>メモの絞り込み欄へ（メモタブ）。</summary>
    FindMemo,
}

public static class PreviewKeyMessage
{
    /// <summary>
    /// <c>key:ArrowLeft</c>・<c>key:ArrowRight</c>・<c>key:BeginEdit</c>・<c>key:NewMemo</c>・<c>key:FindMemo</c> だけを受ける。Windows は JSON の文字列の形
    /// （前後が <c>"</c>）で来ることがあるので、それも受ける。他（旧名 <c>key:ToggleEdit</c>・<c>key:Escape</c> を含む）は null。
    /// </summary>
    public static PreviewKey? Parse(string? body) => body switch
    {
        "key:ArrowLeft" or "\"key:ArrowLeft\"" => PreviewKey.ArrowLeft,
        "key:ArrowRight" or "\"key:ArrowRight\"" => PreviewKey.ArrowRight,
        "key:BeginEdit" or "\"key:BeginEdit\"" => PreviewKey.BeginEdit,
        "key:NewMemo" or "\"key:NewMemo\"" => PreviewKey.NewMemo,
        "key:FindMemo" or "\"key:FindMemo\"" => PreviewKey.FindMemo,
        _ => null,
    };
}
