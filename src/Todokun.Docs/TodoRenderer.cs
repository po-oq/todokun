using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers.Html;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Todokun.Docs;

/// <summary>
/// md → 完全な HTML 文書（要件 9 章）。Miharikun の <c>MarkdownRenderer</c> から、mermaid・色付け・目次を除いて縮めたもの。
/// タスクリスト（- [ ] / - [x]）は Markdig 標準でチェックボックスになる。各項目に md の行番号（<c>data-line</c>）を付け、
/// クリックはページの script が <c>toggle:&lt;行&gt;:&lt;0|1&gt;</c> としてアプリに知らせる（md の書き換えはアプリ側）。
/// </summary>
public static class TodoRenderer
{
    // 順序を変えない：UseAutoIdentifiers は UseAdvancedExtensions より前（後だと日本語見出しの id が既定のままになる）。
    // 単一の改行を改行にする拡張は、Markdig 1.4.0 では UseSoftlineBreakAsHardlineBreak（P0 で確認）。
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseAdvancedExtensions()
        .UseSoftlineBreakAsHardlineBreak()
        .Build();

    private static readonly Lazy<string> BaseCss = new(() => Resource("markdown-base.css"));
    private static readonly Lazy<string> LightCss = new(() => Resource("markdown-light.css"));
    private static readonly Lazy<string> DarkCss = new(() => Resource("markdown-dark.css"));

    // 幅が変わっても（区切り線のドラッグ）、見ていた所を保つ。
    private const string ScrollKeeperScript = """
        (function () {
          var anchor = null, anchorTop = 0, adjusting = false;
          function remember() {
            var els = document.querySelectorAll('h1,h2,h3,h4,h5,h6,p,li,pre,table,blockquote');
            for (var i = 0; i < els.length; i++) {
              var t = els[i].getBoundingClientRect().top;
              if (t >= 0) { anchor = els[i]; anchorTop = t; return; }
            }
          }
          window.addEventListener('scroll', function () { if (!adjusting) remember(); }, { passive: true });
          window.addEventListener('resize', function () {
            if (!anchor) return;
            adjusting = true;
            window.scrollBy(0, anchor.getBoundingClientRect().top - anchorTop);
            requestAnimationFrame(function () { adjusting = false; });
          });
          document.addEventListener('DOMContentLoaded', remember);
        })();
        """;

    /// <param name="baseFolder">md のあるフォルダ（相対パスの基準）。</param>
    /// <param name="isMac">Mod+E の判定（metaKey／ctrlKey）に使う。</param>
    public static string Render(string markdown, string baseFolder, bool isDark, bool isMac, string? title = null)
    {
        var doc = Markdown.Parse(markdown, Pipeline);
        NeutralizeHttpEquiv(doc);
        MarkTaskLines(doc);
        var body = EnabledCheckboxes(doc.ToHtml(Pipeline));

        // 描画ごとの nonce。ここで入れる script にだけ付く。md に書かれた script には付かないので CSP が止める。
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        var head = new StringBuilder();
        head.Append("<!doctype html>\n<html lang=\"ja\">\n<head>\n<meta charset=\"utf-8\">\n");
        head.Append($"<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'nonce-{nonce}'; object-src 'none'; frame-src 'none'; form-action 'none'\">\n");
        head.Append($"<base href=\"{WebUtility.HtmlEncode(FolderUri(baseFolder))}\">\n");
        head.Append($"<title>{WebUtility.HtmlEncode(title ?? "")}</title>\n");
        head.Append("<style>\n").Append(BaseCss.Value).Append(isDark ? DarkCss.Value : LightCss.Value).Append("</style>\n");

        // script は全部 <head> に置き、本文の後ろには何も置かない（md の閉じていない <script src=" が nonce を属性として読まないように）。
        head.Append($"<script nonce=\"{nonce}\">\n");
        head.Append("""
            document.addEventListener('click', function (e) {
              var a = e.target.closest('a[href^="#"]');
              if (!a) return;
              e.preventDefault();
              var el = document.getElementById(decodeURIComponent(a.getAttribute('href').slice(1)));
              if (el) el.scrollIntoView();
            });
            // チェックボックスのクリックはアプリに知らせる（md の該当行はアプリが書き換える）。見た目は先に変わる（失敗したらアプリが描き直す）。
            document.addEventListener('click', function (e) {
              var t = e.target;
              if (!t || !t.matches || !t.matches('li.task-list-item input[type="checkbox"]')) return;
              var li = t.closest('li.task-list-item');
              var line = li && li.getAttribute('data-line');
              if (line === null || typeof invokeCSharpAction !== 'function') { e.preventDefault(); return; }
              invokeCSharpAction('toggle:' + line + ':' + (t.checked ? '1' : '0'));
            }, true);

            """);
        head.Append(KeyForwarderScript.For(isMac)).Append('\n').Append(ScrollKeeperScript).Append("\n</script>\n");
        head.Append("</head>\n<body>\n");

        return head + body + "</body>\n</html>\n";
    }

    /// <summary>
    /// チェック項目（段落の先頭がチェックボックスの <c>&lt;li&gt;</c>）に、md の行番号（0 始まり）を <c>data-line</c> で付ける。
    /// 通常項目の下に入れ子になったチェックボックスを、親の通常項目の分と取り違えない。
    /// </summary>
    private static void MarkTaskLines(MarkdownDocument doc)
    {
        foreach (var item in doc.Descendants<ListItemBlock>())
        {
            if (item.FirstOrDefault() is ParagraphBlock { Inline.FirstChild: TaskList })
                item.GetAttributes().AddProperty("data-line", item.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Markdig は disabled を付けるが灰色で読みにくい。disabled を外して通常の色で出す（クリックは script が受ける）。</summary>
    private static string EnabledCheckboxes(string html) =>
        html.Replace(@"<input disabled=""disabled"" type=""checkbox""", @"<input type=""checkbox"" tabindex=""-1""", StringComparison.Ordinal);

    /// <summary>生の HTML の <c>http-equiv</c> を <c>data-http-equiv</c> にする（<c>&lt;meta http-equiv="refresh"&gt;</c> を効かなくする）。</summary>
    private static void NeutralizeHttpEquiv(MarkdownDocument doc)
    {
        foreach (var block in doc.Descendants<HtmlBlock>())
        {
            var lines = block.Lines.Lines;
            for (var i = 0; i < block.Lines.Count; i++)
            {
                var text = lines[i].ToString();
                if (HttpEquiv.IsMatch(text))
                    lines[i].Slice = new StringSlice(HttpEquiv.Replace(text, "data-http-equiv"));
            }
        }
        foreach (var inline in doc.Descendants<HtmlInline>())
        {
            if (HttpEquiv.IsMatch(inline.Tag))
                inline.Tag = HttpEquiv.Replace(inline.Tag, "data-http-equiv");
        }
    }

    private static readonly Regex HttpEquiv = new("http-equiv", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>フォルダの URL（末尾 / つき）。&lt;base&gt; に使う。</summary>
    public static string FolderUri(string folder)
    {
        var full = Path.GetFullPath(folder);
        if (!Path.EndsInDirectorySeparator(full))
            full += Path.DirectorySeparatorChar;
        return new Uri(full).AbsoluteUri;
    }

    /// <summary>ファイルの URL（一時 HTML を file:/// で開くときに使う）。</summary>
    public static string FileUri(string file) => new Uri(Path.GetFullPath(file)).AbsoluteUri;

    private static string Resource(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"Todokun.Docs.Assets.{name}")
            ?? throw new InvalidOperationException($"埋め込みリソース {name} が無い");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
