using System.Text;
using Todokun.Core;

namespace Todokun.Tests;

public class LaunchOptionsAndPathsTests
{
    private static string? NoEnv(string _) => null;

    [Fact]
    public void Arg_with_space_separated_value() =>
        Assert.Equal("/x/y", LaunchOptions.Parse(["--data-dir", "/x/y"], NoEnv).DataDir);

    [Fact]
    public void Arg_with_equals() =>
        Assert.Equal("/x/y", LaunchOptions.Parse(["--data-dir=/x/y"], NoEnv).DataDir);

    [Fact]
    public void Arg_wins_over_env() =>
        Assert.Equal("/arg", LaunchOptions.Parse(["--data-dir", "/arg"], k => k == "TODOKUN_DATA_DIR" ? "/env" : null).DataDir);

    [Fact]
    public void Env_used_when_no_arg() =>
        Assert.Equal("/env", LaunchOptions.Parse([], k => k == "TODOKUN_DATA_DIR" ? "/env" : null).DataDir);

    [Theory]
    [InlineData("--data-dir")]          // 値が無い
    [InlineData("--data-dir=")]         // 空
    public void Missing_or_empty_value_is_ignored(string arg) =>
        Assert.Null(LaunchOptions.Parse([arg], NoEnv).DataDir);

    [Fact]
    public void Empty_env_is_ignored() =>
        Assert.Null(LaunchOptions.Parse([], _ => "  ").DataDir);

    [Fact]
    public void Default_is_exe_dir_data_and_cache()
    {
        var p = AppPaths.Resolve(new LaunchOptions(null), "/opt/todokun");
        Assert.Equal(Path.GetFullPath("/opt/todokun/data"), p.DataRoot);
        Assert.Equal(Path.GetFullPath("/opt/todokun/cache"), p.CacheRoot);
        Assert.False(p.IsCustomDataRoot);
    }

    [Fact]
    public void Custom_data_dir_keeps_cache_next_to_exe_and_becomes_absolute()
    {
        var p = AppPaths.Resolve(new LaunchOptions("rel/data"), "/opt/todokun");
        Assert.Equal(Path.GetFullPath("rel/data"), p.DataRoot);
        Assert.True(Path.IsPathRooted(p.DataRoot));
        Assert.Equal(Path.GetFullPath("/opt/todokun/cache"), p.CacheRoot);
        Assert.True(p.IsCustomDataRoot);
    }

    [Fact]
    public void ExeDir_falls_back_to_base_directory()
    {
        Assert.Equal("/b", AppPaths.ExeDirOf(null, "/b"));
        Assert.Equal("/b", AppPaths.ExeDirOf("", "/b"));
        Assert.Equal("/e", AppPaths.ExeDirOf("/e/todokun", "/b"));
    }

    [Fact]
    public void Probe_creates_missing_folder_and_leaves_no_file()
    {
        using var t = new TempDir();
        var dir = t.Combine("new", "deep");
        Assert.True(AppPaths.ProbeWritable(dir, out var reason));
        Assert.Null(reason);
        Assert.Empty(Directory.GetFileSystemEntries(dir));
    }

    [Fact]
    public void Probe_fails_when_path_is_a_file()
    {
        using var t = new TempDir();
        var f = t.Combine("file");
        File.WriteAllText(f, "x");
        Assert.False(AppPaths.ProbeWritable(f, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }

    [Fact]
    public void Probe_fails_when_parent_is_a_file()
    {
        using var t = new TempDir();
        var f = t.Combine("file");
        File.WriteAllText(f, "x");
        Assert.False(AppPaths.ProbeWritable(Path.Combine(f, "sub"), out _));
    }

    [Fact]
    public void Probe_fails_on_read_only_folder()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var t = new TempDir();
        var dir = t.Combine("ro");
        Directory.CreateDirectory(dir);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        Assert.False(AppPaths.ProbeWritable(dir, out var reason));
        Assert.False(string.IsNullOrEmpty(reason));
    }
}

public class AtomicFileTests
{
    [Fact]
    public void Writes_new_file_and_creates_parent()
    {
        using var t = new TempDir();
        var p = t.Combine("a", "b", "f.txt");
        AtomicFile.WriteAllText(p, "こんにちは");
        Assert.Equal("こんにちは", File.ReadAllText(p));
    }

