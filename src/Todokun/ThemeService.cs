using Avalonia;
using Avalonia.Styling;

namespace Todokun;

/// <summary>
/// OS のライト/ダークに追従する（要件 8.1）。常に <c>ThemeVariant.Default</c>。色は <c>App.axaml</c> の
/// <c>ThemeDictionaries</c> から読まれる。Miharikun の ThemeService から設定の保存を除いたもの。
/// </summary>
public sealed class ThemeService
{
    public bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>見た目が変わった。引数は変更後の IsDark。UI スレッドで呼ばれる。</summary>
    public event Action<bool>? Changed;

    public void Start()
    {
        var app = Application.Current!;
        app.RequestedThemeVariant = ThemeVariant.Default;
        app.ActualThemeVariantChanged += (_, _) => Changed?.Invoke(IsDark);
    }
}
