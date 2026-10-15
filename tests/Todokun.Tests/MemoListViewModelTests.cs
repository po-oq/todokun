using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>計画 2-3：メモの一覧（追加・改名・削除・絞り込み・選択の確認）。7.4 の「要求 → 確認 → 確定」。</summary>
public sealed class MemoListViewModelTests : IDisposable
{
    private static readonly DateTime Now = DateTime.UtcNow;   // 実際の保存の更新日時と比べるので、実時刻を基準にする
    private readonly TempDir _t = new();
    private readonly FakeUiServices _ui = new();
    private readonly MemoLibrary _lib;
    private readonly MemoIndex _index;
    private readonly MemoEditorSession _editor;
    private readonly MemoListViewModel _vm;

    public MemoListViewModelTests()
    {
        _lib = new MemoLibrary(_t.Path);
        Seed("a", "# A memo", minutesAgo: 30);
        Seed("b", "# B りんご", minutesAgo: 20);
        Seed("c", "# C memo", minutesAgo: 10);
        _index = new MemoIndex(_lib);
        _editor = new MemoEditorSession(_lib, _index, _ui);
        _vm = new MemoListViewModel(_lib, _index, _editor, _ui, () => Now);
        _editor.Open("c");
    }

    public void Dispose() => _t.Dispose();

    private void Seed(string title, string text, int minutesAgo)
    {
        _lib.Write(title, text);
        File.SetLastWriteTimeUtc(_lib.PathOf(title), Now.AddMinutes(-minutesAgo));
    }

    private string[] Titles() => [.. _vm.Items.Select(i => i.Title)];

    // ── 表示 ──

    [Fact]
    public void Items_are_newest_first_with_snippet_and_selection()
    {
        Assert.Equal(["c", "b", "a"], Titles());
        Assert.Equal("C memo", _vm.Items[0].Snippet);
        Assert.True(_vm.Items[0].IsSelected);
        Assert.False(_vm.Items[1].IsSelected);
        Assert.Equal(3, _vm.Count);
        Assert.Equal("3 件", _vm.CountText);
        Assert.False(_vm.IsEmpty);
    }

    [Theory]
    [InlineData(0, "たった今")]
    [InlineData(5, "5 分前")]
    [InlineData(180, "3 時間前")]
    public void Modified_text_is_relative_for_recent_memos(int minutesAgo, string expected)
    {
        Seed("fresh", "x", minutesAgo);
        _index.Add("fresh");
        Assert.Equal(expected, _vm.Items.Single(i => i.Title == "fresh").ModifiedText);
    }

