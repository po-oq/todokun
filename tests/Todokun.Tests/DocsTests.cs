using System.Text.RegularExpressions;
using Todokun.Docs;

namespace Todokun.Tests;

public class TodoRendererTests
{
    private static string R(string md, bool dark = false, bool mac = false) =>
        TodoRenderer.Render(md, "/tmp/x", dark, mac, "t");

    private static string Body(string html) => html[(html.IndexOf("<body>", StringComparison.Ordinal) + 6)..];

    [Fact]
    public void Embedded_css_is_readable_and_theme_switches()
    {
        Assert.Contains("task-list-item", R("- [ ] a"));
        Assert.NotEqual(R("a", dark: false), R("a", dark: true));
    }

    [Fact]
    public void Nested_two_and_three_levels_render_as_nested_lists_with_checkboxes()
    {
        var html = Body(R("- [ ] a\n  - [x] b\n    - [ ] c\n"));
        Assert.Equal(3, Regex.Matches(html, @"<input type=""checkbox""").Count);
        Assert.Single(Regex.Matches(html, "checked=\"checked\""));
        Assert.Equal(3, Regex.Matches(html, "<ul").Count);
    }

    [Theory]
    [InlineData("- [ ] a", false)]
    [InlineData("- [x] a", true)]
    [InlineData("- [X] a", true)]
    public void Checkbox_states(string md, bool checkedExpected)
    {
        var html = Body(R(md));
        Assert.Contains("<input", html);
        Assert.Equal(checkedExpected, html.Contains("checked=\"checked\""));
        Assert.DoesNotContain("disabled", html);   // 読み取り専用は disabled ではなく click 取り消しで実現
    }

    [Fact]
    public void Checkbox_in_numbered_list_item()
    {
        var html = Body(R("1. [ ] a\n2. [x] b\n"));
        Assert.Equal(2, Regex.Matches(html, @"<input type=""checkbox""").Count);
    }

    [Fact]
    public void Checkbox_in_code_block_is_not_converted()
    {
        var html = Body(R("```\n- [x] a\n```\n"));
        Assert.DoesNotContain("<input", html);
        Assert.Contains("[x] a", html);
    }

    [Fact]
    public void Single_newline_becomes_br()
    {
        Assert.Contains("a<br", Body(R("a\nb\n")));
    }

    [Fact]
    public void No_space_after_bracket_is_not_a_checkbox()
    {
        Assert.DoesNotContain("<input", Body(R("-[ ] a\n-[x] b\n")));
    }

    [Fact]
    public void Http_equiv_is_neutralized()
    {
        var html = Body(R("<meta http-equiv=\"refresh\" content=\"0;url=https://example.com\">\n\ntext <meta HTTP-EQUIV=\"refresh\"> x"));
        Assert.DoesNotMatch(new Regex("(?<!data-)http-equiv", RegexOptions.IgnoreCase), html);
        Assert.Contains("data-http-equiv", html);
    }

    [Fact]
    public void Csp_blocks_scripts_without_the_page_nonce()
    {
        var html = R("<script>alert(1)</script>\n");
        var nonce = Regex.Match(html, @"script-src 'nonce-([^']+)'").Groups[1].Value;
        Assert.NotEmpty(nonce);
        // nonce が付くのは自分の <script> だけ（本文の <script> には付かない）
        var body = Body(html);
        Assert.DoesNotContain(nonce, body);
        Assert.Contains("<script>alert(1)</script>", body);   // 出力はされるが CSP で動かない
        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        foreach (Match m in Regex.Matches(head, "<script([^>]*)>"))
            Assert.Contains($"nonce=\"{nonce}\"", m.Groups[1].Value);
    }

