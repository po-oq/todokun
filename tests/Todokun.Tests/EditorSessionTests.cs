using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>計画 11.4 の表の行ごと。Core の一時フォルダ＋ダイアログはフェイク。</summary>
public sealed class EditorSessionTests : IDisposable
{
    private static readonly DateOnly D = new(2026, 10, 14);
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly DayStore _store;
    private readonly DayIndex _index;
    private readonly DayEditorSession _s;
    private bool _canWrite = true;

    public EditorSessionTests()
    {
        _store = new DayStore(_t.Path);
        _index = new DayIndex(_t.Path);
        _s = new DayEditorSession(_store, _index, _ui, () => (_canWrite, _canWrite ? null : "書けません"));
    }

    public void Dispose() => _t.Dispose();

    private void Seed(string text, DateOnly? day = null)
    {
        _store.Write(day ?? D, text);
        _index.Add(day ?? D);
    }

    private void WriteBadBytes(DateOnly day)
    {
        var p = _store.PathOf(day);
        Directory.CreateDirectory(Path.GetDirectoryName(p)!);
        File.WriteAllBytes(p, [0x82, 0xA0, 0x82, 0xA2]);   // Shift_JIS
    }

    // ── 日を開く ──
    [Fact]
    public void Open_missing_loaded_and_failed()
    {
        _s.Open(D);
        Assert.IsType<DayContent.Missing>(_s.Content);
        Assert.IsType<PreviewSource.Message>(_s.Preview);

        Seed("- [ ] a");
        _s.Open(D);
        Assert.Equal("- [ ] a", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        var md = Assert.IsType<PreviewSource.Markdown>(_s.Preview);
        Assert.Equal(_store.PathOf(D), md.CacheKey);

        WriteBadBytes(new DateOnly(2026, 10, 13));
        _s.Open(new DateOnly(2026, 10, 13));
        Assert.IsType<DayContent.Failed>(_s.Content);
        Assert.Contains("読めません", Assert.IsType<PreviewSource.Message>(_s.Preview).Text);
        Assert.Equal(EditorMode.View, _s.Mode);
    }

    // ── 編集へ ──
    [Fact]
    public void BeginEdit_copies_content_into_draft_and_base()
    {
        Seed("hello");
        _s.Open(D);
        Assert.True(_s.BeginEdit());
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.Equal("hello", _s.Draft);
        Assert.False(_s.IsDirty);
    }

    [Fact]
    public void BeginEdit_is_idempotent_and_keeps_draft()
    {
        Seed("hello");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("changed");
        Assert.True(_s.BeginEdit());
        Assert.Equal("changed", _s.Draft);   // 二重に届いても入力を失わない
    }

    [Fact]
    public void Failed_day_cannot_be_edited_and_says_why()
    {
        WriteBadBytes(D);
        _s.Open(D);
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.Equal(EditorMode.View, _s.Mode);
        Assert.False(string.IsNullOrEmpty(_s.Message));
        Assert.True(File.Exists(_store.PathOf(D)));
    }

    [Fact]
    public void Unwritable_data_root_blocks_editing_but_not_viewing()
    {
        Seed("x");
        _canWrite = false;
        _s.Open(D);
        Assert.IsType<DayContent.Loaded>(_s.Content);
        Assert.False(_s.CanBeginEdit);
        Assert.False(_s.BeginEdit());
        Assert.Contains("書けません", _s.Message);
    }

    [Fact]
    public void Missing_day_edits_from_empty()
    {
        _s.Open(D);
        Assert.True(_s.BeginEdit());
        Assert.Equal("", _s.Draft);
    }

    // ── 入力・未保存 ──
    [Fact]
    public void Dirty_ignores_crlf_difference()
    {
        Seed("a\nb");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("a\r\nb");
        Assert.False(_s.IsDirty);
        _s.SetDraft("a\r\nc");
        Assert.True(_s.IsDirty);
    }

    // ── 保存 ──
    [Fact]
    public async Task Save_unchanged_writes_nothing_and_stays_in_edit()
    {
        Seed("a");
        var before = File.GetLastWriteTimeUtc(_store.PathOf(D));
        _s.Open(D);
        _s.BeginEdit();
        Assert.Equal(SaveResult.NoChange, await _s.SaveAsync());
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.Equal(before, File.GetLastWriteTimeUtc(_store.PathOf(D)));
    }

    [Fact]
    public async Task Save_writes_updates_index_and_stays_in_edit()
    {
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("- [x] done\r\n");
        var changed = new List<bool>();
        _s.ContentChanged += changed.Add;
        Assert.Equal(SaveResult.Saved, await _s.SaveAsync());
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.False(_s.IsDirty);
        Assert.True(_index.Contains(D));
        Assert.Equal("- [x] done\n", File.ReadAllText(_store.PathOf(D)));
        Assert.Equal("- [x] done\n", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        Assert.Equal([true], changed);   // 同じ日の作り直し
    }

    [Fact]
    public async Task Save_failure_keeps_input_content_and_index()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        Seed("orig");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("new");
        var dir = Path.GetDirectoryName(_store.PathOf(D))!;
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Equal(SaveResult.Failed, await _s.SaveAsync());
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.Equal("new", _s.Draft);
        Assert.True(_s.IsDirty);
        Assert.Equal("orig", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
        Assert.True(_index.Contains(D));
        Assert.False(string.IsNullOrEmpty(_s.Message));
    }

    // ── 空で保存 ──
    [Fact]
    public async Task Blank_save_with_file_confirms_then_deletes_and_stays_in_edit()
    {
        Seed("text");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("  \n\n");
        Assert.Equal(SaveResult.Deleted, await _s.SaveAsync());
        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.False(File.Exists(_store.PathOf(D)));
        Assert.False(_index.Contains(D));
        Assert.IsType<DayContent.Missing>(_s.Content);
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.False(_s.IsDirty);
    }

    [Fact]
    public async Task Blank_save_cancel_changes_nothing()
    {
        Seed("text");
        _ui.DeleteAnswer = false;
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("");
        Assert.Equal(SaveResult.Cancelled, await _s.SaveAsync());
        Assert.True(File.Exists(_store.PathOf(D)));
        Assert.True(_index.Contains(D));
        Assert.True(_s.IsDirty);
    }

    [Fact]
    public async Task Blank_save_without_file_creates_nothing_and_asks_nothing()
    {
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("   ");
        Assert.Equal(SaveResult.NoChange, await _s.SaveAsync());
        Assert.Equal(0, _ui.ConfirmDeleteCount);
        Assert.False(File.Exists(_store.PathOf(D)));
        Assert.False(_s.IsDirty);   // 未保存の表示が消える
    }

    [Fact]
    public async Task Delete_failure_keeps_content_index_and_base()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;
        Seed("text");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("");
        var dir = Path.GetDirectoryName(_store.PathOf(D))!;
        File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.Equal(SaveResult.Failed, await _s.SaveAsync());
        }
        finally
        {
            File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        Assert.IsType<DayContent.Loaded>(_s.Content);
        Assert.True(_index.Contains(D));
        Assert.Equal("text", _s.Base);
        Assert.Equal(EditorMode.Edit, _s.Mode);
    }

    // ── ビューに戻る ──
    [Fact]
    public async Task Leave_without_changes_asks_nothing()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        Assert.True(await _s.LeaveEditAsync());
        Assert.Equal(EditorMode.View, _s.Mode);
        Assert.Equal(0, _ui.AskSaveCount);
    }

