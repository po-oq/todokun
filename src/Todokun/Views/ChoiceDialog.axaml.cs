using Avalonia.Controls;
using Avalonia.Input;

namespace Todokun.Views;

/// <summary>
/// 選択肢のダイアログ（保存しますか？［保存］［破棄］［キャンセル］／削除の確認［削除］［キャンセル］）。
/// 最後のボタンが取り消し（Esc・×）。答えはボタンの番号。
/// </summary>
public partial class ChoiceDialog : Window
{
    public ChoiceDialog()
    {
        InitializeComponent();
    }

    private void Setup(string message, string[] buttons)
    {
        MessageText.Text = message;
        for (var i = 0; i < buttons.Length; i++)
        {
            var index = i;
            var b = new Button { Content = buttons[i], MinWidth = 80, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center };
            if (i == 0) b.IsDefault = true;
            if (i == buttons.Length - 1) b.IsCancel = true;
            b.Click += (_, _) => Close(index);
            Buttons.Children.Add(b);
        }
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(buttons.Length - 1); };
    }

    /// <summary>メインウィンドウとして出す案内（二重起動の「すでに起動しています」）。閉じるとアプリが終わる。</summary>
    public static ChoiceDialog CreateStandalone(string message, string button)
    {
        var dialog = new ChoiceDialog { WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowInTaskbar = true };
        dialog.Setup(message, [button]);
        return dialog;
    }

    /// <summary>ボタンの番号（0 始まり）。×・Esc は最後のボタン。owner が無ければ最後（取り消し）。</summary>
    public static async Task<int> ShowAsync(Window? owner, string message, params string[] buttons)
    {
        if (owner is null) return buttons.Length - 1;
        var dialog = new ChoiceDialog();
        dialog.Setup(message, buttons);
        return await dialog.ShowDialog<int?>(owner) ?? buttons.Length - 1;
    }
}