    [Fact]
    public void Modified_text_is_a_date_for_older_memos()
    {
        Seed("old", "x", minutesAgo: 60 * 24 * 3);
        _index.Add("old");
        var expected = Now.AddDays(-3).ToLocalTime().ToString("M/d", System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(expected, _vm.Items.Single(i => i.Title == "old").ModifiedText);
    }

    [Fact]
    public void Query_filters_by_title_and_body()
    {
        _vm.Query = "りんご";
        Assert.Equal(["b"], Titles());
        Assert.Equal("1 / 3 件", _vm.CountText);

        _vm.Query = "A";
        Assert.Equal(["a"], Titles());   // 大文字小文字を無視（タイトル a・本文「# A memo」）

        _vm.Query = "zzz";
        Assert.Empty(_vm.Items);
        Assert.True(_vm.NoMatch);
        Assert.False(_vm.IsEmpty);

        _vm.Query = "";
        Assert.Equal(3, _vm.Items.Count);
    }

    [Fact]
    public void Empty_state_when_there_are_no_memos()
    {
        foreach (var t in new[] { "a", "b", "c" }) _index.Remove(t);
        Assert.True(_vm.IsEmpty);
        Assert.Equal("0 件", _vm.CountText);
    }

    [Fact]
    public async Task Saving_the_open_memo_moves_it_up_but_keeps_the_selection()
    {
        await _vm.OpenAsync("a");
        _editor.BeginEdit();
        _editor.SetDraft("# 新しい A");
        await _editor.SaveAsync();

        Assert.Equal("a", _vm.Items[0].Title);
        Assert.True(_vm.Items[0].IsSelected);
        Assert.Equal("新しい A", _vm.Items[0].Snippet);
    }

    // ── 開く ──

    [Fact]
    public async Task Open_switches_the_editor_to_that_memo()
    {
        await _vm.OpenAsync("a");
        Assert.Equal("a", _editor.Title);
        Assert.True(_vm.Items.Single(i => i.Title == "a").IsSelected);
        Assert.False(_vm.Items.Single(i => i.Title == "c").IsSelected);
    }

    [Fact]
    public async Task Open_of_the_open_memo_does_nothing()
    {
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        await _vm.OpenAsync("c");

        Assert.Equal(0, _ui.AskSaveCount);
        Assert.True(_editor.IsEditing);
        Assert.Equal("typing", _editor.Draft);
    }

    [Fact]
    public async Task Open_with_unsaved_changes_asks_and_cancel_keeps_everything()
    {
        _ui.SaveAnswer = SaveChoice.Cancel;
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        await _vm.OpenAsync("a");

        Assert.Equal("c", _editor.Title);
        Assert.True(_vm.Items.Single(i => i.Title == "c").IsSelected);
        Assert.Equal("typing", _editor.Draft);
    }

    [Fact]
    public async Task Open_while_a_dialog_is_up_does_nothing()
    {
        _editor.BeginEdit();
        _editor.SetDraft("typing");
        _ui.SaveAnswer = SaveChoice.Cancel;
        _ui.WhileAsking = () => _vm.OpenAsync("b").GetAwaiter().GetResult();

        await _editor.LeaveEditAsync();

        Assert.Equal("c", _editor.Title);
    }

    [Fact]
    public async Task Open_of_a_memo_that_vanished_removes_it_and_says_so()
    {
        File.Delete(_lib.PathOf("a"));

        await _vm.OpenAsync("a");

        Assert.Equal("c", _editor.Title);
        Assert.DoesNotContain("a", Titles());
        Assert.Contains("『a』が見つかりません", _vm.Notice);
    }

    // ── 追加 ──

    [Fact]
    public async Task Add_creates_and_opens_an_empty_memo_in_rename_state()
    {
        await _vm.AddAsync();

        Assert.Equal("無題のメモ", _editor.Title);
        Assert.True(_lib.Exists("無題のメモ"));
        Assert.Equal("無題のメモ", _vm.RenamingTitle);
        Assert.True(_vm.Items.Single(i => i.Title == "無題のメモ").IsRenaming);
        Assert.Equal("無題のメモ", _vm.RenameText);
    }

    [Fact]
    public async Task Add_clears_the_query_so_the_new_memo_is_visible()
    {
        _vm.Query = "りんご";
        await _vm.AddAsync();
        Assert.Equal("", _vm.Query);
        Assert.Contains("無題のメモ", Titles());
    }

    [Fact]
    public async Task Add_twice_gets_a_number()
    {
        await _vm.AddAsync();
        _vm.CancelRename();
        await _vm.AddAsync();
        Assert.Equal("無題のメモ (2)", _editor.Title);
    }

    [Fact]
    public async Task Commit_after_add_renames_and_enters_edit_mode()
    {
        await _vm.AddAsync();
        _vm.RenameText = "買い物";
        _vm.CommitRename();

        Assert.Equal("買い物", _editor.Title);
        Assert.True(_lib.Exists("買い物"));
        Assert.False(_lib.Exists("無題のメモ"));
        Assert.True(_editor.IsEditing);
        Assert.Null(_vm.RenamingTitle);
    }

    [Fact]
    public async Task Cancel_after_add_keeps_the_default_name_and_enters_edit_mode()
    {
        await _vm.AddAsync();
        _vm.RenameText = "捨てる名前";
        _vm.CancelRename();

        Assert.Equal("無題のメモ", _editor.Title);
        Assert.True(_editor.IsEditing);
        Assert.Null(_vm.RenamingTitle);
    }

    [Fact]
    public async Task Add_with_unsaved_changes_asks_first_and_cancel_creates_nothing()
    {
        _ui.SaveAnswer = SaveChoice.Cancel;
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        await _vm.AddAsync();

        Assert.False(_lib.Exists("無題のメモ"));
        Assert.Equal("c", _editor.Title);
        Assert.Null(_vm.RenamingTitle);
    }

    // ── 名前の変更 ──

    [Fact]
    public async Task Rename_replaces_forbidden_characters_and_says_so()
    {
        await _vm.OpenAsync("a");
        _vm.BeginRename("a");
        _vm.RenameText = "A/B:テスト?";
        _vm.CommitRename();

        Assert.Equal("A／B：テスト？", _editor.Title);
        Assert.True(_lib.Exists("A／B：テスト？"));
        Assert.Contains("『A／B：テスト？』", _vm.Notice);
    }

    [Fact]
    public void Rename_to_an_existing_name_gets_a_number()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "b";
        _vm.CommitRename();

        Assert.True(_lib.Exists("b (2)"));
        Assert.Equal("# B りんご", File.ReadAllText(_lib.PathOf("b")));
        Assert.Contains("『b (2)』", _vm.Notice);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("  a.md ")]
    [InlineData("   ")]
    public void Rename_to_the_same_or_blank_name_changes_nothing(string text)
    {
        _vm.BeginRename("a");
        _vm.RenameText = text;
        _vm.CommitRename();

        Assert.Equal(["c", "b", "a"], Titles());
        Assert.Null(_vm.Notice);
        Assert.Null(_vm.RenamingTitle);
    }

