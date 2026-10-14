using Avalonia;
using Todokun.Core;

namespace Todokun;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // 古い macOS が Finder から渡すことがある -psn_ の引数は取り除く
        args = [.. args.Where(a => !a.StartsWith("-psn_", StringComparison.Ordinal))];
        // 落ちる前の最後の例外をログに残す（本文は書かない。型・メッセージ・スタックだけ）
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write($"未処理の例外（終了={e.IsTerminating}）: {e.ExceptionObject}");
        TaskScheduler.UnobservedTaskException += (_, e) =>
            AppLog.Write($"観測されなかったタスクの例外: {e.Exception}");
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect();
}