    [Fact]
    public void Overwrites_existing_and_leaves_no_temp()
    {
        using var t = new TempDir();
        var p = t.Combine("f.txt");
        AtomicFile.WriteAllText(p, "1");
        AtomicFile.WriteAllText(p, "2");
        Assert.Equal("2", File.ReadAllText(p));
        Assert.Equal([p], Directory.GetFiles(t.Path));
    }

    [Fact]
    public void Writes_utf8_without_bom()
    {
        using var t = new TempDir();
        var p = t.Combine("f.txt");
        AtomicFile.WriteAllText(p, "a");
        Assert.Equal([(byte)'a'], File.ReadAllBytes(p));
    }
}

public class DayFileTests
{
    private const string Root = "/data";

    private static string P(params string[] parts) => Path.Combine([Root, .. parts]);

    [Fact]
    public void PathOf_zero_pads_month()
    {
        Assert.Equal(P("2026", "01", "2026-01-05.md"), DayFile.PathOf(Root, new DateOnly(2026, 1, 5)));
    }

    [Fact]
    public void TryParse_accepts_the_canonical_shape()
    {
        Assert.True(DayFile.TryParse(Root, P("2026", "10", "2026-10-14.md"), out var d));
        Assert.Equal(new DateOnly(2026, 10, 14), d);
    }

    [Theory]
    [InlineData("2026/10/2026-10-14.md.abc.tmp")]   // 一時ファイル
    [InlineData("2026/10/2026-10-14.txt")]          // 他の拡張子
    [InlineData("2026/10/2026-11-02.md")]           // 年月がフォルダとずれる
    [InlineData("2025/10/2026-10-14.md")]
    [InlineData("2026/1/2026-01-05.md")]            // 月が 1 桁
    [InlineData("26/10/2026-10-14.md")]
    [InlineData("2026/10/2026-13-01.md")]           // 日付として不正
    [InlineData("2026/10/2026-10-1.md")]
    [InlineData("2026-10-14.md")]                   // 階層が違う
    [InlineData("2026/10/x/2026-10-14.md")]
    [InlineData("2026/10/memo.md")]
    public void TryParse_rejects_other_shapes(string rel)
    {
        Assert.False(DayFile.TryParse(Root, Path.Combine(Root, rel.Replace('/', Path.DirectorySeparatorChar)), out _));
    }
}

public class DayStoreTests
{
    private static readonly DateOnly Day = new(2026, 10, 14);

    [Fact]
    public void Missing_when_no_file()
    {
        using var t = new TempDir();
        Assert.IsType<DayContent.Missing>(new DayStore(t.Path).Read(Day));
    }

    [Fact]
    public void Write_uses_lf_no_bom_and_read_returns_text()
    {
        using var t = new TempDir();
        var s = new DayStore(t.Path);
        s.Write(Day, "a\r\nb\r\n");
        var bytes = File.ReadAllBytes(s.PathOf(Day));
        Assert.Equal("a\nb\n", Encoding.UTF8.GetString(bytes));
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("a\nb\n", Assert.IsType<DayContent.Loaded>(s.Read(Day)).Text);
    }

    [Fact]
    public void Read_accepts_bom_and_keeps_crlf()
    {
        using var t = new TempDir();
        var s = new DayStore(t.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(s.PathOf(Day))!);
        File.WriteAllBytes(s.PathOf(Day), [.. new byte[] { 0xEF, 0xBB, 0xBF }, .. Encoding.UTF8.GetBytes("あ\r\nい")]);
        Assert.Equal("あ\r\nい", Assert.IsType<DayContent.Loaded>(s.Read(Day)).Text);
    }

    [Fact]
    public void Non_utf8_is_failed()
    {
        using var t = new TempDir();
        var s = new DayStore(t.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(s.PathOf(Day))!);
        File.WriteAllBytes(s.PathOf(Day), [0x82, 0xA0, 0x82, 0xA2]);   // Shift_JIS の「あい」
        Assert.IsType<DayContent.Failed>(s.Read(Day));
    }

