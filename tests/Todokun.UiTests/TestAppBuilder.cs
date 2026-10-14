using Avalonia;
using Avalonia.Headless;
using Todokun.UiTests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Todokun.UiTests;

/// <summary>画面を出さずに Avalonia の部品を動かす（Avalonia.Headless）。アプリ本体の <c>App</c>（色・スタイル）をそのまま使う。</summary>
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Todokun.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