    [Fact]
    public async Task Leave_dirty_save_saves_and_returns_to_view()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        _ui.SaveAnswer = SaveChoice.Save;
        Assert.True(await _s.LeaveEditAsync());
        Assert.Equal(EditorMode.View, _s.Mode);
        Assert.Equal("b", File.ReadAllText(_store.PathOf(D)));
    }

    [Fact]
    public async Task Leave_dirty_discard_drops_changes()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        _ui.SaveAnswer = SaveChoice.Discard;
        Assert.True(await _s.LeaveEditAsync());
        Assert.Equal("a", File.ReadAllText(_store.PathOf(D)));
        Assert.Equal(EditorMode.View, _s.Mode);
    }

    [Fact]
    public async Task Leave_dirty_cancel_stays_in_edit()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        _ui.SaveAnswer = SaveChoice.Cancel;
        Assert.False(await _s.LeaveEditAsync());
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.Equal("b", _s.Draft);
    }

    [Fact]
    public async Task Leave_with_save_then_blank_delete_cancelled_stays_in_edit()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("");
        _ui.SaveAnswer = SaveChoice.Save;
        _ui.DeleteAnswer = false;   // 保存を選んだが、削除の確認でキャンセル
        Assert.False(await _s.LeaveEditAsync());
        Assert.Equal(EditorMode.Edit, _s.Mode);
        Assert.True(File.Exists(_store.PathOf(D)));
    }

    // ── 別の日を開く ──
    [Fact]
    public async Task Open_other_day_while_dirty_confirms_and_cancel_keeps_day()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        _ui.SaveAnswer = SaveChoice.Cancel;
        Assert.False(await _s.TryOpenDayAsync(D.AddDays(-1)));
        Assert.Equal(D, _s.Day);
        Assert.Equal("b", _s.Draft);
    }

    [Fact]
    public async Task Open_other_day_after_save_switches()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        Assert.True(await _s.TryOpenDayAsync(D.AddDays(-1)));
        Assert.Equal(D.AddDays(-1), _s.Day);
        Assert.Equal(EditorMode.View, _s.Mode);
        Assert.Equal("b", File.ReadAllText(_store.PathOf(D)));
    }

    // ── 確認中 ──
    [Fact]
    public async Task While_confirming_moves_edit_requests_and_second_leave_are_ignored()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        var observed = new List<string>();
        _ui.WhileAsking = () =>
        {
            observed.Add($"confirming={_s.IsConfirming}");
            observed.Add($"beginEdit={_s.BeginEdit()}");
            observed.Add($"leave={_s.LeaveEditAsync().IsCompletedSuccessfully && _s.LeaveEditAsync().Result}");
            observed.Add($"open={_s.TryOpenDayAsync(D.AddDays(-1)).Result}");
        };
        _ui.SaveAnswer = SaveChoice.Cancel;
        await _s.LeaveEditAsync();
        Assert.Equal(["confirming=True", "beginEdit=False", "leave=False", "open=False"], observed);
        Assert.Equal(1, _ui.AskSaveCount);   // 2 つ目のダイアログは出ない
        Assert.Equal(D, _s.Day);
    }

    [Fact]
    public async Task Close_uses_the_same_confirmation()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("b");
        _ui.SaveAnswer = SaveChoice.Cancel;
        Assert.False(await _s.CanCloseAsync());
        _ui.SaveAnswer = SaveChoice.Discard;
        Assert.True(await _s.CanCloseAsync());
    }

    [Fact]
    public async Task External_change_is_not_detected_while_editing()
    {
        Seed("a");
        _s.Open(D);
        _s.BeginEdit();
        _s.SetDraft("mine");
        File.WriteAllText(_store.PathOf(D), "external");
        Assert.Equal("mine", _s.Draft);
        Assert.Equal("a", Assert.IsType<DayContent.Loaded>(_s.Content).Text);
    }
}

