using System.Text;
using Todokun.Core;

namespace Todokun.Tests;

public sealed class MemoTitleTests
{
    [Theory]
    [InlineData("買い物", "買い物")]
    [InlineData("A\\B", "A＼B")]
    [InlineData("A/B", "A／B")]
    [InlineData("A:B", "A：B")]
    [InlineData("A*B", "A＊B")]
    [InlineData("A?B", "A？B")]
    [InlineData("A\"B", "A”B")]
    [InlineData("A<B>C", "A＜B＞C")]
    [InlineData("A|B", "A｜B")]
    [InlineData("A/B:テスト?", "A／B：テスト？")]
    public void Forbidden_characters_become_fullwidth(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Theory]
    [InlineData("a\nb", "a b")]
    [InlineData("a\tb", "a b")]
    [InlineData("a\0b", "a b")]
    [InlineData("a\r\nb", "a  b")]
    public void Control_characters_become_a_space(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Theory]
    [InlineData("  a  ", "a")]
    [InlineData("　a　", "a")]
    [InlineData("\na\n", "a")]
    [InlineData(" a b ", "a b")]
    public void Surrounding_whitespace_is_trimmed(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Theory]
    [InlineData("a.md", "a")]
    [InlineData("a.MD", "a")]
    [InlineData("a.Md", "a")]
    [InlineData("a.md ", "a")]
    [InlineData(" a.md", "a")]
    [InlineData("a.md.md", "a")]
    [InlineData("a.md.", "a")]
    [InlineData("a. md", "a. md")]
    [InlineData("a.markdown", "a.markdown")]
    public void Trailing_md_is_removed_until_stable(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Theory]
    [InlineData(".env", "env")]
    [InlineData("..a", "a")]
    [InlineData(". a", "a")]
    [InlineData("a.", "a")]
    [InlineData("a...", "a")]
    [InlineData("a. .", "a")]
    [InlineData("a.b", "a.b")]
    public void Leading_and_trailing_dots_are_removed(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("　 \t\n")]
    [InlineData(".")]
    [InlineData("...")]
    [InlineData(".md")]
    [InlineData(" . .md ")]
    public void Nothing_left_returns_empty(string raw)
        => Assert.Equal("", MemoTitle.Normalize(raw));

    [Fact]
    public void Nfd_input_becomes_nfc()
    {
        var nfd = "が".Normalize(NormalizationForm.FormD);
        Assert.NotEqual("が", nfd);
        Assert.Equal("が", MemoTitle.Normalize(nfd));
    }

    [Fact]
    public void Exactly_100_chars_is_kept_and_101_is_cut()
    {
        Assert.Equal(new string('a', 100), MemoTitle.Normalize(new string('a', 100)));
        Assert.Equal(new string('a', 100), MemoTitle.Normalize(new string('a', 101)));
    }

    [Fact]
    public void Cut_never_splits_a_surrogate_pair()
    {
        var raw = new string('a', 99) + "😀" + "b";
        var result = MemoTitle.Normalize(raw);
        Assert.Equal(new string('a', 99), result);
        Assert.True(result.Length <= 100);
        Assert.All(result, c => Assert.False(char.IsSurrogate(c)));

        var fits = new string('a', 98) + "😀";
        Assert.Equal(fits, MemoTitle.Normalize(fits + "zzz"));
    }

