using Todokun.Core;

namespace Todokun.Tests;

public sealed class MemoMigrationTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly MemoLibrary _lib;

    public MemoMigrationTests() => _lib = new MemoLibrary(_t.Path);

    public void Dispose() => _t.Dispose();

    private string OldPath => Path.Combine(_t.Path, "memo.md");

    private void PutOld(byte[] bytes) => File.WriteAllBytes(OldPath, bytes);

    private MemoMigrationResult Run(bool writable = true) => MemoMigration.Run(_lib, writable);

    private string[] Visible() => [.. _lib.EnumerateTitles().Order(StringComparer.Ordinal)];

    [Fact]
    public void Nothing_to_do_without_memo_md()
    {
        Assert.IsType<MemoMigrationResult.NothingToDo>(Run());
        Assert.False(Directory.Exists(_lib.Folder));
    }

    [Fact]
    public void Migrates_bytes_unchanged_including_bom_and_crlf()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. "一行目\r\n二行目\r\n"u8.ToArray()];
        PutOld(bytes);

        var r = Assert.IsType<MemoMigrationResult.Migrated>(Run());

        Assert.Equal("メモ", r.Title);
        Assert.True(r.OldRemoved);
        Assert.Equal(bytes, File.ReadAllBytes(_lib.PathOf("メモ")));
        Assert.False(File.Exists(OldPath));
    }

    [Fact]
    public void Migrates_content_that_is_not_utf8_as_it_is()
    {
        byte[] sjis = [0x82, 0xA0, 0x82, 0xA2];
        PutOld(sjis);
        Assert.IsType<MemoMigrationResult.Migrated>(Run());
        Assert.Equal(sjis, File.ReadAllBytes(_lib.PathOf("メモ")));
    }

    [Fact]
    public void Migrates_an_empty_memo_md()
    {
        PutOld([]);
        Assert.IsType<MemoMigrationResult.Migrated>(Run());
        Assert.True(File.Exists(_lib.PathOf("メモ")));
        Assert.False(File.Exists(OldPath));
    }

    [Fact]
    public void A_different_memo_with_the_same_name_gets_a_number()
    {
        _lib.Write("メモ", "別の内容");
        PutOld("古いメモ"u8.ToArray());

        var r = Assert.IsType<MemoMigrationResult.Migrated>(Run());

        Assert.Equal("メモ (2)", r.Title);
        Assert.Equal("別の内容", Assert.IsType<DayContent.Loaded>(_lib.Read("メモ")).Text);
        Assert.Equal("古いメモ", Assert.IsType<DayContent.Loaded>(_lib.Read("メモ (2)")).Text);
    }

    [Fact]
    public void Same_bytes_already_in_memos_are_not_copied_again_and_memo_md_is_removed()
    {
        PutOld("同じ"u8.ToArray());
        File.WriteAllBytes(Path.Combine(Directory.CreateDirectory(_lib.Folder).FullName, "どこか.md"), "同じ"u8.ToArray());

        var r = Assert.IsType<MemoMigrationResult.AlreadyMigrated>(Run());

        Assert.True(r.OldRemoved);
        Assert.Equal(["どこか"], Visible());
        Assert.False(File.Exists(OldPath));
    }

    [Fact]
    public void Running_twice_does_not_create_a_second_copy()
    {
        PutOld("x"u8.ToArray());
        Run();
        PutOld("x"u8.ToArray()); // 消せなかった場合を模す
        var r = Assert.IsType<MemoMigrationResult.AlreadyMigrated>(Run());
        Assert.True(r.OldRemoved);
        Assert.Equal(["メモ"], Visible());
    }

    [Fact]
    public void Failure_keeps_memo_md_and_the_next_run_migrates()
    {
        PutOld("本文"u8.ToArray());
        // memos がファイルになっていて、フォルダを作れない
        File.WriteAllText(_lib.Folder, "邪魔");

        var failed = Assert.IsType<MemoMigrationResult.Failed>(Run());
        Assert.False(string.IsNullOrWhiteSpace(failed.Reason));
        Assert.True(File.Exists(OldPath));

        File.Delete(_lib.Folder);
        // その間に「＋」で memos/ とメモができても、memo.md を取り残さない
        _lib.Create("無題のメモ");

        var ok = Assert.IsType<MemoMigrationResult.Migrated>(Run());
        Assert.Equal("メモ", ok.Title);
        Assert.Equal(["メモ", "無題のメモ"], Visible());
        Assert.False(File.Exists(OldPath));
    }

    [MacFact]
    public void When_memo_md_cannot_be_removed_the_next_run_does_not_make_a_second_copy()
    {
        if (OperatingSystem.IsWindows()) return;
        PutOld("本文"u8.ToArray());
        Directory.CreateDirectory(_lib.Folder);
        // データルートを書込不可にすると memo.md は消せないが、memos/ の中には書ける
        File.SetUnixFileMode(_t.Path, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var first = Assert.IsType<MemoMigrationResult.Migrated>(Run());
        Assert.False(first.OldRemoved);
        Assert.True(File.Exists(OldPath));

        var second = Assert.IsType<MemoMigrationResult.AlreadyMigrated>(Run());
        Assert.False(second.OldRemoved);
        Assert.Equal(["メモ"], Visible());
    }

    [Fact]
    public void Leftover_temp_files_are_invisible_and_do_not_block_the_next_run()
    {
        PutOld("本文"u8.ToArray());
        Directory.CreateDirectory(_lib.Folder);
        File.WriteAllBytes(Path.Combine(_lib.Folder, ".md.0123456789abcdef.tmp"), "本文"u8.ToArray());
        Assert.Empty(_lib.EnumerateTitles());

        var r = Assert.IsType<MemoMigrationResult.Migrated>(Run());

        Assert.Equal("メモ", r.Title);
        Assert.Equal(["メモ"], Visible());
    }

    [Fact]
    public void Unwritable_data_root_does_not_migrate_and_says_so()
    {
        PutOld("本文"u8.ToArray());

        var r = Assert.IsType<MemoMigrationResult.NotWritable>(Run(writable: false));

        Assert.True(File.Exists(OldPath));
        Assert.False(Directory.Exists(_lib.Folder));
        Assert.NotNull(r);
    }

    [Fact]
    public void Unwritable_data_root_without_memo_md_has_nothing_to_say()
    {
        Assert.IsType<MemoMigrationResult.NothingToDo>(Run(writable: false));
    }
}
