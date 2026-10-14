using System.Runtime.InteropServices;

namespace Poc;

/// <summary>
/// mac 専用：このプロセス内から NSEvent を AppKit のイベント経路（[NSApp sendEvent:]）に流す。
/// OS 全体への注入（CGEventPost）はアクセシビリティ権限が要るので使えない。ここは HID 層は通らないが、
/// ウィンドウ → ファーストレスポンダー（WKWebView）→ nextResponder（Avalonia の NSView）の経路は本物。
/// </summary>
public static class MacEvents
{
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr objc_getClass(string name);
    [DllImport("/usr/lib/libobjc.A.dylib")] static extern IntPtr sel_registerName(string name);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr Msg(IntPtr r, IntPtr s);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr MsgP(IntPtr r, IntPtr s, IntPtr a);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr MsgStr(IntPtr r, IntPtr s, string a);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern IntPtr MsgB(IntPtr r, IntPtr s, bool a);
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] static extern long MsgLong(IntPtr r, IntPtr s);

    [StructLayout(LayoutKind.Sequential)] struct NSPoint { public double X, Y; }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr KeyEvt(IntPtr cls, IntPtr sel, long type, NSPoint loc, ulong flags, double ts, long winNum, IntPtr ctx,
        IntPtr chars, IntPtr charsIgnoring, byte isRepeat, ushort keyCode);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    static extern IntPtr MouseEvt(IntPtr cls, IntPtr sel, long type, NSPoint loc, ulong flags, double ts, long winNum, IntPtr ctx,
        long eventNumber, long clickCount, float pressure);

    static IntPtr S(string s) => sel_registerName(s);
    static IntPtr NSStr(string s) => MsgStr(objc_getClass("NSString"), S("stringWithUTF8String:"), s);

    public const ulong Shift = 1UL << 17, Control = 1UL << 18, Option = 1UL << 19, Command = 1UL << 20;
    // 仮想キーコード
    public const ushort KeyLeft = 123, KeyRight = 124, KeyE = 14, KeyEnter = 36, KeyTab = 48, KeyEsc = 53;

    static double Ts() => Environment.TickCount64 / 1000.0;

    public static void Key(IntPtr nsWindow, ushort code, string chars, ulong flags, bool down, bool isRepeat = false)
    {
        var winNum = MsgLong(nsWindow, S("windowNumber"));
        var ev = KeyEvt(objc_getClass("NSEvent"), S("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            down ? 10 : 11, new NSPoint(), flags, Ts(), winNum, IntPtr.Zero, NSStr(chars), NSStr(chars), (byte)(isRepeat ? 1 : 0), code);
        if (ev == IntPtr.Zero) throw new InvalidOperationException("NSEvent を作れない");
        MsgP(MsgApp(), S("sendEvent:"), ev);
    }

    public static IntPtr MsgApp() => Msg(objc_getClass("NSApplication"), S("sharedApplication"));

    /// <summary>ウィンドウ座標（左上原点・pt）に対するマウスの 1 イベント。type: 1=down 2=up 6=dragged</summary>
    public static void Mouse(IntPtr nsWindow, long type, double x, double yFromTop, double contentHeight)
    {
        var winNum = MsgLong(nsWindow, S("windowNumber"));
        var ev = MouseEvt(objc_getClass("NSEvent"), S("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            type, new NSPoint { X = x, Y = contentHeight - yFromTop }, 0, Ts(), winNum, IntPtr.Zero, 0, 1, type == 2 ? 0f : 1f);
        MsgP(MsgApp(), S("sendEvent:"), ev);
    }

    public static string? FirstResponderClass(IntPtr nsWindow)
    {
        var fr = Msg(nsWindow, S("firstResponder"));
        if (fr == IntPtr.Zero) return null;
        var cls = Msg(fr, S("class"));
        var name = Msg(cls, S("description"));
        var utf8 = Msg(name, S("UTF8String"));
        return Marshal.PtrToStringUTF8(utf8);
    }

    [StructLayout(LayoutKind.Sequential)] struct CGRect { public double X, Y, W, H; }
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] static extern IntPtr CGWindowListCreateImage(CGRect r, uint option, uint windowId, uint imageOption);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern IntPtr CFURLCreateFromFileSystemRepresentation(IntPtr a, byte[] buf, long len, bool isDir);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern IntPtr CFStringCreateWithCString(IntPtr a, string s, uint enc);
    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] static extern IntPtr CGImageDestinationCreateWithURL(IntPtr url, IntPtr type, long count, IntPtr opts);
    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] static extern void CGImageDestinationAddImage(IntPtr d, IntPtr img, IntPtr props);
    [DllImport("/System/Library/Frameworks/ImageIO.framework/ImageIO")] static extern bool CGImageDestinationFinalize(IntPtr d);

    /// <summary>自分のウィンドウを PNG に保存（自プロセスのウィンドウなので画面収録の権限は要らない）。</summary>
    public static bool Shot(IntPtr nsWindow, string path)
    {
        var num = (uint)MsgLong(nsWindow, S("windowNumber"));
        var nul = new CGRect { X = double.PositiveInfinity, Y = double.PositiveInfinity, W = 0, H = 0 };
        var img = CGWindowListCreateImage(nul, 1 << 3, num, 1);
        if (img == IntPtr.Zero) return false;
        var bytes = System.Text.Encoding.UTF8.GetBytes(path);
        var url = CFURLCreateFromFileSystemRepresentation(IntPtr.Zero, bytes, bytes.Length, false);
        var dest = CGImageDestinationCreateWithURL(url, CFStringCreateWithCString(IntPtr.Zero, "public.png", 0x08000100), 1, IntPtr.Zero);
        CGImageDestinationAddImage(dest, img, IntPtr.Zero);
        return CGImageDestinationFinalize(dest);
    }

    // ── 入力ソース（IME）の切り替え。テスト後は必ず元に戻す ──
    const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";
    [DllImport(Carbon)] static extern IntPtr TISCopyCurrentKeyboardInputSource();
    [DllImport(Carbon)] static extern IntPtr TISCreateInputSourceList(IntPtr props, bool includeAllInstalled);
    [DllImport(Carbon)] static extern IntPtr TISGetInputSourceProperty(IntPtr src, IntPtr key);
    [DllImport(Carbon)] static extern int TISSelectInputSource(IntPtr src);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern long CFArrayGetCount(IntPtr a);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern IntPtr CFArrayGetValueAtIndex(IntPtr a, long i);
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")] static extern bool CFStringGetCString(IntPtr s, byte[] buf, long size, uint enc);

    static IntPtr TisIdKey()
    {
        var h = NativeLibrary.Load(Carbon);
        return Marshal.ReadIntPtr(NativeLibrary.GetExport(h, "kTISPropertyInputSourceID"));
    }

    static string CfStr(IntPtr s)
    {
        if (s == IntPtr.Zero) return "";
        var buf = new byte[512];
        return CFStringGetCString(s, buf, buf.Length, 0x08000100) ? System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0') : "";
    }

    public static string CurrentInputSourceId() => CfStr(TISGetInputSourceProperty(TISCopyCurrentKeyboardInputSource(), TisIdKey()));

    public static List<(string Id, IntPtr Ref)> InputSources()
    {
        var list = new List<(string, IntPtr)>();
        var arr = TISCreateInputSourceList(IntPtr.Zero, false);
        var n = CFArrayGetCount(arr);
        for (long i = 0; i < n; i++)
        {
            var src = CFArrayGetValueAtIndex(arr, i);
            list.Add((CfStr(TISGetInputSourceProperty(src, TisIdKey())), src));
        }
        return list;
    }

    public static bool Select(string id)
    {
        foreach (var (sid, src) in InputSources())
            if (sid == id) return TISSelectInputSource(src) == 0;
        return false;
    }
}
