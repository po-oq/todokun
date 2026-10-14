namespace Todokun.Core;

/// <summary>日ファイルを読んだ結果。「読めない」を「ファイルなし」と混ぜない（計画 11.4）。</summary>
public abstract record DayContent
{
    private DayContent() { }

    /// <summary>ファイルが無い日。</summary>
    public sealed record Missing : DayContent;

    /// <summary>読めた。<paramref name="Text"/> は BOM を除き、改行はそのまま（CRLF のことがある）。</summary>
    public sealed record Loaded(string Text) : DayContent;

    /// <summary>読めない（IO エラー・UTF-8 として正しくない）。</summary>
    public sealed record Failed(string Reason) : DayContent;
}
