using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>メモのセッション（いま開いている 1 つ）。状態機械そのものは DayEditorSession の既存テストが持つ。ここは「メモの違い」と共通部分の要点。</summary>
public sealed class MemoEditorSessionTests : IDisposable
{
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly MemoLibrary _lib;
    private readonly MemoIndex _index;
    private readonly MemoEditorSession _s;
    private bool _canWrite = true;

    public MemoEditorSessionTests()
    {
        _lib = new MemoLibrary(_t.Path);
        _index = new MemoIndex(_lib);
        _s = new MemoEditorSession(_lib, _index, _ui, () => (_canWrite, _canWrite ? null : "書けません"));
    }

    public void Dispose() => _t.Dispose();

    private void OpenMemo(string title, string? text = null)
    {
        if (text is not null) _lib.Write(title, text);
        else _lib.Create(title);
        _index.Add(title);
        _s.Open(title);
    }

    [Fact]
    public void No_memo_is_open_at_first()
    {
        Assert.Equal("メモなし", _s.HeaderText);
        Assert.Null(_s.Title);
        Assert.False(_s.CanBeginEdit);
        var m = Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.Equal("メモがありません。＋で追加してください", m.Text);
    }

    [Fact]
    public void Open_shows_the_title_as_the_header()
    {
        OpenMemo("買い物", "- [ ] 牛乳");
        Assert.Equal("買い物", _s.Title);
        Assert.Equal("買い物", _s.HeaderText);
        Assert.True(_s.IsViewing);
        Assert.True(_s.CanBeginEdit);
    }

    [Fact]
    public void Loaded_memo_previews_with_the_memos_folder_as_base()
    {
        OpenMemo("a", "# hello");
        var md = Assert.IsType<PreviewSource.Markdown>(_s.Preview);
        Assert.Equal("# hello", md.Text);
        Assert.Equal(_lib.Folder, md.BaseFolder);
        Assert.Equal(_lib.PathOf("a"), md.CacheKey);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\n")]
    public void Blank_memo_shows_the_no_body_message(string text)
    {
        OpenMemo("a", text);
        var m = Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.Equal("本文がありません。『編集』で書けます", m.Text);
    }

    [Fact]
    public void A_memo_whose_file_vanished_shows_a_message_and_can_still_be_edited()
    {
        OpenMemo("a", "x");
        File.Delete(_lib.PathOf("a"));
        _s.Open("a");
        Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.IsType<DayContent.Missing>(_s.Content);
    }

    [Fact]
    public async Task Save_writes_the_file_stays_in_edit_mode_and_touches_the_index()
    {
        OpenMemo("a");
        Assert.True(_s.BeginEdit());
        _s.SetDraft("# 見出し\nb");
        Assert.Equal(SaveResult.Saved, await _s.SaveAsync());
        Assert.True(_s.IsEditing);
        Assert.False(_s.IsDirty);
        Assert.Equal("# 見出し\nb", File.ReadAllText(_lib.PathOf("a")));
        Assert.Equal("見出し", _index.Find("a")!.Snippet);
    }

    [Fact]
    public async Task Save_without_change_writes_nothing()
    {
        OpenMemo("a", "x");
        var before = File.GetLastWriteTimeUtc(_lib.PathOf("a"));
        _s.BeginEdit();
        Thread.Sleep(20);
        Assert.Equal(SaveResult.NoChange, await _s.SaveAsync());
        Assert.Equal(before, File.GetLastWriteTimeUtc(_lib.PathOf("a")));
    }

    [Fact]
    public void Unreadable_memo_cannot_be_edited_and_is_not_overwritten()
    {
        byte[] sjis = [0x82, 0xA0, 0x82, 0xA2];
        Directory.CreateDirectory(_lib.Folder);
        File.WriteAllBytes(_lib.PathOf("a"), sjis);
        _s.Open("a");
        Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.StartsWith("メモのファイルは読めないため、編集できません：", _s.Message);
        Assert.Equal(sjis, File.ReadAllBytes(_lib.PathOf("a")));
    }

    [Fact]
    public void Unwritable_data_root_blocks_editing()
    {
        _canWrite = false;
        OpenMemo("a", "x");
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.StartsWith("データの保存先に書き込めないため、編集できません：", _s.Message);
    }

    [Fact]
    public async Task Blank_save_keeps_the_memo_as_an_empty_file_without_asking()
    {
        OpenMemo("a", "x");
        _s.BeginEdit();
        _s.SetDraft("  \n");
        Assert.Equal(SaveResult.Saved, await _s.SaveAsync());
        Assert.Equal(0, _ui.ConfirmDeleteCount);
        Assert.True(_lib.Exists("a"));
        Assert.True(_index.Contains("a"));
        var m = Assert.IsType<PreviewSource.Message>(_s.Preview);
        Assert.Equal("本文がありません。『編集』で書けます", m.Text);
    }

    [Fact]
    public async Task Leaving_with_changes_asks_with_the_memo_message()
    {
        _ui.SaveAnswer = SaveChoice.Discard;
        OpenMemo("a", "x");
        _s.BeginEdit();
        _s.SetDraft("changed");
        Assert.True(await _s.LeaveEditAsync());
        Assert.Equal("メモが保存されていません。保存しますか？", _ui.LastAskSaveMessage);
        Assert.True(_s.IsViewing);
        Assert.Equal("x", File.ReadAllText(_lib.PathOf("a")));
    }

    [Fact]
    public async Task ContentChanged_fires_on_open_and_on_save()
    {
        var calls = new List<bool>();
        _s.ContentChanged += reload => calls.Add(reload);
        OpenMemo("a");
        _s.BeginEdit();
        _s.SetDraft("x");
        await _s.SaveAsync();
        Assert.Equal([false, true], calls);
    }

    // ── 別のメモを開く ──

    [Fact]
    public async Task TryOpen_opens_another_memo_from_view()
    {
        OpenMemo("a", "A");
        _lib.Write("b", "B");
        _index.Add("b");

        Assert.True(await _s.TryOpenAsync("b"));

        Assert.Equal("b", _s.Title);
        Assert.Equal("B", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
    }

    [Fact]
    public async Task TryOpen_asks_when_there_are_unsaved_changes_and_cancel_keeps_the_memo()
    {
        _ui.SaveAnswer = SaveChoice.Cancel;
        OpenMemo("a", "A");
        _lib.Write("b", "B");
        _s.BeginEdit();
        _s.SetDraft("changed");

        Assert.False(await _s.TryOpenAsync("b"));

        Assert.Equal("a", _s.Title);
        Assert.True(_s.IsEditing);
        Assert.Equal("changed", _s.Draft);
    }

    [Fact]
    public async Task TryOpen_with_save_writes_the_old_memo_then_opens()
    {
        _ui.SaveAnswer = SaveChoice.Save;
        OpenMemo("a", "A");
        _lib.Write("b", "B");
        _s.BeginEdit();
        _s.SetDraft("changed");

        Assert.True(await _s.TryOpenAsync("b"));

        Assert.Equal("b", _s.Title);
        Assert.Equal("changed", File.ReadAllText(_lib.PathOf("a")));
    }

    [Fact]
    public async Task TryOpen_while_confirming_does_nothing()
    {
        OpenMemo("a", "A");
        _lib.Write("b", "B");
        _s.BeginEdit();
        _s.SetDraft("changed");
        bool? inner = null;
        _ui.WhileAsking = () => inner = _s.TryOpenAsync("b").GetAwaiter().GetResult();
        _ui.SaveAnswer = SaveChoice.Cancel;

        await _s.LeaveEditAsync();

        Assert.False(inner);
        Assert.Equal("a", _s.Title);
    }

    // ── 改名・メモなし ──

    [Fact]
    public async Task RenameTo_keeps_the_draft_and_mode_and_saves_to_the_new_name()
    {
        OpenMemo("old", "x");
        _s.BeginEdit();
        _s.SetDraft("typing");
        var titleChanges = new List<string?>();
        _s.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MemoEditorSession.HeaderText)) titleChanges.Add(_s.HeaderText);
        };

