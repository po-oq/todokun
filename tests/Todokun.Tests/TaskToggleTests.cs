using Todokun.Core;

namespace Todokun.Tests;

public class TaskToggleTests
{
    [Fact]
    public void Checks_an_unchecked_item_and_changes_one_char_only()
    {
        var r = TaskToggle.Toggle("- [ ] a\n- [ ] b\n", 1, true);
        Assert.Equal(TaskToggleStatus.Toggled, r.Status);
        Assert.Equal("- [ ] a\n- [x] b\n", r.Text);
    }

    [Fact]
    public void Unchecks_a_checked_item()
    {
        var r = TaskToggle.Toggle("- [x] a", 0, false);
        Assert.Equal(TaskToggleStatus.Toggled, r.Status);
        Assert.Equal("- [ ] a", r.Text);
    }

    [Fact]
    public void Capital_X_counts_as_checked_and_becomes_a_space()
    {
        var r = TaskToggle.Toggle("- [X] a", 0, false);
        Assert.Equal("- [ ] a", r.Text);
    }

    [Fact]
    public void Crlf_text_keeps_every_other_byte()
    {
        var text = "# t\r\n\r\n- [ ] a\r\n  - [x] b\r\n- [ ] c";
        var r = TaskToggle.Toggle(text, 3, false);
        Assert.Equal(TaskToggleStatus.Toggled, r.Status);
        Assert.Equal("# t\r\n\r\n- [ ] a\r\n  - [ ] b\r\n- [ ] c", r.Text);
    }

    [Fact]
    public void Last_line_without_a_trailing_newline()
    {
        var r = TaskToggle.Toggle("- [ ] a\n- [ ] last", 1, true);
        Assert.Equal("- [ ] a\n- [x] last", r.Text);
    }

    [Theory]
    [InlineData("* [ ] a", "* [x] a")]
    [InlineData("+ [ ] a", "+ [x] a")]
    [InlineData("1. [ ] a", "1. [x] a")]
    [InlineData("2) [ ] a", "2) [x] a")]
    [InlineData("> - [ ] a", "> - [x] a")]
    [InlineData("    - [ ] a", "    - [x] a")]
    public void Other_list_markers_and_quotes(string line, string expected)
    {
        Assert.Equal(expected, TaskToggle.Toggle(line, 0, true).Text);
    }

    [Fact]
    public void Already_in_the_requested_state_writes_nothing()
    {
        var r = TaskToggle.Toggle("- [x] a", 0, true);
        Assert.Equal(TaskToggleStatus.AlreadyInState, r.Status);
        Assert.Equal("- [x] a", r.Text);
    }

    [Theory]
    [InlineData("plain text", 0)]
    [InlineData("- item without box", 0)]
    [InlineData("-[ ] no space", 0)]
    [InlineData("- [ ]no space after", 0)]
    [InlineData("- [?] other mark", 0)]
    [InlineData("", 0)]
    public void Non_task_lines_are_refused(string text, int line)
    {
        var r = TaskToggle.Toggle(text, line, true);
        Assert.Equal(TaskToggleStatus.NotATask, r.Status);
        Assert.Equal(text, r.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(99)]
    public void Line_out_of_range(int line)
    {
        var r = TaskToggle.Toggle("- [ ] a\n- [ ] b", line, true);
        Assert.Equal(TaskToggleStatus.LineOutOfRange, r.Status);
    }

    [Fact]
    public void Only_the_first_box_of_the_line_is_changed()
    {
        var r = TaskToggle.Toggle("- [ ] text with [ ] inside", 0, true);
        Assert.Equal("- [x] text with [ ] inside", r.Text);
    }
}