    [Fact]
    public void Cut_is_trimmed_again_so_it_never_ends_in_md_or_dot_or_space()
    {
        // 切った結果の末尾が ".md" になる 103 文字
        var raw = new string('a', 97) + ".md" + "xyz";
        var result = MemoTitle.Normalize(raw);
        Assert.Equal(new string('a', 97), result);

        var dot = new string('a', 99) + ".b";
        Assert.Equal(new string('a', 99), MemoTitle.Normalize(dot));

        var space = new string('a', 99) + " b";
        Assert.Equal(new string('a', 99), MemoTitle.Normalize(space));
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("con", "con_")]
    [InlineData("Nul", "Nul_")]
    [InlineData("PRN", "PRN_")]
    [InlineData("AUX", "AUX_")]
    [InlineData("COM1", "COM1_")]
    [InlineData("com0", "com0_")]
    [InlineData("LPT9", "LPT9_")]
    [InlineData("CON.txt", "CON_.txt")]
    [InlineData("nul .md", "nul_")]
    [InlineData("nul .txt", "nul_ .txt")]
    [InlineData("COM¹", "COM¹_")]
    [InlineData("LPT³", "LPT³_")]
    [InlineData("CONIN$", "CONIN$_")]
    [InlineData("conout$", "conout$_")]
    [InlineData("CONSOLE", "CONSOLE")]
    [InlineData("COM10", "COM10")]
    [InlineData("コン", "コン")]
    public void Reserved_names_get_an_underscore_after_the_base(string raw, string expected)
        => Assert.Equal(expected, MemoTitle.Normalize(raw));

    [Fact]
    public void Reserved_name_fix_keeps_the_length_within_100()
    {
        var raw = "CON." + new string('a', 120);
        var result = MemoTitle.Normalize(raw);
        Assert.Equal("CON_." + new string('a', 95), result);
        Assert.Equal(100, result.Length);
        Assert.Equal(result, MemoTitle.Normalize(result));
    }

    [Fact]
    public void Reserved_name_check_survives_the_other_steps()
    {
        Assert.Equal("con_", MemoTitle.Normalize(" con.md "));
        Assert.Equal("NUL_", MemoTitle.Normalize(".NUL."));
    }

    [Fact]
    public void Public_tables_cover_the_documented_names()
    {
        foreach (var n in new[] { "CON", "PRN", "AUX", "NUL", "COM0", "COM9", "LPT0", "LPT9", "COM¹", "COM²", "COM³", "LPT¹", "LPT²", "LPT³", "CONIN$", "CONOUT$" })
            Assert.Contains(n, MemoTitle.ReservedNames);
        Assert.Equal(9, MemoTitle.Replacements.Count);
        Assert.Equal('＼', MemoTitle.Replacements['\\']);
    }

    [Theory]
    [InlineData("a.md ")]
    [InlineData("a.md.md")]
    [InlineData("a.md.")]
    [InlineData(" . md ")]
    [InlineData("con.md.")]
    public void Specific_inputs_are_idempotent(string raw)
    {
        var once = MemoTitle.Normalize(raw);
        Assert.Equal(once, MemoTitle.Normalize(once));
    }

    [Fact]
    public void Generated_inputs_are_idempotent_and_safe()
    {
        string[] pieces = ["a", "b", ".", ".md", ".MD", " ", "　", "\t", "/", ":", "CON", "nul", "com1", "COM¹", "が", "が".Normalize(NormalizationForm.FormD), "😀", "あ", "$", "_"];
        var rnd = new Random(20261015);
        for (var i = 0; i < 5000; i++)
        {
            var sb = new StringBuilder();
            var n = rnd.Next(0, 70);
            for (var j = 0; j < n; j++) sb.Append(pieces[rnd.Next(pieces.Length)]);
            var raw = sb.ToString();

            var once = MemoTitle.Normalize(raw);
            Assert.Equal(once, MemoTitle.Normalize(once));
            Assert.True(once.Length <= 100, raw);
            Assert.Equal(once, once.Normalize(NormalizationForm.FormC));
            Assert.False(once.EndsWith('.') || once.StartsWith('.'), raw);
            Assert.False(once.EndsWith(".md", StringComparison.OrdinalIgnoreCase), raw);
            Assert.Equal(once.Trim(), once);
            Assert.False(once.Length > 0 && char.IsHighSurrogate(once[^1]), raw);
            Assert.DoesNotContain(once, c => "\\/:*?\"<>|".Contains(c) || c < ' ');
        }
    }
}
