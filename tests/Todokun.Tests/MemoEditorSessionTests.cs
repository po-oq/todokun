using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>メモのセッション。状態機械そのものは DayEditorSession の既存テストが持つ。ここは「メモの違い」と共通部分の要点。</summary>
public sealed class MemoEditorSessionTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly MemoStore _store;
    private readonly MemoEditorSession _s;
    private bool _canWrite = true;

    public MemoEditorSessionTests()
    {
        _store = new MemoStore(_t.Path);
        _s = new MemoEditorSession(_store, _ui, () => (_canWrite, _canWrite ? null : "書けません"));
    }

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Header_is_memo() => Assert.Equal("メモ", _s.HeaderText);

    [Fact]
    public void Missing_memo_shows_the_guide_message()
    {
        _s.Load();
        var m = Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.Equal("メモはまだありません。『編集』で書けます", m.Text);
        Assert.True(_s.IsViewing);
    }

    [Fact]
    public void Loaded_memo_previews_with_the_data_root_as_base()
    {
        _store.Write("# hello");
        _s.Load();
        var md = Assert.IsType<PreviewSource.Markdown>(_s.Preview);
        Assert.Equal("# hello", md.Text);
        Assert.Equal(_t.Path, md.BaseFolder);
        Assert.Equal(_store.Path, md.CacheKey);
    }

    [Fact]
    public async Task Save_writes_the_file_and_stays_in_edit_mode()
    {
        _s.Load();
        Assert.True(_s.BeginEdit());
        _s.SetDraft("a\nb");
        Assert.Equal(SaveResult.Saved, await _s.SaveAsync());
        Assert.True(_s.IsEditing);
        Assert.False(_s.IsDirty);
        Assert.Equal("a\nb", File.ReadAllText(_store.Path));
    }

    [Fact]
    public async Task Save_without_change_writes_nothing()
    {
        _store.Write("x");
        var before = File.GetLastWriteTimeUtc(_store.Path);
        _s.Load();
        _s.BeginEdit();
        Thread.Sleep(20);
        Assert.Equal(SaveResult.NoChange, await _s.SaveAsync());
        Assert.Equal(before, File.GetLastWriteTimeUtc(_store.Path));
    }

    [Fact]
    public void Unreadable_memo_cannot_be_edited_and_is_not_overwritten()
    {
        byte[] sjis = [0x82, 0xA0, 0x82, 0xA2];
        File.WriteAllBytes(_store.Path, sjis);
        _s.Load();
        Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.StartsWith("メモのファイルは読めないため、編集できません：", _s.Message);
        Assert.Equal(sjis, File.ReadAllBytes(_store.Path));
    }

    [Fact]
    public void Unwritable_data_root_blocks_editing()
    {
        _canWrite = false;
        _s.Load();
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.StartsWith("データの保存先に書き込めないため、編集できません：", _s.Message);
    }

    [Fact]
    public async Task Blank_save_confirms_then_deletes_the_file()
    {
        _store.Write("x");
        _s.Load();
        _s.BeginEdit();
        _s.SetDraft("  \n");
        Assert.Equal(SaveResult.Deleted, await _s.SaveAsync());
        Assert.Equal("メモのファイルを削除します。よろしいですか？", _ui.LastConfirmDeleteMessage);
        Assert.False(_store.Exists());
        Assert.IsType<PreviewSource.Message>(_s.Preview);
    }

    [Fact]
    public async Task Blank_save_cancelled_keeps_the_file()
    {
        _ui.DeleteAnswer = false;
        _store.Write("x");
        _s.Load();
        _s.BeginEdit();
        _s.SetDraft("");
        Assert.Equal(SaveResult.Cancelled, await _s.SaveAsync());
        Assert.True(_store.Exists());
        Assert.True(_s.IsEditing);
    }

    [Fact]
    public async Task Leaving_with_changes_asks_with_the_memo_message()
    {
        _ui.SaveAnswer = SaveChoice.Discard;
        _s.Load();
        _s.BeginEdit();
        _s.SetDraft("changed");
        Assert.True(await _s.LeaveEditAsync());
        Assert.Equal("メモが保存されていません。保存しますか？", _ui.LastAskSaveMessage);
        Assert.True(_s.IsViewing);
        Assert.False(_store.Exists());
    }

    [Fact]
    public async Task ContentChanged_fires_on_load_and_on_save()
    {
        var calls = new List<bool>();
        _s.ContentChanged += reload => calls.Add(reload);
        _s.Load();
        _s.BeginEdit();
        _s.SetDraft("x");
        await _s.SaveAsync();
        Assert.Equal([false, true], calls);
    }
}

public sealed class ModEToggleTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly MemoEditorSession _s;
    private long _now;
    private readonly ModEToggle _toggle;

    public ModEToggleTests()
    {
        _s = new MemoEditorSession(new MemoStore(_t.Path), _ui);
        _s.Load();
        _toggle = new ModEToggle(_s, () => _now);
    }

    public void Dispose() => _t.Dispose();

    [Fact]
    public void Enters_edit_from_view_and_does_not_bounce_back_inside_the_window()
    {
        _now = 1000;
        Assert.True(_toggle.OnModE());
        Assert.True(_s.IsEditing);
        _now = 1000 + ModEToggle.WindowMs - 1;
        Assert.True(_toggle.OnModE());
        Assert.True(_s.IsEditing);
    }

    [Fact]
    public void Leaves_edit_after_the_window()
    {
        _now = 1000;
        _toggle.OnModE();
        _now = 1000 + ModEToggle.WindowMs + 1;
        Assert.True(_toggle.OnModE());
        Assert.True(_s.IsViewing);
    }

    [Fact]
    public void Page_begin_edit_then_window_key_does_not_bounce_back()
    {
        _now = 5000;
        _toggle.OnPageBeginEdit();
        Assert.True(_s.IsEditing);
        _now = 5001;
        Assert.True(_toggle.OnModE());
        Assert.True(_s.IsEditing);
    }

    [Fact]
    public async Task Does_not_handle_the_key_while_confirming()
    {
        _now = 1;
        _s.BeginEdit();
        _s.SetDraft("changed");
        bool? handled = null;
        _ui.WhileAsking = () => handled = _toggle.OnModE();
        _now = 1000;
        await _s.LeaveEditAsync();
        Assert.False(handled);
    }
}