        var final = _lib.Rename("old", "new");
        _s.RenameTo(final);

        Assert.Equal("new", _s.Title);
        Assert.Equal(["new"], titleChanges);
        Assert.True(_s.IsEditing);
        Assert.Equal("typing", _s.Draft);
        Assert.True(_s.IsDirty);
        Assert.Equal(SaveResult.Saved, await _s.SaveAsync());
        Assert.Equal("typing", File.ReadAllText(_lib.PathOf("new")));
        Assert.False(_lib.Exists("old"));
    }

    [Fact]
    public void RenameTo_in_view_rebuilds_the_preview()
    {
        OpenMemo("old", "# x");
        var calls = new List<bool>();
        _s.ContentChanged += reload => calls.Add(reload);

        _s.RenameTo(_lib.Rename("old", "new"));

        Assert.Equal([true], calls);
        var md = Assert.IsType<PreviewSource.Markdown>(_s.Preview);
        Assert.Equal(_lib.PathOf("new"), md.CacheKey);
    }

    [Fact]
    public void RenameTo_in_edit_does_not_redraw_the_page()
    {
        OpenMemo("old", "# x");
        _s.BeginEdit();
        var calls = 0;
        _s.ContentChanged += _ => calls++;
        _s.RenameTo("new");
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Clear_returns_to_no_memo()
    {
        OpenMemo("a", "- [ ] x");
        _s.Clear();

        Assert.Null(_s.Title);
        Assert.Equal("メモなし", _s.HeaderText);
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.Equal("メモがありません。＋で追加してください", _s.Message);
        Assert.False(_s.ToggleCheck(0, true));
        Assert.Equal("- [ ] x", File.ReadAllText(_lib.PathOf("a")));
    }

    [Fact]
    public void Opening_after_clear_enables_editing_again()
    {
        OpenMemo("a", "x");
        _s.Clear();
        Assert.False(_s.CanBeginEdit);
        _s.Open("a");
        Assert.True(_s.CanBeginEdit);
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
        var lib = new MemoLibrary(_t.Path);
        _s = new MemoEditorSession(lib, new MemoIndex(lib), _ui);
        _s.Open(lib.Create("m"));
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