    [Fact]
    public void Nonce_differs_per_render()
    {
        var a = Regex.Match(R("x"), @"nonce-([^']+)'").Groups[1].Value;
        var b = Regex.Match(R("x"), @"nonce-([^']+)'").Groups[1].Value;
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Nothing_is_placed_after_body()
    {
        var html = R("text");
        Assert.EndsWith("</body>\n</html>\n", html);
        Assert.DoesNotContain("<script", Body(html));
    }

    [Fact]
    public void No_mermaid_or_highlight_in_output()
    {
        var html = R("# t\n\n```csharp\nvar a = 1;\n```\n\n- [ ] a\n");
        Assert.DoesNotContain("mermaid", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("highlight", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Japanese_heading_gets_github_style_id_for_page_links()
    {
        Assert.Contains("id=\"今日のメモ\"", Body(R("## 今日のメモ\n")));
    }

    [Fact]
    public void Base_href_points_to_the_folder()
    {
        Assert.Contains("<base href=\"file:///tmp/x/\">", R("a"));
    }

    [Fact]
    public void Key_forwarder_script_is_embedded_with_platform_modifier()
    {
        var win = R("a", mac: false);
        var mac = R("a", mac: true);
        Assert.Contains("var IS_MAC = false;", win);
        Assert.Contains("var IS_MAC = true;", mac);
        Assert.Contains("key:BeginEdit", win);
        Assert.Contains("e.repeat", win);
        Assert.DoesNotContain("key:ToggleEdit", win);
    }

    [Fact]
    public void Task_items_carry_their_source_line_and_nothing_else_does()
    {
        var html = Body(R("# t\n\n- [ ] a\n  - [x] b\n- plain\n  - [ ] nested\n\n```\n- [ ] code\n```\n\n- [ ] last"));
        Assert.Equal(["2", "3", "5", "11"], Regex.Matches(html, @"<li class=""task-list-item"" data-line=""(\d+)""").Select(m => m.Groups[1].Value));
        Assert.Equal(4, Regex.Matches(html, "data-line=").Count);   // 通常項目・コードブロックには付かない
        Assert.Contains("<li>plain", html);
    }

    [Fact]
    public void Source_lines_are_the_same_for_crlf_text()
    {
        var html = Body(R("# t\r\n\r\n- [ ] a\r\n  - [x] b"));
        Assert.Equal(["2", "3"], Regex.Matches(html, @"data-line=""(\d+)""").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void Numbered_and_quoted_task_items_carry_the_line_too()
    {
        var html = Body(R("1. [ ] n\n\n> - [ ] q"));
        Assert.Equal(["0", "2"], Regex.Matches(html, @"data-line=""(\d+)""").Select(m => m.Groups[1].Value));
    }

    [Fact]
    public void Click_script_reports_the_checkbox_to_the_app_and_lets_it_flip()
    {
        var html = R("- [ ] a");
        Assert.Contains("invokeCSharpAction('toggle:'", html);
        Assert.Contains("closest('li.task-list-item')", html);
        Assert.DoesNotContain("disabled", html);
    }

    [Fact]
    public void Key_forwarder_sends_new_memo_and_find_memo_with_the_platform_modifier_and_not_for_repeat()
    {
        var mac = KeyForwarderScript.For(true);
        var win = KeyForwarderScript.For(false);
        foreach (var s in new[] { mac, win })
        {
            foreach (var message in new[] { "key:NewMemo", "key:FindMemo" })
            {
                var send = s.IndexOf(message, StringComparison.Ordinal);
                Assert.True(send > 0, message);
                // 送る前に preventDefault し、押し続けは送らない
                var before = s[..send];
                Assert.True(before.LastIndexOf("e.preventDefault()", StringComparison.Ordinal) > before.LastIndexOf("e.key ===", StringComparison.Ordinal));
                Assert.True(before.LastIndexOf("e.repeat", StringComparison.Ordinal) > before.LastIndexOf("e.key ===", StringComparison.Ordinal));
            }
        }
        Assert.Contains("var IS_MAC = true;", mac);
        Assert.Contains("var IS_MAC = false;", win);
    }

    [Fact]
    public void Key_forwarder_does_not_send_for_repeat_before_sending_begin_edit()
    {
        var s = KeyForwarderScript.For(false);
        Assert.True(s.IndexOf("e.repeat", StringComparison.Ordinal) < s.IndexOf("key:BeginEdit", StringComparison.Ordinal));
    }
}

public class PreviewKeyMessageTests
{
    [Theory]
    [InlineData("key:ArrowLeft", PreviewKey.ArrowLeft)]
    [InlineData("key:ArrowRight", PreviewKey.ArrowRight)]
    [InlineData("key:BeginEdit", PreviewKey.BeginEdit)]
    [InlineData("key:NewMemo", PreviewKey.NewMemo)]
    [InlineData("key:FindMemo", PreviewKey.FindMemo)]
    [InlineData("\"key:NewMemo\"", PreviewKey.NewMemo)]
    [InlineData("\"key:FindMemo\"", PreviewKey.FindMemo)]
    [InlineData("\"key:ArrowLeft\"", PreviewKey.ArrowLeft)]
    [InlineData("\"key:ArrowRight\"", PreviewKey.ArrowRight)]
    [InlineData("\"key:BeginEdit\"", PreviewKey.BeginEdit)]
    public void Accepts_the_five_messages(string body, PreviewKey expected) =>
        Assert.Equal(expected, PreviewKeyMessage.Parse(body));

    [Theory]
    [InlineData("key:ToggleEdit")]
    [InlineData("key:Escape")]
    [InlineData("key:ArrowUp")]
    [InlineData("key:")]
    [InlineData("hello")]
    [InlineData("\"hello\"")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("KEY:ArrowLeft")]
    [InlineData(" key:ArrowLeft")]
    public void Drops_everything_else(string? body) =>
        Assert.Null(PreviewKeyMessage.Parse(body));
}


public class PreviewCheckboxMessageTests
{
    [Theory]
    [InlineData("toggle:2:1", 2, true)]
    [InlineData("toggle:0:0", 0, false)]
    [InlineData("toggle:123:1", 123, true)]
    [InlineData("\"toggle:5:0\"", 5, false)]
    public void Accepts_line_and_state(string body, int line, bool isChecked)
    {
        var m = PreviewCheckboxMessage.Parse(body);
        Assert.NotNull(m);
        Assert.Equal(line, m.Value.Line);
        Assert.Equal(isChecked, m.Value.Checked);
    }

    [Theory]
    [InlineData("toggle:")]
    [InlineData("toggle:1")]
    [InlineData("toggle:a:1")]
    [InlineData("toggle:-1:1")]
    [InlineData("toggle:1:2")]
    [InlineData("toggle:1:1:1")]
    [InlineData("toggle:99999999999:1")]
    [InlineData("key:BeginEdit")]
    [InlineData(" toggle:1:1")]
    [InlineData("")]
    [InlineData(null)]
    public void Drops_everything_else(string? body) =>
        Assert.Null(PreviewCheckboxMessage.Parse(body));
}
