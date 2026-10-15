using Todokun.Core;

namespace Todokun.Tests;

/// <summary>計画 2-5：最後に開いていたメモを cache/last-memo.txt に覚える。</summary>
public sealed class MemoLastOpenedTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MemoLastOpened _last;

    public MemoLastOpenedTests() => _last = new MemoLastOpened(_t.Combine("cache", "last-memo.txt"));

    public void Dispose() => _t.Dispose();

    private string FilePath => _t.Combine("cache", "last-memo.txt");

    [Fact]
    public void Nothing_is_remembered_at_first()
    {
        Assert.Null(_last.Read());
    }

    [Fact]
    public void Write_then_read_roundtrips_one_line_with_lf_and_no_bom()
    {
        _last.Write("買い物 (2)");

        Assert.Equal("買い物 (2)", _last.Read());
        var bytes = File.ReadAllBytes(FilePath);
        Assert.Equal("買い物 (2)\n"u8.ToArray(), bytes);
    }

    [Fact]
    public void A_blank_file_reads_as_nothing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, " \n");
        Assert.Null(_last.Read());
    }

    [Fact]
    public void Only_the_first_line_counts()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, "a\r\nb\n");
        Assert.Equal("a", _last.Read());
    }

    [Fact]
    public void Same_title_is_not_written_again()
    {
        _last.Write("a");
        File.Delete(FilePath);   // 書き直されれば、また現れる

        _last.Write("a");

        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void A_different_title_replaces_it()
    {
        _last.Write("a");
        _last.Write("b");
        Assert.Equal("b", _last.Read());
    }

    [Fact]
    public void Clear_removes_the_file_and_tolerates_a_missing_one()
    {
        _last.Write("a");
        _last.Clear();
        Assert.False(File.Exists(FilePath));
        _last.Clear();
        Assert.Null(_last.Read());
    }

    [Fact]
    public void Write_after_clear_writes_the_same_title_again()
    {
        _last.Write("a");
        _last.Clear();
        _last.Write("a");
        Assert.Equal("a", _last.Read());
    }

    [Fact]
    public void An_unwritable_cache_is_tolerated()
    {
        // cache の場所がファイルになっていて、フォルダを作れない
        File.WriteAllText(_t.Combine("cache"), "邪魔");

        _last.Write("a");
        _last.Clear();

        Assert.Null(_last.Read());
    }
}
