using System.Text;

namespace Todokun.Core;

/// <summary>厳密な UTF-8 のテキストファイル（BOM は読むとき外す。書くときは LF・BOM なし・アトミック）。</summary>
public static class Utf8TextFile
{
    // 厳密な UTF-8。既定のデコーダーは不正なバイトを U+FFFD に置き換えてしまい、元に戻せない。
    private static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);

    /// <summary>ファイルが無ければ <see cref="DayContent.Missing"/>。読めない・UTF-8 でないなら <see cref="DayContent.Failed"/>。</summary>
    public static DayContent Read(string path)
    {
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
    public static void Write(string path, string text) =>
        AtomicFile.WriteAllText(path, text.Replace("\r\n", "\n"));
}