    [Fact]
    public void Delete_removes_file_and_is_idempotent()
    {
        using var t = new TempDir();
        var s = new DayStore(t.Path);
        s.Write(Day, "x");
        s.Delete(Day);
        s.Delete(Day);
        Assert.IsType<DayContent.Missing>(s.Read(Day));
    }

    [Fact]
    public void Write_failure_throws_and_keeps_original()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var t = new TempDir();
        var s = new DayStore(t.Path);
        s.Write(Day, "original");
        var dir = Path.GetDirectoryName(s.PathOf(Day))!;
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        var ex = Assert.ThrowsAny<Exception>(() => s.Write(Day, "new"));
        Assert.True(ex is IOException or UnauthorizedAccessException);
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        Assert.Equal("original", Assert.IsType<DayContent.Loaded>(s.Read(Day)).Text);
    }
}

public class DayIndexTests
{
    private static void Touch(string root, string rel)
    {
        var p = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllText(p, "x");
    }

    [Fact]
    public void Scans_only_day_files_and_ignores_the_rest()
    {
        using var t = new TempDir();
        Touch(t.Path, "2026/10/2026-10-14.md");
        Touch(t.Path, "2026/10/2026-10-13.md");
        Touch(t.Path, "2026/10/2026-11-02.md");        // 年月ずれ
        Touch(t.Path, "2026/10/2026-10-12.md.x.tmp");
        Touch(t.Path, "2026/10/notes.md");
        Touch(t.Path, "readme.md");
        var idx = new DayIndex(t.Path);
        Assert.Equal(2, idx.Count);
        Assert.True(File.Exists(Path.Combine(t.Path, "readme.md")));   // 消さない
    }

    [Fact]
    public void Missing_root_or_file_root_is_empty()
    {
        using var t = new TempDir();
        Assert.Equal(0, new DayIndex(t.Combine("nope")).Count);
        var f = t.Combine("file");
        File.WriteAllText(f, "x");
        Assert.Equal(0, new DayIndex(f).Count);
    }

    [Fact]
    public void DaysIn_is_descending_per_month()
    {
        using var t = new TempDir();
        foreach (var d in new[] { "2026/10/2026-10-02.md", "2026/10/2026-10-30.md", "2026/10/2026-10-14.md", "2026/09/2026-09-30.md" })
            Touch(t.Path, d);
        var idx = new DayIndex(t.Path);
        Assert.Equal([new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 14), new DateOnly(2026, 10, 2)], idx.DaysIn(2026, 10));
        Assert.Equal([new DateOnly(2026, 9, 30)], idx.DaysIn(2026, 9));
        Assert.Empty(idx.DaysIn(2026, 8));
    }

    [Fact]
    public void Previous_and_next_cross_months_and_years()
    {
        using var t = new TempDir();
        foreach (var d in new[] { "2025/12/2025-12-31.md", "2026/01/2026-01-02.md", "2026/03/2026-03-01.md" })
            Touch(t.Path, d);
        var idx = new DayIndex(t.Path);
        Assert.Equal(new DateOnly(2025, 12, 31), idx.Previous(new DateOnly(2026, 1, 2)));
        Assert.Equal(new DateOnly(2026, 1, 2), idx.Next(new DateOnly(2025, 12, 31)));
        Assert.Equal(new DateOnly(2026, 3, 1), idx.Next(new DateOnly(2026, 1, 2)));
    }

    [Fact]
    public void Previous_and_next_work_from_a_day_without_file_and_return_null_at_ends()
    {
        using var t = new TempDir();
        Touch(t.Path, "2026/10/2026-10-10.md");
        Touch(t.Path, "2026/10/2026-10-20.md");
        var idx = new DayIndex(t.Path);
        var mid = new DateOnly(2026, 10, 15);
        Assert.Equal(new DateOnly(2026, 10, 10), idx.Previous(mid));
        Assert.Equal(new DateOnly(2026, 10, 20), idx.Next(mid));
        Assert.Null(idx.Previous(new DateOnly(2026, 10, 10)));
        Assert.Null(idx.Next(new DateOnly(2026, 10, 20)));
        Assert.Null(new DayIndex(t.Combine("empty")).Previous(mid));
    }

    [Fact]
    public void Future_days_are_indexed()
    {
        using var t = new TempDir();
        Touch(t.Path, "2099/01/2099-01-01.md");
        Assert.True(new DayIndex(t.Path).Contains(new DateOnly(2099, 1, 1)));
    }

    [Fact]
    public void Add_and_remove_raise_changed_only_on_real_change()
    {
        using var t = new TempDir();
        var idx = new DayIndex(t.Path);
        var n = 0;
        idx.Changed += () => n++;
        var d = new DateOnly(2026, 10, 14);
        idx.Add(d); idx.Add(d);
        Assert.Equal(1, n);
        idx.Remove(d); idx.Remove(d);
        Assert.Equal(2, n);
    }

    [Fact]
    public void Handles_365_files()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        for (var i = 0; i < 365; i++) store.Write(new DateOnly(2025, 1, 1).AddDays(i), "x");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var idx = new DayIndex(t.Path);
        Assert.Equal(365, idx.Count);
        Assert.True(sw.ElapsedMilliseconds < 3000);
    }
}