public class MainViewModelEditingTests
{
    [Fact]
    public async Task Cancelled_confirmation_keeps_selection_and_month()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        var today = new DateOnly(2026, 10, 14);
        store.Write(today, "a");
        store.Write(new DateOnly(2026, 9, 30), "b");
        var ui = new FakeUiServices { SaveAnswer = SaveChoice.Cancel };
        var vm = new MainViewModel(new DayIndex(t.Path), store, today, t.Path, false, ui, t.Combine("p"));
        vm.Editor.BeginEdit();
        vm.Editor.SetDraft("changed");
        Assert.False(await vm.OpenDayAsync(new DateOnly(2026, 9, 30)));
        Assert.Equal(today, vm.LeftPane.Cursor.SelectedDay);
        Assert.Equal(new DateOnly(2026, 10, 1), vm.LeftPane.Cursor.DisplayedMonth);
        Assert.Equal(today, vm.Editor.Day);
    }

    [Fact]
    public void Page_keys_move_to_neighbors_and_begin_edit_is_idempotent()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        var today = new DateOnly(2026, 10, 14);
        foreach (var d in new[] { today, new DateOnly(2026, 10, 12), new DateOnly(2026, 9, 30) }) store.Write(d, "x");
        var vm = new MainViewModel(new DayIndex(t.Path), store, today, t.Path, false, new FakeUiServices(), t.Combine("p"));
        var requested = new List<DateOnly>();
        vm.LeftPane.Cursor.OpenDayRequested += requested.Add;
        vm.OnPageKey(PageKey.ArrowLeft);
        Assert.Equal([new DateOnly(2026, 10, 12)], requested);
        Assert.Equal(new DateOnly(2026, 10, 12), vm.Editor.Day);   // 要求は確定され、日が開いた
        vm.OnPageKey(PageKey.ArrowRight);
        Assert.Equal([new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 14)], requested);
        vm.OnPageKey(PageKey.ArrowRight);   // 端：何もしない
        Assert.Equal(2, requested.Count);
        vm.OnPageKey(PageKey.BeginEdit);
        vm.OnPageKey(PageKey.BeginEdit);
        Assert.True(vm.Editor.IsEditing);
        vm.OnPageKey(PageKey.ArrowLeft);    // 編集中は日を動かさない
        Assert.Equal(2, requested.Count);
    }

    [Fact]
    public void Theme_change_requests_rerender_of_same_content()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        var today = new DateOnly(2026, 10, 14);
        store.Write(today, "x");
        var vm = new MainViewModel(new DayIndex(t.Path), store, today, t.Path, false, new FakeUiServices(), t.Combine("p"));
        var calls = new List<bool>();
        vm.PreviewChanged += (_, reload) => calls.Add(reload);
        vm.OnThemeChanged();
        Assert.Equal([true], calls);
    }

    [Fact]
    public void Local_links_route_through_ui_services()
    {
        using var t = new TempDir();
        var store = new DayStore(t.Path);
        var ui = new FakeUiServices();
        var vm = new MainViewModel(new DayIndex(t.Path), store, new DateOnly(2026, 10, 14), t.Path, false, ui, t.Combine("p"));
        var file = t.Combine("note.md");
        File.WriteAllText(file, "x");
        vm.OpenLocalLink(file);
        Assert.Equal([file], ui.Opened);
        var exe = t.Combine("run.sh");
        File.WriteAllText(exe, "x");
        vm.OpenLocalLink(exe);
        Assert.Equal([exe], ui.Revealed);   // 開いてよい種類でないものは、起動せず Finder で選ぶだけ
    }
}
