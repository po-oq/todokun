using Avalonia.Controls;
using Todokun.Presentation;
using Todokun.Views;

namespace Todokun.Services;

/// <summary>Avalonia での <see cref="IUiServices"/>。確認はメインウィンドウの上に出す。</summary>
public sealed class AvaloniaUiServices(Func<Window?> owner) : IUiServices
{
    public async Task<SaveChoice> AskSaveAsync()
    {
        var i = await ChoiceDialog.ShowAsync(owner(), "この日のメモが保存されていません。保存しますか？", "保存", "破棄", "キャンセル");
        return i switch { 0 => SaveChoice.Save, 1 => SaveChoice.Discard, _ => SaveChoice.Cancel };
    }

    public async Task<bool> ConfirmDeleteAsync() =>
        await ChoiceDialog.ShowAsync(owner(), "この日のファイルを削除します。よろしいですか？", "削除", "キャンセル") == 0;

    public void OpenWithDefaultApp(string target) => ShellOpen.Open(target);

    public void RevealInFileManager(string file) => ShellOpen.Reveal(file);
}
