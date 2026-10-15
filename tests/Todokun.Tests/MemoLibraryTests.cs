using System.Text;
using Todokun.Core;

namespace Todokun.Tests;

public sealed class MemoLibraryTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MemoLibrary _lib;

    public MemoLibraryTests() => _lib = new MemoLibrary(_t.Path);

    public void Dispose() => _t.Dispose();

    private string Memos => _t.Combine("memos");

    private void Put(string name, string text = "x")
    {
        Directory.CreateDirectory(Memos);
        File.WriteAllText(Path.Combine(Memos, name), text);
    }

    [Fact]
    public void Path_is_memos_folder_under_the_root_with_md()
    {
        Assert.Equal(Path.Combine(_t.Path, "memos", "買い物.md"), _lib.PathOf("買い物"));
    }

    [Fact]
    public void Enumerate_ignores_non_md_tmp_dot_and_subfolders()
    {
        Put("a.md");
        Put("B.MD");
        Put("c.txt");
        Put("d.md.0123456789abcdef.tmp");
        Put(".hidden.md");
        Put("~RFabc.TMP");
        Directory.CreateDirectory(Path.Combine(Memos, "sub.md"));
        Put(Path.Combine("sub.md", "inner.md"));

        Assert.Equal(["B", "a"], _lib.EnumerateTitles().Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Enumerate_is_empty_when_the_folder_is_missing()
    {
        Assert.Empty(_lib.EnumerateTitles());
    }

    [Fact]
    public void Write_then_read_roundtrips_with_lf_and_no_bom()
    {
        _lib.Write("a", "x\r\ny");
        var bytes = File.ReadAllBytes(_lib.PathOf("a"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("x\ny", Assert.IsType<DayContent.Loaded>(_lib.Read("a")).Text);
        Assert.True(_lib.Exists("a"));
    }

    [Fact]
    public void Read_of_a_missing_memo_is_missing()
    {
        Assert.IsType<DayContent.Missing>(_lib.Read("nope"));
    }

    [Fact]
    public void Write_can_write_an_empty_text()
    {
        _lib.Write("a", "");
        Assert.True(File.Exists(_lib.PathOf("a")));
        Assert.Equal("", Assert.IsType<DayContent.Loaded>(_lib.Read("a")).Text);
    }

    [Fact]
    public void Create_makes_an_empty_file_and_returns_the_title()
    {
        var title = _lib.Create("無題のメモ");
        Assert.Equal("無題のメモ", title);
        Assert.Equal(0, new FileInfo(_lib.PathOf(title)).Length);
    }

    [Fact]
    public void Create_avoids_duplicates()
    {
        Assert.Equal("無題のメモ", _lib.Create("無題のメモ"));
        Assert.Equal("無題のメモ (2)", _lib.Create("無題のメモ"));
        Assert.Equal("無題のメモ (3)", _lib.Create("無題のメモ"));
    }

    [Fact]
    public void Create_normalizes_and_falls_back_to_the_default_title()
    {
        Assert.Equal("A／B", _lib.Create("A/B"));
        Assert.Equal("無題のメモ", _lib.Create("   "));
    }

    [Fact]
    public void Unique_title_ignores_case()
    {
        Put("Memo.md");
        Assert.Equal("memo (2)", _lib.UniqueTitle("memo"));
    }

    [Fact]
    public void Unique_title_treats_nfc_and_nfd_as_the_same()
    {
        Put("が.md");
        var nfd = "が".Normalize(NormalizationForm.FormD);
        Assert.Equal("が (2)", _lib.UniqueTitle(nfd));
    }

    [Fact]
    public void Unique_title_keeps_100_chars_by_cutting_the_body()
    {
        var name = new string('a', 100);
        Put(name + ".md");
        var next = _lib.UniqueTitle(name);
        Assert.Equal(new string('a', 96) + " (2)", next);
        Assert.Equal(100, next.Length);
    }

    [Fact]
    public void Unique_title_cut_does_not_split_a_surrogate_pair()
    {
        var name = new string('a', 97) + "😀";
        Put(name + ".md");
        var next = _lib.UniqueTitle(name);
        Assert.True(next.Length <= 100);
        Assert.EndsWith(" (2)", next);
        Assert.All(next, c => Assert.False(char.IsSurrogate(c)));
    }

    [Fact]
    public void Unique_title_does_not_collide_with_itself()
    {
        Put("a.md");
        Assert.Equal("a", _lib.UniqueTitle("a", except: "a"));
        Assert.Equal("A", _lib.UniqueTitle("A", except: "a"));
    }

    [Fact]
    public void Rename_moves_the_file_and_keeps_the_content()
    {
        _lib.Write("old", "本文");
        Assert.Equal("new", _lib.Rename("old", "new"));
        Assert.False(_lib.Exists("old"));
        Assert.Equal("本文", Assert.IsType<DayContent.Loaded>(_lib.Read("new")).Text);
    }

    [Fact]
    public void Rename_to_an_existing_name_adds_a_number_and_never_overwrites()
    {
        _lib.Write("a", "A");
        _lib.Write("b", "B");
        Assert.Equal("b (2)", _lib.Rename("a", "b"));
        Assert.Equal("B", Assert.IsType<DayContent.Loaded>(_lib.Read("b")).Text);
        Assert.Equal("A", Assert.IsType<DayContent.Loaded>(_lib.Read("b (2)")).Text);
    }

    [Fact]
    public void Rename_to_the_same_name_does_nothing()
    {
        _lib.Write("a", "A");
        Assert.Equal("a", _lib.Rename("a", "a"));
        Assert.Equal("a", _lib.Rename("a", " a.md "));
        Assert.Equal("A", Assert.IsType<DayContent.Loaded>(_lib.Read("a")).Text);
    }

    [Fact]
    public void Rename_to_blank_keeps_the_old_title()
    {
        _lib.Write("a", "A");
        Assert.Equal("a", _lib.Rename("a", "  "));
        Assert.True(_lib.Exists("a"));
    }

    [Fact]
    public void Rename_that_only_changes_the_case_works()
    {
        _lib.Write("memo", "A");
        Assert.Equal("Memo", _lib.Rename("memo", "Memo"));
        Assert.Equal(["Memo"], _lib.EnumerateTitles().ToArray());
        Assert.Equal("A", Assert.IsType<DayContent.Loaded>(_lib.Read("Memo")).Text);
    }

    [Fact]
    public void Rename_of_a_missing_memo_throws_and_creates_nothing()
    {
        Directory.CreateDirectory(Memos);
        Assert.ThrowsAny<IOException>(() => _lib.Rename("ghost", "new"));
        Assert.Empty(_lib.EnumerateTitles());
    }

    [Fact]
    public void Delete_removes_the_file_and_ignores_a_missing_one()
    {
        _lib.Write("a", "A");
        _lib.Delete("a");
        Assert.False(_lib.Exists("a"));
        _lib.Delete("a");
    }
}
