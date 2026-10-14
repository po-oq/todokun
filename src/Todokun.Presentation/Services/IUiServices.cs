namespace Todokun.Presentation;

/// <summary>未保存のまま離れるときの確認の答え（要件 8.4）。</summary>
public enum SaveChoice
{
    Save,
    Discard,
    Cancel,
}

/// <summary>ViewModel が使う、画面の仕組みへの口。ViewModel はウィンドウを知らない。</summary>
public interface IUiServices
{
    /// <summary>「保存しますか？［保存］［破棄］［キャンセル］」。</summary>
    Task<SaveChoice> AskSaveAsync();

    /// <summary>「この日のファイルを削除します。よろしいですか？［削除］［キャンセル］」。削除＝true。</summary>
    Task<bool> ConfirmDeleteAsync();

    /// <summary>URL・ファイル・フォルダを、既定のアプリで開く。</summary>
    void OpenWithDefaultApp(string target);

    /// <summary>ファイルを選んだ状態で、ファイラー（エクスプローラー・Finder）で開く。</summary>
    void RevealInFileManager(string file);
}
