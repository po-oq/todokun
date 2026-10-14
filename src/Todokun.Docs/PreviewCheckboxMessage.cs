using System.Text.RegularExpressions;

namespace Todokun.Docs;

/// <summary>ページから届くチェックボックスのクリックの知らせ：<c>toggle:&lt;行&gt;:&lt;0|1&gt;</c>（行は md の 0 始まり、末尾は押した後の状態）。</summary>
public static partial class PreviewCheckboxMessage
{
    [GeneratedRegex(@"^toggle:(?<line>\d{1,9}):(?<state>[01])$")]
    private static partial Regex Pattern();

    /// <summary>Windows は JSON の文字列の形（前後が <c>"</c>）で来ることがあるので、それも受ける。他は null。</summary>
    public static (int Line, bool Checked)? Parse(string? body)
    {
        if (string.IsNullOrEmpty(body)) return null;
        var text = body.Length >= 2 && body[0] == '"' && body[^1] == '"' ? body[1..^1] : body;
        var m = Pattern().Match(text);
        if (!m.Success) return null;
        return (int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture), m.Groups["state"].Value == "1");
    }
}
