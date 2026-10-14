namespace Todokun.Docs;

/// <summary>
/// ページの <c>keydown</c> を C# に知らせるスクリプト（本文。<c>&lt;script&gt;</c> なし。要件 11 章、計画 11.6）。
/// ←→（修飾キーなし）→ <c>key:ArrowLeft</c>／<c>key:ArrowRight</c>、Mod+E → <c>key:BeginEdit</c>。
/// Mod+E は押し続け（<c>e.repeat</c>）のとき送らない。<c>defaultPrevented</c> のときは何も送らない。
/// </summary>
public static class KeyForwarderScript
{
    private const string Template = """
        (function () {
          if (window.__todokunKeys) return;
          window.__todokunKeys = true;
          var IS_MAC = __ISMAC__;
          document.addEventListener('keydown', function (e) {
            if (e.defaultPrevented || typeof invokeCSharpAction !== 'function') return;
            var plain = !e.ctrlKey && !e.metaKey && !e.altKey && !e.shiftKey;
            if (plain && e.key === 'ArrowLeft') { e.preventDefault(); invokeCSharpAction('key:ArrowLeft'); return; }
            if (plain && e.key === 'ArrowRight') { e.preventDefault(); invokeCSharpAction('key:ArrowRight'); return; }
            var mod = IS_MAC ? (e.metaKey && !e.ctrlKey) : (e.ctrlKey && !e.metaKey);
            if (mod && !e.altKey && !e.shiftKey && (e.key === 'e' || e.key === 'E')) {
              e.preventDefault();
              if (e.repeat) return;
              invokeCSharpAction('key:BeginEdit');
            }
          });
        })();
        """;

    public static string For(bool isMac) => Template.Replace("__ISMAC__", isMac ? "true" : "false");
}
