using Todokun.Core.Editing;

namespace Todokun.Tests;

/// <summary>記法：<c>^</c> がカーソル（選択なし）。<c>«…»</c> が選択（カーソルは » の位置）。</summary>
public class EditingTests
{
    private static EditState Parse(string marked)
    {
        var a = marked.IndexOf('«');
        if (a >= 0)
        {
            var b = marked.IndexOf('»');
            var text = marked.Remove(b, 1).Remove(a, 1);
            return new EditState(text, b - 1, a, b - 1);
        }
        var c = marked.IndexOf('^');
        return EditState.At(marked.Remove(c, 1), c);
    }

    private static string Show(EditState s, EditResult? r)
    {
        if (r is null) return "(null)";
        var t = r.Apply(s.Text);
        if (r.SelStart != r.SelEnd)
        {
            var lo = Math.Min(r.SelStart, r.SelEnd); var hi = Math.Max(r.SelStart, r.SelEnd);
            return t.Insert(hi, "»").Insert(lo, "«");
        }
        return t.Insert(r.Caret, "^");
    }

    private static string Run(Func<EditState, EditResult?> f, string marked)
    {
        var s = Parse(marked);
        return Show(s, f(s));
    }

    // ── Tab ──
    [Theory]
    [InlineData("^- [ ] a", "  ^- [ ] a")]          // 行頭
    [InlineData("- [ ] a^", "  - [ ] a^")]          // 行末
    [InlineData("- [ ]^ a\nb", "  - [ ]^ a\nb")]    // 行の途中
    [InlineData("  - a^", "    - a^")]              // 字下げあり
    [InlineData("a\n^\nb", "a\n  ^\nb")]            // 空行
    [InlineData("a\r\nb^\r\nc", "a\r\n  b^\r\nc")]  // CRLF
    public void Indent_single_line(string input, string expected) =>
        Assert.Equal(expected, Run(ListEditing.Indent, input));

    [Fact]
    public void Indent_multiple_lines_selection() =>
        Assert.Equal("«  a\n  b\n  c»\nd", Run(ListEditing.Indent, "«a\nb\nc»\nd"));

    [Fact]
    public void Indent_selection_ending_at_column0_excludes_last_line() =>
        Assert.Equal("«  a\n  b\n»c", Run(ListEditing.Indent, "«a\nb\n»c"));

    [Fact]
    public void Indent_selection_within_one_line_keeps_selection()
    {
        Assert.Equal("  a«bc»d", Run(ListEditing.Indent, "a«bc»d"));
    }

    [Fact]
    public void Indent_crlf_multiple_lines()
    {
        Assert.Equal("«  a\r\n  b»\r\nc", Run(ListEditing.Indent, "«a\r\nb»\r\nc"));
    }

    // ── Shift+Tab ──
    [Theory]
    [InlineData("    - a^", "  - a^")]       // 4 → 2
    [InlineData("  - a^", "- a^")]           // 2 → 0
    [InlineData(" - a^", "- a^")]            // 1 つだけ
    [InlineData("\t- a^", "- a^")]           // タブ
    [InlineData("- a^", "(null)")]           // 字下げなし
    [InlineData("^  - a", "^- a")]           // カーソルは行頭より前へ動かない
    [InlineData("a\r\n  b^\r\n", "a\r\nb^\r\n")]
    public void Outdent_single_line(string input, string expected) =>
        Assert.Equal(expected, Run(ListEditing.Outdent, input));

    [Fact]
    public void Outdent_caret_inside_removed_indent_goes_to_line_start()
    {
        var s = EditState.At("  - a", 1);   // 字下げの 1 つ目と 2 つ目の間
        Assert.Equal("^- a", Show(s, ListEditing.Outdent(s)));
    }

    [Fact]
    public void Outdent_multiple_lines_mixed() =>
        Assert.Equal("«a\nb\n- c»", Run(ListEditing.Outdent, "«  a\n b\n  - c»"));

    // ── Ctrl/Cmd+Enter ──
    [Theory]
    [InlineData("- [ ] a^", "- [x] a^")]
    [InlineData("- [x] a^", "- [ ] a^")]
    [InlineData("- [X] a^", "- [ ] a^")]
    [InlineData("  - [ ] ^a", "  - [x] ^a")]
    [InlineData("^- [ ] a", "^- [x] a")]            // カーソル位置は動かさない
    [InlineData("- [ ] a\n- [ ] b^", "- [ ] a\n- [x] b^")]
    [InlineData("- [ ] a^\r\nb", "- [x] a^\r\nb")]
    [InlineData("* [ ] a^", "(null)")]              // * は対象外
    [InlineData("+ [ ] a^", "(null)")]
    [InlineData("-[ ] a^", "(null)")]
    [InlineData("- [ ]a^", "(null)")]
    [InlineData("普通の行^", "(null)")]
    public void ToggleCheck_table(string input, string expected) =>
        Assert.Equal(expected, Run(ListEditing.ToggleCheck, input));

    // ── Enter ──
    [Theory]
    [InlineData("- [ ] a^", "- [ ] a\n- [ ] ^")]
    [InlineData("- [x] a^", "- [x] a\n- [ ] ^")]
    [InlineData("  - [ ] a^", "  - [ ] a\n  - [ ] ^")]                // 字下げをコピー
    [InlineData("- [ ] a^b", "- [ ] a\n- [ ] ^b")]                      // 後ろの文字は新しい項目の本文
    [InlineData("- [ ] ^a", "- [ ] \n- [ ] ^a")]                        // マーカーの直後
    [InlineData("- [ ] a^\r\n", "- [ ] a\r\n- [ ] ^\r\n")]              // CRLF
    [InlineData("- [ ] ^", "^")]                                        // 空項目 → 抜ける
    [InlineData("  - [ ] ^", "^")]                                      // 字下げも消す
    [InlineData("x\n  - [ ] ^\ny", "x\n^\ny")]
    [InlineData("- [^ ] a", "(null)")]                                  // マーカーの内側
    [InlineData("^- [ ] a", "(null)")]                                  // 字下げ（行頭）
    [InlineData("  ^- [ ] a", "(null)")]                                // 字下げの中
    [InlineData("普通の行^", "(null)")]
    [InlineData("- 通常の項目^", "(null)")]                             // チェックなしの項目は対象外
    public void ContinueItem_table(string input, string expected) =>
        Assert.Equal(expected, Run(ListEditing.ContinueItem, input));

    [Fact]
    public void ContinueItem_with_selection_is_null()
    {
        Assert.Null(ListEditing.ContinueItem(Parse("- [ ] «ab»c")));
    }

    [Fact]
    public void ContinueItem_between_CR_and_LF_is_null()
    {
        var s = EditState.At("- [ ] a\r\nb", 8);   // \r と \n の間
        Assert.Null(ListEditing.ContinueItem(s));
    }

    [Fact]
    public void ContinueItem_uses_crlf_when_text_has_crlf_elsewhere()
    {
        var s = EditState.At("x\r\n- [ ] a", 10);   // 最終行（改行なし）
        Assert.Equal("x\r\n- [ ] a\r\n- [ ] ", ListEditing.ContinueItem(s)!.Apply(s.Text));
    }

    [Fact]
    public void Result_is_a_single_range_replacement()
    {
        var s = Parse("«a\nb»");
        var r = ListEditing.Indent(s)!;
        Assert.Equal(0, r.Start);
        Assert.Equal(3, r.Length);   // "a\nb"
        Assert.Equal("  a\n  b", r.Replacement);
    }
}