public class StartupCopyTests
{
    private static readonly DateOnly Today = new(2026, 10, 14);

    private static (DayStore store, DayIndex index) Make(TempDir t, params (string day, string text)[] files)
    {
        var store = new DayStore(t.Path);
        foreach (var (day, text) in files) store.Write(DateOnly.Parse(day), text);
        return (store, new DayIndex(t.Path));
    }

    [Fact]
    public void Today_exists_does_nothing()
    {
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-13", "old"), ("2026-10-14", "today"));
        Assert.IsType<StartupCopyResult.NotNeeded>(StartupCopy.Run(Today, s, i));
        Assert.Equal("today", Assert.IsType<DayContent.Loaded>(s.Read(Today)).Text);
    }

    [Fact]
    public void Today_exists_but_unreadable_is_not_overwritten()
    {
        using var t = new TempDir();
        var (s, _) = Make(t, ("2026-10-13", "old"));
        Directory.CreateDirectory(Path.GetDirectoryName(s.PathOf(Today))!);
        File.WriteAllBytes(s.PathOf(Today), [0x82, 0xA0]);
        var i = new DayIndex(t.Path);
        Assert.IsType<StartupCopyResult.NotNeeded>(StartupCopy.Run(Today, s, i));
        Assert.Equal([0x82, 0xA0], File.ReadAllBytes(s.PathOf(Today)));
    }

    [Fact]
    public void Copies_latest_past_day_and_adds_today_to_index()
    {
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-10", "A"), ("2026-10-12", "- [ ] B\n"));
        var r = Assert.IsType<StartupCopyResult.Copied>(StartupCopy.Run(Today, s, i));
        Assert.Equal(new DateOnly(2026, 10, 12), r.From);
        Assert.Equal("- [ ] B\n", Assert.IsType<DayContent.Loaded>(s.Read(Today)).Text);
        Assert.True(i.Contains(Today));
    }

