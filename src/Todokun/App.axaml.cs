using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Todokun.Core;
using Todokun.Presentation;
using Todokun.Services;
using Todokun.Views;

namespace Todokun;

public partial class App : Application
{
    public ThemeService Theme { get; } = new();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var options = LaunchOptions.Parse(desktop.Args ?? [], Environment.GetEnvironmentVariable);
            var today = DateOnly.FromDateTime(DateTime.Now);
            // 起動の順序（計画 11.1）は AppStartup。画面はその結果を受けて組み立てる
            var start = AppStartup.Run(options, AppPaths.ExeDirOf(Environment.ProcessPath, AppContext.BaseDirectory), today);

            if (start.AlreadyRunning)
            {
                // 案内を出して終わる（--data-dir が違えば別のデータルートなので起動できる）
                desktop.MainWindow = ChoiceDialog.CreateStandalone(
                    $"todokun はすでに起動しています。\n（データの場所：{start.Paths.DataRoot}）", "OK");
                return;
            }

            desktop.Exit += (_, _) => start.Instance.Dispose();
            TodoPreview.WebView2UserDataFolder = Path.Combine(start.Paths.CacheRoot, "webview2");
            var previewDir = Path.Combine(start.Paths.CacheRoot, "preview");
            if (start.CacheWritable) _ = Task.Run(() => PreviewFiles.CleanOld(previewDir));   // 1 日より古いものだけ

            Theme.Start();
            var ui = new AvaloniaUiServices(() => desktop.MainWindow);
            var vm = new MainViewModel(start.Index, start.Store, today, start.Paths.DataRoot, start.Paths.IsCustomDataRoot, ui,
                previewDir: previewDir,
                isDark: () => Theme.IsDark,
                dataWritable: () => (start.DataWritable, start.DataReason),
                previewUnavailableReason: start.CacheWritable ? null : start.CacheReason,
                startupNotice: start.Notice);
            Theme.Changed += _ => vm.OnThemeChanged();
            desktop.MainWindow = new MainWindow { DataContext = vm };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