    [Fact]
    public void Commit_is_idempotent()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "z";
        _vm.CommitRename();
        _vm.CommitRename();

        Assert.Equal(["c", "b", "z"], Titles());
        Assert.Null(_vm.Notice);
    }

    [Fact]
    public async Task Rename_of_the_open_memo_keeps_the_unsaved_draft()
    {
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        _vm.BeginRename("c");
        _vm.RenameText = "c2";
        _vm.CommitRename();

        Assert.Equal("c2", _editor.Title);
        Assert.True(_editor.IsEditing);
        Assert.Equal("typing", _editor.Draft);
        Assert.Equal(0, _ui.AskSaveCount);
        Assert.Equal(SaveResult.Saved, await _editor.SaveAsync());
        Assert.Equal("typing", File.ReadAllText(_lib.PathOf("c2")));
    }

    [Fact]
    public void Rename_does_not_move_the_item()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "a2";
        _vm.CommitRename();
        Assert.Equal(["c", "b", "a2"], Titles());
    }

    [Fact]
    public void Rename_of_a_memo_that_vanished_removes_it_and_says_so()
    {
        File.Delete(_lib.PathOf("a"));
        _vm.BeginRename("a");
        _vm.RenameText = "z";
        _vm.CommitRename();

        Assert.DoesNotContain("a", Titles());
        Assert.False(_lib.Exists("z"));
        Assert.Contains("『a』が見つかりません", _vm.Notice);
    }

    [Fact]
    public async Task Open_while_renaming_commits_the_rename_first()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "a2";

        await _vm.OpenAsync("b");

        Assert.True(_lib.Exists("a2"));
        Assert.Equal("b", _editor.Title);
        Assert.Null(_vm.RenamingTitle);
    }

    [Fact]
    public void Begin_rename_of_another_commits_the_first()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "a2";
        _vm.BeginRename("b");

        Assert.True(_lib.Exists("a2"));
        Assert.Equal("b", _vm.RenamingTitle);
        Assert.Equal("b", _vm.RenameText);
    }

    [Fact]
    public void Rename_preview_shows_the_normalized_name_only_when_it_differs()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "x/y";
        Assert.Equal("→ 『x／y』で保存されます", _vm.RenamePreview);
        _vm.RenameText = "plain";
        Assert.Null(_vm.RenamePreview);
    }

    // ── 削除 ──

    [Fact]
    public async Task Delete_of_the_open_memo_asks_then_opens_the_next_one()
    {
        // 並びは c, b, a。c を消すと「次」の b
        await _vm.DeleteAsync("c");

        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Contains("『c』", _ui.LastConfirmDeleteMessage);
        Assert.False(_lib.Exists("c"));
        Assert.Equal("b", _editor.Title);
        Assert.Equal(["b", "a"], Titles());
    }

    [Fact]
    public async Task Delete_of_the_last_item_opens_the_previous_one()
    {
        await _vm.OpenAsync("a");
        await _vm.DeleteAsync("a");
        Assert.Equal("b", _editor.Title);
    }

    [Fact]
    public async Task Deleting_every_memo_leaves_no_memo()
    {
        foreach (var t in new[] { "c", "b", "a" }) await _vm.DeleteAsync(t);

        Assert.Null(_editor.Title);
        Assert.True(_vm.IsEmpty);
    }

    [Fact]
    public async Task Delete_picks_the_neighbor_in_the_filtered_list()
    {
        _vm.Query = "memo";   // c と a だけが見える（b は飛ばす）
        Assert.Equal(["c", "a"], Titles());

        await _vm.DeleteAsync("c");

        Assert.Equal("a", _editor.Title);
        Assert.Equal("memo", _vm.Query);   // 絞り込みは消さない
    }

    [Fact]
    public async Task Delete_cancel_keeps_the_memo_and_the_editing_state()
    {
        _ui.DeleteAnswer = false;
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        await _vm.DeleteAsync("c");

        Assert.True(_lib.Exists("c"));
        Assert.True(_editor.IsEditing);
        Assert.Equal("typing", _editor.Draft);
    }

    [Fact]
    public async Task Delete_message_warns_about_unsaved_changes_of_the_open_memo()
    {
        _editor.BeginEdit();
        _editor.SetDraft("typing");
        await _vm.DeleteAsync("c");
        Assert.Contains("保存していない変更も失われます", _ui.LastConfirmDeleteMessage);

        await _vm.DeleteAsync("a");
        Assert.DoesNotContain("保存していない変更", _ui.LastConfirmDeleteMessage);
    }

    [Fact]
    public async Task Delete_of_another_memo_leaves_the_open_memo_editing()
    {
        _editor.BeginEdit();
        _editor.SetDraft("typing");

        await _vm.DeleteAsync("a");

        Assert.False(_lib.Exists("a"));
        Assert.Equal("c", _editor.Title);
        Assert.True(_editor.IsEditing);
        Assert.Equal("typing", _editor.Draft);
    }

    [Fact]
    public async Task The_editor_is_marked_confirming_while_the_delete_dialog_is_up()
    {
        bool? during = null;
        _ui.WhileAsking = () => during = _editor.IsConfirming;

        await _vm.DeleteAsync("a");

        Assert.True(during);
        Assert.False(_editor.IsConfirming);
    }

    [Fact]
    public async Task Delete_of_a_memo_that_already_vanished_only_drops_it_from_the_list()
    {
        File.Delete(_lib.PathOf("a"));

        await _vm.DeleteAsync("a");

        Assert.Equal(0, _ui.ConfirmDeleteCount);
        Assert.DoesNotContain("a", Titles());
    }

    [Fact]
    public async Task Delete_does_nothing_when_the_target_vanishes_during_the_dialog()
    {
        _ui.WhileAsking = () => File.Delete(_lib.PathOf("a"));

        await _vm.DeleteAsync("a");

        Assert.DoesNotContain("a", Titles());
        Assert.Equal("c", _editor.Title);
        Assert.Null(_vm.Notice);
    }

    [Fact]
    public async Task Delete_of_the_renaming_item_cancels_the_rename()
    {
        _vm.BeginRename("a");
        _vm.RenameText = "a2";

        await _vm.DeleteAsync("a");

        Assert.Null(_vm.RenamingTitle);
        Assert.False(_lib.Exists("a2"));
        Assert.False(_lib.Exists("a"));
    }

    [Fact]
    public async Task Delete_failure_keeps_the_memo_and_says_why()
    {
        if (OperatingSystem.IsWindows()) return;
        File.SetUnixFileMode(_lib.Folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            await _vm.DeleteAsync("a");
        }
        finally
        {
            File.SetUnixFileMode(_lib.Folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        Assert.True(_lib.Exists("a"));
        Assert.Contains("『a』を削除できませんでした", _vm.Notice);
        Assert.Contains("a", Titles());
    }

    [Fact]
    public async Task Notice_is_cleared_by_the_next_list_operation_but_not_by_content_changes()
    {
        File.Delete(_lib.PathOf("a"));
        await _vm.OpenAsync("a");
        Assert.NotNull(_vm.Notice);

        _editor.BeginEdit();   // 右ペインの操作では消えない
        Assert.NotNull(_vm.Notice);

        await _vm.OpenAsync("b");
        Assert.Null(_vm.Notice);
    }
}