    [Fact]
    public void Copy_goes_through_lf_normalization()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(store.PathOf(new DateOnly(2026, 10, 13)))!);
        File.WriteAllBytes(store.PathOf(new DateOnly(2026, 10, 13)), Encoding.UTF8.GetBytes("a\r\nb"));
        var i = new DayIndex(t.Path);
        StartupCopy.Run(Today, store, i);
        Assert.Equal("a\nb", Encoding.UTF8.GetString(File.ReadAllBytes(store.PathOf(Today))));
    }

    [Fact]
    public void No_source_does_nothing()
    {
        using var t = new TempDir();
        var (s, i) = Make(t);
        Assert.IsType<StartupCopyResult.NoSource>(StartupCopy.Run(Today, s, i));
        Assert.IsType<DayContent.Missing>(s.Read(Today));
        Assert.False(i.Contains(Today));
    }

    [Fact]
    public void Whitespace_only_sources_are_skipped()
    {
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-10", "real"), ("2026-10-13", " \n\n  "));
        var r = Assert.IsType<StartupCopyResult.Copied>(StartupCopy.Run(Today, s, i));
        Assert.Equal(new DateOnly(2026, 10, 10), r.From);
    }

    [Fact]
    public void Only_whitespace_sources_is_no_source()
    {
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-13", "\n"));
        Assert.IsType<StartupCopyResult.NoSource>(StartupCopy.Run(Today, s, i));
    }

    [Fact]
    public void Future_days_are_not_sources()
    {
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-15", "future"));
        Assert.IsType<StartupCopyResult.NoSource>(StartupCopy.Run(Today, s, i));
    }

    [Fact]
    public void Unreadable_newest_is_skipped_and_older_readable_is_used()
    {
        using var t = new TempDir();
        var (s, _) = Make(t, ("2026-10-10", "good"));
        var bad = s.PathOf(new DateOnly(2026, 10, 13));
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        File.WriteAllBytes(bad, [0x82, 0xA0]);
        var i = new DayIndex(t.Path);
        var r = Assert.IsType<StartupCopyResult.Copied>(StartupCopy.Run(Today, s, i));
        Assert.Equal(new DateOnly(2026, 10, 10), r.From);
    }

    [Fact]
    public void Only_unreadable_or_blank_sources_is_failed()
    {
        using var t = new TempDir();
        var (s, _) = Make(t, ("2026-10-10", " "));
        var bad = s.PathOf(new DateOnly(2026, 10, 13));
        Directory.CreateDirectory(Path.GetDirectoryName(bad)!);
        File.WriteAllBytes(bad, [0x82, 0xA0]);
        var i = new DayIndex(t.Path);
        Assert.IsType<StartupCopyResult.Failed>(StartupCopy.Run(Today, s, i));
        Assert.IsType<DayContent.Missing>(s.Read(Today));
    }

    [Fact]
    public void Write_failure_is_failed_and_index_unchanged()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        using var t = new TempDir();
        var (s, i) = Make(t, ("2026-10-13", "x"));
        var month = Path.GetDirectoryName(s.PathOf(Today))!;
        File.SetUnixFileMode(month, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.IsType<StartupCopyResult.Failed>(StartupCopy.Run(Today, s, i));
            Assert.False(i.Contains(Today));
        }
        finally
        {
            File.SetUnixFileMode(month, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}

public class SingleInstanceTests
{
    [Fact]
    public void Second_acquire_for_same_root_is_refused_and_other_root_is_allowed()
    {
        using var t = new TempDir();
        using var first = SingleInstance.TryAcquire(t.Combine("a"));
        if (first.Status == SingleInstanceStatus.Unavailable) return;   // この環境では Mutex が使えない
        Assert.Equal(SingleInstanceStatus.Acquired, first.Status);

        using var second = SingleInstance.TryAcquire(t.Combine("a"));
        Assert.Equal(SingleInstanceStatus.AlreadyRunning, second.Status);

        using var other = SingleInstance.TryAcquire(t.Combine("b"));
        Assert.Equal(SingleInstanceStatus.Acquired, other.Status);
    }

    [Fact]
    public void Released_root_can_be_acquired_again()
    {
        using var t = new TempDir();
        var root = t.Combine("a");
        var first = SingleInstance.TryAcquire(root);
        if (first.Status != SingleInstanceStatus.Acquired) return;
        first.Dispose();
        using var again = SingleInstance.TryAcquire(root);
        Assert.Equal(SingleInstanceStatus.Acquired, again.Status);
    }

    [Fact]
    public void Name_ignores_trailing_separator_and_case()
    {
        var a = SingleInstance.NameFor("/Tmp/Todokun-Data");
        Assert.Equal(a, SingleInstance.NameFor("/Tmp/Todokun-Data/"));
        Assert.Equal(a, SingleInstance.NameFor("/tmp/todokun-data"));
        Assert.StartsWith("todokun-", a);
        Assert.Equal("todokun-".Length + 16, a.Length);
        Assert.NotEqual(a, SingleInstance.NameFor("/tmp/other"));
    }
}
