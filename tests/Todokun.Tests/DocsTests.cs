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
    [InlineData("\"key:ArrowLeft\"", PreviewKey.ArrowLeft)]
    [InlineData("\"key:ArrowRight\"", PreviewKey.ArrowRight)]
    [InlineData("\"key:BeginEdit\"", PreviewKey.BeginEdit)]
    public void Accepts_the_three_messages(string body, PreviewKey expected) =>
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
