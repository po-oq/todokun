using System.Text;

namespace Todokun.Core;

/// <summary>日ファイルの読み書き（要件 6・12 章、計画 11.8）。</summary>
public sealed class DayStore(string root)
{
    // 厳密な UTF-8。既定のデコーダーは不正なバイトを U+FFFD に置き換えてしまい、元に戻せない。
    private static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);

    public string Root { get; } = root;

    public string PathOf(DateOnly day) => DayFile.PathOf(Root, day);

    public bool Exists(DateOnly day) => File.Exists(PathOf(day));

    public DayContent Read(DateOnly day)
    {
        var path = PathOf(day);
        try
        {
            if (!File.Exists(path)) return new DayContent.Missing();
            var bytes = File.ReadAllBytes(path);
            var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
            return new DayContent.Loaded(Strict.GetString(bytes, start, bytes.Length - start));
        }
        catch (DecoderFallbackException)
        {
            return new DayContent.Failed("UTF-8 として読めません");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new DayContent.Failed(ex.Message);
        }
    }

    /// <summary>LF に統一し、BOM なしの UTF-8 で、アトミックに書く。失敗は例外（IOException など）。</summary>
    public void Write(DateOnly day, string text) =>
        AtomicFile.WriteAllText(PathOf(day), Normalize(text));

    /// <summary>日ファイルを消す。無ければ何もしない。失敗は例外。</summary>
    public void Delete(DateOnly day)
    {
        var path = PathOf(day);
        if (File.Exists(path)) File.Delete(path);
    }

    public static string Normalize(string text) => text.Replace("\r\n", "\n");
}
