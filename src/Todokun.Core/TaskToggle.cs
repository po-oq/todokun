using System.Text.RegularExpressions;

namespace Todokun.Core;

public enum TaskToggleStatus
{
    /// <summary>1 文字を書き換えた。</summary>
    Toggled,
    /// <summary>すでに求められた状態だった（何も変えない）。</summary>
    AlreadyInState,
    /// <summary>その行はチェック項目ではない（ファイルが外で変わった）。</summary>
    NotATask,
    /// <summary>行番号が範囲外（ファイルが外で変わった）。</summary>
    LineOutOfRange,
}

public readonly record struct TaskToggleResult(TaskToggleStatus Status, string Text);

/// <summary>
/// ビューのチェックボックスのクリックを、md の該当行の <c>[ ]</c> ⇄ <c>[x]</c> に直す（純粋関数）。
/// 行番号は 0 始まり（Markdig の <c>ListItemBlock.Line</c>）。変えるのは印の 1 文字だけで、改行コードは触らない。
/// </summary>
public static partial class TaskToggle
{
    // 箇条書き（- * +）・番号付き（1. 1)）、引用（>）の中、字下げあり。] の後ろは空白か行末（Markdig と同じ）。
    [GeneratedRegex(@"^(?:[ \t]*>)*[ \t]*(?:[-*+]|\d{1,9}[.)])[ \t]+\[(?<mark>[ xX])\](?=[ \t]|$)")]
    private static partial Regex TaskLine();

    public static TaskToggleResult Toggle(string text, int line, bool newChecked)
    {
        if (line < 0) return new(TaskToggleStatus.LineOutOfRange, text);

        // 行頭の位置を探す（\n で区切る。\r は行の中身の末尾に残るだけ）
        var start = 0;
        for (var i = 0; i < line; i++)
        {
            var nl = text.IndexOf('\n', start);
            if (nl < 0) return new(TaskToggleStatus.LineOutOfRange, text);
            start = nl + 1;
        }

        var end = text.IndexOf('\n', start);
        if (end < 0) end = text.Length;
        var content = text.AsSpan(start, end - start);
        if (content.Length > 0 && content[^1] == '\r') content = content[..^1];

        var m = TaskLine().Match(content.ToString());
        if (!m.Success) return new(TaskToggleStatus.NotATask, text);

        var markIndex = start + m.Groups["mark"].Index;
        var isChecked = text[markIndex] is 'x' or 'X';
        if (isChecked == newChecked) return new(TaskToggleStatus.AlreadyInState, text);

        var replaced = string.Concat(text.AsSpan(0, markIndex), newChecked ? "x" : " ", text.AsSpan(markIndex + 1));
        return new(TaskToggleStatus.Toggled, replaced);
    }
}
