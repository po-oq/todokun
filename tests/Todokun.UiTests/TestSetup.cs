using System.Runtime.CompilerServices;
using Todokun.Views;

namespace Todokun.UiTests;

internal static class TestSetup
{
    /// <summary>ヘッドレスのテストでは、本物の WebView を作らない（Windows の WebView2 は STA のスレッドが要るため）。</summary>
    [ModuleInitializer]
    internal static void Init() => TodoPreview.WebViewDisabled = true;
}
