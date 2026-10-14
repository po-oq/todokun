using System.Text.RegularExpressions;

namespace Todokun.Core.Editing;

/// <summary>
/// 編集モードの補助（要件 10 章、計画 11.5）。純粋関数。null は「何もしない」（Enter なら通常の改行）。
/// 改行は <c>\n</c>・<c>\r\n</c> をそのまま保ち、行の内容に <c>\r</c> を含めない。
/// </summary>
public static partial class ListEditing
{
    private const string Unit = "  ";

    [GeneratedRegex(@"^(\s*)- \[([ xX])\] ")]
    private static partial Regex CheckItem();

    // ── 行の見方 ──
    private static int LineStart(string text, int pos) => pos <= 0 ? 0 : text.LastIndexOf('\n', pos - 1) + 1;

    /// <summary>行の内容の終わり（改行・末尾の <c>\r</c> は含まない）。</summary>
    private static int ContentEnd(string text, int lineStart)
    {
        var i = text.IndexOf('\n', lineStart);
        var end = i < 0 ? text.Length : i;
        return end > lineStart && text[end - 1] == '\r' ? end - 1 : end;
    }

    /// <summary>選択がかかっている行の先頭の位置。選択が行頭（桁 0）で終わるとき、その最後の行は含めない。</summary>
    private static List<int> AffectedLineStarts(EditState s)
    {
        var text = s.Text;
        var lo = Math.Clamp(s.HasSelection ? s.SelLow : s.Caret, 0, text.Length);
        var hi = Math.Clamp(s.HasSelection ? s.SelHigh : s.Caret, 0, text.Length);
        var first = LineStart(text, lo);
        var starts = new List<int> { first };
        var lastLine = LineStart(text, hi);
        if (s.HasSelection && hi > lo && hi == lastLine) lastLine = LineStart(text, hi - 1);
        var cur = first;
        while (cur < lastLine)
        {
            var nl = text.IndexOf('\n', cur);
            if (nl < 0) break;
            cur = nl + 1;
            starts.Add(cur);
        }
        return starts;
    }

    /// <summary>各行の先頭に対する「差分」（行頭位置と増減）を適用して、1 か所の置換にまとめる。</summary>
    private static EditResult Build(EditState s, List<int> starts, Func<int, (int removeLen, string insert)> edit)
    {
        var text = s.Text;
        var regionStart = starts[0];
        var regionEnd = ContentEnd(text, starts[^1]);
        var sb = new System.Text.StringBuilder();
        var cursor = regionStart;
        var deltas = new List<(int at, int removed, int added)>();
        foreach (var ls in starts)
        {
            var (removeLen, insert) = edit(ls);
            sb.Append(text, cursor, ls - cursor).Append(insert);
            cursor = ls + removeLen;
            deltas.Add((ls, removeLen, insert.Length));
        }
        sb.Append(text, cursor, regionEnd - cursor);

        // 選択の始まりが行頭にあるとき、挿入した字下げの手前に残す（選択が行全体を覆ったままになる）。
        int Map(int p, bool strict = false)
        {
            var shift = 0;
            foreach (var (at, removed, added) in deltas)
            {
                if (removed > 0)
                {
                    if (p > at) shift -= Math.Min(removed, p - at);
                }
                else if (strict ? at < p : at <= p)
                {
                    shift += added;
                }
            }
            return p + shift;
        }
        return new EditResult(regionStart, regionEnd - regionStart, sb.ToString(), Map(s.Caret), Map(s.SelStart, strict: s.HasSelection && s.SelStart <= s.SelEnd), Map(s.SelEnd));
    }

    // ── Tab ──
    public static EditResult? Indent(EditState s)
    {
        var starts = AffectedLineStarts(s);
        return Build(s, starts, _ => (0, Unit));
    }

    // ── Shift+Tab ──
    public static EditResult? Outdent(EditState s)
    {
        var starts = AffectedLineStarts(s);
        var text = s.Text;
        int Removal(int ls)
        {
            var end = ContentEnd(text, ls);
            var n = end - ls;
            if (n >= 1 && text[ls] == '\t') return 1;
            if (n >= 2 && text[ls] == ' ' && text[ls + 1] == ' ') return 2;
            if (n >= 1 && text[ls] == ' ') return 1;
            return 0;
        }
        if (starts.All(ls => Removal(ls) == 0)) return null;
        return Build(s, starts, ls => (Removal(ls), ""));
    }

    // ── Ctrl/Cmd+Enter ──
    public static EditResult? ToggleCheck(EditState s)
    {
        var text = s.Text;
        var caret = Math.Clamp(s.Caret, 0, text.Length);
        var ls = LineStart(text, caret);
        var content = text[ls..ContentEnd(text, ls)];
        var m = CheckItem().Match(content);
        if (!m.Success) return null;
        var mark = ls + m.Groups[2].Index;
        var repl = m.Groups[2].Value == " " ? "x" : " ";
        return new EditResult(mark, 1, repl, s.Caret, s.SelStart, s.SelEnd);
    }

    // ── Enter ──
    public static EditResult? ContinueItem(EditState s)
    {
        if (s.HasSelection) return null;
        var text = s.Text;
        var caret = s.Caret;
        if (caret < 0 || caret > text.Length) return null;
        var ls = LineStart(text, caret);
        var end = ContentEnd(text, ls);
        if (caret > end) return null;   // CRLF の \r と \n の間
        var content = text[ls..end];
        var m = CheckItem().Match(content);
        if (!m.Success) return null;
        var indent = m.Groups[1].Value;
        var markerLen = m.Length;
        var col = caret - ls;
        if (col < markerLen) return null;   // マーカーの内側・字下げの中

        if (content.Length == markerLen)
        {
            // 本文が空：行を空行にする（字下げも消す）
            return new EditResult(ls, content.Length, "", ls, ls, ls);
        }

        var lineHasCr = end < text.Length && text[end] == '\r';
        var nl = lineHasCr || text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var ins = nl + indent + "- [ ] ";
        var after = caret + ins.Length;
        return new EditResult(caret, 0, ins, after, after, after);
    }
}
