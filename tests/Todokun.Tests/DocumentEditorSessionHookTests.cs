using Todokun.Core;
using Todokun.Presentation;

namespace Todokun.Tests;

/// <summary>計画 2-1：<c>DeleteWhenBlank</c>・<c>HasDocument</c>（派生が決める口）。既定値では何も変わらない。</summary>
public sealed class DocumentEditorSessionHookTests
{
    /// <summary>メモリ上に 1 つの文書を持つだけの小さな派生。</summary>
    private sealed class FakeSession : DocumentEditorSession
    {
        public string? Stored;                 // null＝ファイルが無い
        public bool DeleteWhenBlankValue = true;
        public bool HasDocumentValue = true;
        public int Writes;
        public int Deletes;

        public FakeSession(IUiServices ui) : base(ui, null) { }

        public override string HeaderText => "fake";
        public override PreviewSource Preview => new PreviewSource.Message("fake");

        protected override bool DeleteWhenBlank => DeleteWhenBlankValue;
        protected override bool HasDocument => HasDocumentValue;

        public void Show()
        {
            _generation++;
            ShowContent(StoreRead());
        }

        public void SetHasDocument(bool value)
        {
            HasDocumentValue = value;
            RefreshCanBeginEdit();
        }

        protected override DayContent StoreRead() => Stored is null ? new DayContent.Missing() : new DayContent.Loaded(Stored);
        protected override bool StoreExists() => Stored is not null;
        protected override void StoreWrite(string text) { Stored = text.Replace("\r\n", "\n"); Writes++; }
        protected override void StoreDelete() { Stored = null; Deletes++; }
        protected override string UnreadableMessage(string reason) => reason;
        protected override string AskSaveText => "save?";
        protected override string ConfirmDeleteText => "delete?";
    }

    private readonly FakeUiServices _ui = new();

    private FakeSession Make(string? stored)
    {
        var s = new FakeSession(_ui) { Stored = stored };
        s.Show();
        return s;
    }

    [Fact]
    public async Task Defaults_keep_the_blank_save_as_delete_with_confirmation()
    {
        var s = Make("x");
        s.BeginEdit();
        s.SetDraft("  \n");

        var r = await s.SaveAsync();

        Assert.Equal(SaveResult.Deleted, r);
        Assert.Equal(1, _ui.ConfirmDeleteCount);
        Assert.Null(s.Stored);
    }

    [Fact]
    public async Task Delete_when_blank_false_writes_an_empty_text_without_asking()
    {
        var s = Make("x");
        s.DeleteWhenBlankValue = false;
        s.BeginEdit();
        s.SetDraft("");

        var r = await s.SaveAsync();

        Assert.Equal(SaveResult.Saved, r);
        Assert.Equal(0, _ui.ConfirmDeleteCount);
        Assert.Equal("", s.Stored);
        Assert.Equal(0, s.Deletes);
        Assert.Equal("", Assert.IsType<DayContent.Loaded>(s.Content).Text);
    }

    [Fact]
    public async Task Delete_when_blank_false_creates_the_file_even_when_it_did_not_exist()
    {
        var s = Make(null);
        s.DeleteWhenBlankValue = false;
        s.BeginEdit();
        s.SetDraft("\n");

        var r = await s.SaveAsync();

        Assert.Equal(SaveResult.Saved, r);
        Assert.NotNull(s.Stored);
        Assert.IsType<DayContent.Loaded>(s.Content);
        Assert.Equal(0, _ui.ConfirmDeleteCount);
    }

    [Fact]
    public async Task No_document_cannot_begin_edit_and_says_why()
    {
        var s = Make(null);
        s.SetHasDocument(false);

        Assert.False(s.CanBeginEdit);
        Assert.False(s.BeginEditCommand.CanExecute(null));
        Assert.False(s.BeginEdit());
        Assert.Equal("メモがありません。＋で追加してください", s.Message);
        Assert.Equal(EditorMode.View, s.Mode);
        Assert.Equal(SaveResult.Cancelled, await s.SaveAsync());
    }

    [Fact]
    public void No_document_ignores_check_toggles()
    {
        var s = Make("- [ ] a\n");
        s.SetHasDocument(false);

        Assert.False(s.ToggleCheck(0, true));
        Assert.Equal("- [ ] a\n", s.Stored);
        Assert.Equal(0, s.Writes);
    }

    [Fact]
    public void Refresh_can_begin_edit_notifies_the_command()
    {
        var s = Make("x");
        var raised = 0;
        s.BeginEditCommand.CanExecuteChanged += (_, _) => raised++;

        s.SetHasDocument(false);
        s.SetHasDocument(true);

        Assert.True(raised >= 2);
        Assert.True(s.CanBeginEdit);
    }

    [Fact]
    public void Default_has_document_allows_editing()
    {
        var s = Make("x");
        Assert.True(s.CanBeginEdit);
        Assert.True(s.BeginEdit());
    }
}
