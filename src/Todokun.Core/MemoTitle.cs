using System.Text;

namespace Todokun.Core;

/// <summary>メモのタイトル（＝ファイル名）の正規化。入力は拒否せず、確定時に直す。冪等。</summary>
public static class MemoTitle
{
    public const int MaxLength = 100;

    /// <summary>ファイル名に使えない文字 → 全角。</summary>
    public static IReadOnlyDictionary<char, char> Replacements { get; } = new Dictionary<char, char>
    {
        ['\\'] = '＼', ['/'] = '／', [':'] = '：', ['*'] = '＊', ['?'] = '？',
        ['"'] = '”', ['<'] = '＜', ['>'] = '＞', ['|'] = '｜',
    };

    /// <summary>Windows の予約名（大文字小文字を問わない）。</summary>
    public static IReadOnlySet<string> ReservedNames { get; } = BuildReserved();

    private static HashSet<string> BuildReserved()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" };
        for (var i = 0; i <= 9; i++)
        {
            set.Add("COM" + i);
            set.Add("LPT" + i);
        }
        foreach (var d in "¹²³")
        {
            set.Add("COM" + d);
            set.Add("LPT" + d);
        }
        return set;
    }

    /// <summary>正規化後のタイトル。全部が消えたら空文字（呼び出し側が元のタイトルのまま／既定名にする）。</summary>
    public static string Normalize(string raw)
    {
        var s = raw.Normalize(NormalizationForm.FormC);

        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            if (Replacements.TryGetValue(c, out var full)) sb.Append(full);
            else if (c < ' ') sb.Append(' ');
            else sb.Append(c);
        }

        s = Strip(sb.ToString());
        if (s.Length == 0) return "";

        s = Cut(s);
        if (s.Length == 0) return "";

        // 予約名の直後に足した `_` で 100 文字を超えたら、もう一度切る（`CON.` + 長い拡張子など）。
        return Cut(AvoidReserved(s));
    }

    private static string Cut(string s) => Truncate(s, MaxLength);

    /// <summary><paramref name="max"/> 文字（UTF-16。サロゲートペアの途中では切らない）に切り、末尾を取り直す。</summary>
    public static string Truncate(string s, int max)
    {
        if (s.Length <= max) return s;
        var cut = max;
        if (cut > 0 && char.IsHighSurrogate(s[cut - 1])) cut--;
        return Strip(s[..cut]);
    }

    /// <summary>前後の空白・末尾の <c>.md</c>・先頭と末尾の <c>.</c> を、変わらなくなるまで取る。</summary>
    private static string Strip(string s)
    {
        while (true)
        {
            var before = s;
            s = s.Trim();
            if (s.EndsWith(".md", StringComparison.OrdinalIgnoreCase)) s = s[..^3];
            s = s.TrimStart('.').TrimEnd('.');
            if (s == before) return s;
        }
    }

    /// <summary>最初の <c>.</c> の前の部分（後ろの空白を除く）が予約名なら、その直後に <c>_</c> を足す。</summary>
    private static string AvoidReserved(string s)
    {
        var dot = s.IndexOf('.');
        var head = (dot < 0 ? s : s[..dot]).TrimEnd();
        return ReservedNames.Contains(head) ? s.Insert(head.Length, "_") : s;
    }
}
