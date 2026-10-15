using System.Text;
using Todokun.Core;

namespace Todokun.Tests;

public sealed class MemoIndexTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MemoLibrary _lib;

    public MemoIndexTests() => _lib = new MemoLibrary(_t.Path);

    public void Dispose() => _t.Dispose();

    private void Put(string title, string text, int minutesAgo = 0)
    {
        _lib.Write(title, text);
        File.SetLastWriteTimeUtc(_lib.PathOf(title), DateTime.UtcNow.AddMinutes(-minutesAgo));
    }

    private void PutBytes(string title, byte[] bytes)
    {
        Directory.CreateDirectory(_lib.Folder);
        File.WriteAllBytes(_lib.PathOf(title), bytes);
    }

    [Fact]
    public void Empty_when_the_folder_is_missing()
    {
        var index = new MemoIndex(_lib);
        Assert.Equal(0, index.Count);
        Assert.Empty(index.Items);
    }

    [Fact]
    public void Scan_lists_titles_newest_first()
    {
        Put("old", "o", minutesAgo: 30);
        Put("new", "n", minutesAgo: 1);
        Put("mid", "m", minutesAgo: 10);
        Assert.Equal(["new", "mid", "old"], new MemoIndex(_lib).Items.Select(e => e.Title).ToArray());
    }

    [Fact]
    public void Same_time_is_ordered_by_title_ignoring_case()
    {
        var at = DateTime.UtcNow.AddMinutes(-5);
        foreach (var t in new[] { "b", "A", "c" })
        {
            _lib.Write(t, "x");
            File.SetLastWriteTimeUtc(_lib.PathOf(t), at);
        }
        Assert.Equal(["A", "b", "c"], new MemoIndex(_lib).Items.Select(e => e.Title).ToArray());
    }

    [Fact]
    public void Scan_ignores_non_md_tmp_dot_and_subfolders()
    {
        Put("a", "x");
        File.WriteAllText(Path.Combine(_lib.Folder, "b.txt"), "x");
        File.WriteAllText(Path.Combine(_lib.Folder, "c.md.0123456789abcdef.tmp"), "x");
        File.WriteAllText(Path.Combine(_lib.Folder, ".d.md"), "x");
        Directory.CreateDirectory(Path.Combine(_lib.Folder, "sub"));
        File.WriteAllText(Path.Combine(_lib.Folder, "sub", "e.md"), "x");
        Assert.Equal(["a"], new MemoIndex(_lib).Items.Select(e => e.Title).ToArray());
    }

    [Theory]
    [InlineData("# 見出し\n本文", "見出し")]
    [InlineData("\n\n  最初の行  \n2 行目", "最初の行")]
    [InlineData("- [ ] やること", "やること")]
    [InlineData("- [x] 済み", "済み")]
    [InlineData("- 箇条書き", "箇条書き")]
    [InlineData("###", "")]
    [InlineData("", "")]
    [InlineData("   \n \n", "")]
    public void Snippet_is_the_first_nonblank_line_without_markers(string text, string expected)
        => Assert.Equal(expected, MemoIndex.MakeSnippet(text));

    [Fact]
    public void Snippet_is_cut_to_a_short_length()
    {
        var snippet = MemoIndex.MakeSnippet(new string('あ', 500));
        Assert.True(snippet.Length is > 0 and <= 80);
    }

    [Fact]
    public void Scan_reads_the_snippet_from_the_file()
    {
        Put("a", "# タイトル行\n本文");
        Assert.Equal("タイトル行", new MemoIndex(_lib).Find("a")!.Snippet);
    }

    [Fact]
    public void Snippet_of_an_empty_memo_is_empty()
    {
        _lib.Create("空");
        Assert.Equal("", new MemoIndex(_lib).Find("空")!.Snippet);
    }

    [Fact]
    public void Snippet_skips_a_bom()
    {
        PutBytes("a", [0xEF, 0xBB, 0xBF, .. "こんにちは"u8.ToArray()]);
        Assert.Equal("こんにちは", new MemoIndex(_lib).Find("a")!.Snippet);
    }

    [Fact]
    public void Invalid_utf8_snippet_is_unreadable_but_the_memo_is_listed()
    {
        PutBytes("sjis", [0x82, 0xA0, 0x82, 0xA2]);
        var e = new MemoIndex(_lib).Find("sjis")!;
        Assert.Equal("（読めません）", e.Snippet);
    }

    [Fact]
    public void A_multibyte_char_cut_at_the_4kb_boundary_is_not_reported_as_unreadable()
    {
        // 4095 バイトの ASCII の後ろに「あ」（3 バイト）→ 4096 バイトの境目で文字の途中が切れる。
        var text = new string('a', 4095) + "あ" + "\n次の行";
        PutBytes("long", Encoding.UTF8.GetBytes(text));
        var snippet = new MemoIndex(_lib).Find("long")!.Snippet;
        Assert.NotEqual("（読めません）", snippet);
        Assert.StartsWith("aaaa", snippet);
    }

    [Fact]
    public void Add_reads_the_new_file_and_notifies()
    {
        var index = new MemoIndex(_lib);
        var changed = 0;
        index.Changed += () => changed++;

        Put("fresh", "# 新しい");
        index.Add("fresh");

        Assert.Equal(1, changed);
        Assert.Equal("新しい", index.Find("fresh")!.Snippet);
        Assert.True(index.Contains("FRESH"));
    }

    [Fact]
    public void Remove_drops_the_entry_and_notifies_only_when_it_existed()
    {
        Put("a", "x");
        var index = new MemoIndex(_lib);
        var changed = 0;
        index.Changed += () => changed++;

        index.Remove("a");
        index.Remove("a");

        Assert.Equal(0, index.Count);
        Assert.Equal(1, changed);
    }

    [Fact]
    public void Rename_keeps_the_time_and_the_order()
    {
        Put("a", "x", minutesAgo: 1);
        Put("b", "y", minutesAgo: 10);
        var index = new MemoIndex(_lib);
        var before = index.Find("b")!.ModifiedUtc;

        _lib.Rename("b", "bb");
        index.Rename("b", "bb");

        Assert.Equal(["a", "bb"], index.Items.Select(e => e.Title).ToArray());
        Assert.Equal(before, index.Find("bb")!.ModifiedUtc);
        Assert.Null(index.Find("b"));
    }

    [Fact]
    public void Touch_moves_the_memo_to_the_top_and_updates_the_snippet()
    {
        Put("a", "# 古い", minutesAgo: 30);
        Put("b", "x", minutesAgo: 10);
        var index = new MemoIndex(_lib);
        Assert.Equal("b", index.Items[0].Title);

        _lib.Write("a", "# 新しい");
        index.Touch("a", "# 新しい");

        Assert.Equal("a", index.Items[0].Title);
        Assert.Equal("新しい", index.Items[0].Snippet);
    }

    [Fact]
    public void Touch_adds_a_memo_that_is_not_listed()
    {
        var index = new MemoIndex(_lib);
        _lib.Write("late", "本文");
        index.Touch("late", "本文");
        Assert.True(index.Contains("late"));
        Assert.Equal("本文", index.Find("late")!.Snippet);
    }

    [Fact]
    public void Search_matches_title_and_body_ignoring_case()
    {
        Put("Shopping", "milk");
        Put("会議", "Agenda: BUDGET");
        Put("other", "nothing");
        var index = new MemoIndex(_lib);
        string? Read(string t) => (_lib.Read(t) as DayContent.Loaded)?.Text;

        Assert.Equal(["Shopping"], index.Search("shop", Read).Select(e => e.Title).ToArray());
        Assert.Equal(["会議"], index.Search("budget", Read).Select(e => e.Title).ToArray());
        Assert.Equal(3, index.Search("", Read).Count);
        Assert.Empty(index.Search("zzz", Read));
    }

    [Fact]
    public void Search_treats_an_unreadable_body_as_no_match()
    {
        Put("a", "x");
        PutBytes("sjis", [0x82, 0xA0]);
        var index = new MemoIndex(_lib);
        string? Read(string t) => (_lib.Read(t) as DayContent.Loaded)?.Text;
        Assert.Empty(index.Search("あ", Read));
    }

    [Fact]
    public void Search_does_not_read_bodies_of_titles_that_already_match()
    {
        Put("hit", "x");
        var index = new MemoIndex(_lib);
        var reads = 0;
        var result = index.Search("hit", _ => { reads++; return null; });
        Assert.Single(result);
        Assert.Equal(0, reads);
    }
}
