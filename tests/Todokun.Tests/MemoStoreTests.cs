using System.Text;
using Todokun.Core;

namespace Todokun.Tests;

public sealed class MemoStoreTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MemoStore _s;

    public MemoStoreTests() => _s = new MemoStore(_t.Path);

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Path_is_memo_md_directly_under_the_root()
    {
        Assert.Equal(System.IO.Path.Combine(_t.Path, "memo.md"), _s.Path);
    }

    [Fact]
    public void Missing_when_there_is_no_file()
    {
        Assert.False(_s.Exists());
        Assert.IsType<DayContent.Missing>(_s.Read());
    }

    [Fact]
    public void Write_then_read_roundtrips_with_lf_and_no_bom()
    {
        _s.Write("a\r\nb");
        var bytes = File.ReadAllBytes(_s.Path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal("a\nb", Assert.IsType<DayContent.Loaded>(_s.Read()).Text);
        Assert.True(_s.Exists());
    }

    [Fact]
    public void Read_strips_a_bom()
    {
        File.WriteAllBytes(_s.Path, [0xEF, 0xBB, 0xBF, (byte)'x']);
        Assert.Equal("x", Assert.IsType<DayContent.Loaded>(_s.Read()).Text);
    }

    [Fact]
    public void Invalid_utf8_is_failed_and_the_file_is_untouched()
    {
        byte[] sjis = [0x82, 0xA0, 0x82, 0xA2];
        File.WriteAllBytes(_s.Path, sjis);
        Assert.IsType<DayContent.Failed>(_s.Read());
        Assert.Equal(sjis, File.ReadAllBytes(_s.Path));
    }

    [Fact]
    public void Write_creates_the_root_folder_when_missing()
    {
        var s = new MemoStore(_t.Combine("not", "yet"));
        s.Write("x");
        Assert.True(s.Exists());
    }

    [Fact]
    public void Delete_removes_the_file_and_is_a_no_op_when_missing()
    {
        _s.Delete();
        _s.Write("x");
        _s.Delete();
        Assert.False(_s.Exists());
    }

    [Fact]
    public void Utf8_text_roundtrips()
    {
        _s.Write("日本語のメモ\n- [ ] 買い物");
        Assert.Equal("日本語のメモ\n- [ ] 買い物", Assert.IsType<DayContent.Loaded>(_s.Read()).Text);
        Assert.Equal("日本語のメモ\n- [ ] 買い物", Encoding.UTF8.GetString(File.ReadAllBytes(_s.Path)));
    }

    [Fact]
    public void Memo_file_is_not_a_day_file()
    {
        _s.Write("x");
        Assert.False(DayFile.TryParse(_t.Path, _s.Path, out _));
        Assert.Equal(0, new DayIndex(_t.Path).Count);
    }
}
