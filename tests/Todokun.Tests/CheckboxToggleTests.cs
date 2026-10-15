using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>ビューのチェックボックスのクリック（楽観的更新の裏側）。ファイルを読み直し、検証して、1 文字だけ書く。</summary>
public sealed class CheckboxToggleTests : IDisposable
{
    private static readonly DateOnly D = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly MemoLibrary _memo;
    private readonly MemoEditorSession _s;
    private bool _canWrite = true;
    private readonly List<string> _events = [];

    public CheckboxToggleTests()
    {
        _memo = new MemoLibrary(_t.Path);
        _s = new MemoEditorSession(_memo, new MemoIndex(_memo), _ui, () => (_canWrite, _canWrite ? null : "書けません"));
        _s.ContentChanged += reload => _events.Add(reload ? "changed:reload" : "changed");
        _s.ContentUpdatedInPlace += () => _events.Add("inplace");
    }

    public void Dispose() => _t.Dispose();

    private void Open(string text)
    {
        _memo.Write("m", text);
        _s.Open("m");
        _events.Clear();
    }

    [Fact]
    public void Toggle_writes_one_char_updates_content_and_does_not_redraw()
    {
        Open("- [ ] a\n- [ ] b\n");
        Assert.True(_s.ToggleCheck(1, true));
        Assert.Equal("- [ ] a\n- [x] b\n", File.ReadAllText(_memo.PathOf("m")));
        Assert.Equal("- [ ] a\n- [x] b\n", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        Assert.Equal(["inplace"], _events);   // 楽観的更新：ページは作り直さない
        Assert.True(_s.IsViewing);
        Assert.Null(_s.Message);
    }

    [Fact]
    public void Preview_text_follows_the_toggle()
    {
        Open("- [ ] a");
        _s.ToggleCheck(0, true);
        Assert.Equal("- [x] a", Assert.IsType<PreviewSource.Markdown>(_s.Preview).Text);
    }

    [Fact]
    public void Uncheck_works_and_a_second_toggle_after_the_first()
    {
        Open("- [x] a\n- [ ] b");
        Assert.True(_s.ToggleCheck(0, false));
        Assert.True(_s.ToggleCheck(1, true));
        Assert.Equal("- [ ] a\n- [x] b", File.ReadAllText(_memo.PathOf("m")));
    }

    [Fact]
    public void File_changed_outside_is_respected_not_overwritten_with_stale_text()
    {
        Open("- [ ] a\n- [ ] b");
        File.WriteAllText(_memo.PathOf("m"), "- [ ] a\n- [ ] b\n- [ ] added outside");   // 表示した後に外で追記
        Assert.True(_s.ToggleCheck(0, true));
        Assert.Equal("- [x] a\n- [ ] b\n- [ ] added outside", File.ReadAllText(_memo.PathOf("m")));
    }

    [Fact]
    public void Line_that_is_no_longer_a_task_is_refused_and_the_page_is_redrawn_from_the_file()
    {
        Open("- [ ] a\n- [ ] b");
        File.WriteAllText(_memo.PathOf("m"), "changed outside\n- [ ] b");
        Assert.False(_s.ToggleCheck(0, true));
        Assert.Equal("changed outside\n- [ ] b", File.ReadAllText(_memo.PathOf("m")));   // 書き換えない
        Assert.Equal(["changed:reload"], _events);
        Assert.Equal("changed outside\n- [ ] b", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        Assert.StartsWith("チェックボックスを更新できませんでした", _s.Message);
    }

    [Fact]
    public void Already_in_the_requested_state_writes_nothing()
    {
        Open("- [ ] a");
        File.WriteAllText(_memo.PathOf("m"), "- [x] a");   // 外で先にチェックされた
        var before = File.GetLastWriteTimeUtc(_memo.PathOf("m"));
        Thread.Sleep(20);
        Assert.True(_s.ToggleCheck(0, true));
        Assert.Equal(before, File.GetLastWriteTimeUtc(_memo.PathOf("m")));
        Assert.Equal("- [x] a", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        Assert.Equal(["inplace"], _events);
    }

    [Fact]
    public void Unwritable_data_root_refuses_and_reverts_the_page()
    {
        Open("- [ ] a");
        _canWrite = false;
        Assert.False(_s.ToggleCheck(0, true));
        Assert.Equal("- [ ] a", File.ReadAllText(_memo.PathOf("m")));
        Assert.Equal(["changed:reload"], _events);
        Assert.StartsWith("データの保存先に書き込めないため", _s.Message);
    }

    [Fact]
    public void Unreadable_file_is_not_overwritten()
    {
        Open("- [ ] a");
        byte[] sjis = [0x82, 0xA0, 0x82, 0xA2];
        File.WriteAllBytes(_memo.PathOf("m"), sjis);
        Assert.False(_s.ToggleCheck(0, true));
        Assert.Equal(sjis, File.ReadAllBytes(_memo.PathOf("m")));
        Assert.IsType<DayContent.Failed>(_s.Content);
        Assert.Equal(["changed:reload"], _events);
    }

    [Fact]
    public void File_deleted_outside_shows_missing_and_does_not_recreate_it()
    {
        Open("- [ ] a");
        File.Delete(_memo.PathOf("m"));
        Assert.False(_s.ToggleCheck(0, true));
        Assert.False(File.Exists(_memo.PathOf("m")));
        Assert.IsType<DayContent.Missing>(_s.Content);
        Assert.Equal(["changed:reload"], _events);
    }

    [Fact]
    public void Write_failure_keeps_the_file_and_reverts_the_page()
    {
        if (OperatingSystem.IsWindows()) return;
        Open("- [ ] a");
        File.SetUnixFileMode(_memo.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.False(_s.ToggleCheck(0, true));
        }
        finally
        {
            File.SetUnixFileMode(_memo.Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Equal("- [ ] a", File.ReadAllText(_memo.PathOf("m")));
        Assert.StartsWith("保存できませんでした", _s.Message);
        Assert.Equal(["changed:reload"], _events);
    }

    [Fact]
    public void Ignored_while_editing_and_while_confirming()
    {
        Open("- [ ] a");
        _s.BeginEdit();
        Assert.False(_s.ToggleCheck(0, true));
        Assert.Equal("- [ ] a", File.ReadAllText(_memo.PathOf("m")));
        Assert.Empty(_events);
    }

    [Fact]
    public void Day_session_toggles_through_the_same_path_and_updates_the_index()
    {
        var store = new DayStore(_t.Path);
        var index = new DayIndex(_t.Path);
        store.Write(D, "- [ ] a");
        index.Add(D);
        var day = new DayEditorSession(store, index, _ui);
        day.Open(D);
        Assert.True(day.ToggleCheck(0, true));
        Assert.Equal("- [x] a", File.ReadAllText(store.PathOf(D)));
        Assert.True(index.Contains(D));
    }

    [Fact]
    public void Crlf_file_is_normalised_to_lf_on_write_like_any_save()
    {
        Open("- [ ] a\r\n- [ ] b");
        _s.ToggleCheck(0, true);
        Assert.Equal("- [x] a\n- [ ] b", File.ReadAllText(_memo.PathOf("m")));
    }
}
