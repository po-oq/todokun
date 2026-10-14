using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using Todokun.Docs;
using Todokun.Presentation;

namespace Todokun.Views;

/// <summary>
/// TODO のプレビュー（md → HTML → <see cref="NativeWebView"/>。計画 11.7）。Miharikun の MarkdownPreview から、
/// ドキュメント（ファイル・html）・目次・mermaid・Esc の受け口を除いて縮めたもの。リンクはここで振り分ける。
/// </summary>
public partial class TodoPreview : UserControl
{
    /// <summary>Windows の WebView2 の作業フォルダ（<c>cache/webview2</c>。ウィンドウより前に決める）。mac では使わない。</summary>
    public static string? WebView2UserDataFolder { get; set; }

    /// <summary>テスト用：true のとき WebView を作らず、案内の文字だけ出す（ヘッドレスは STA でなく、WebView2 が失敗するため）。</summary>
    public static bool WebViewDisabled { get; set; }

    private IPreviewHost? _vm;
    private NativeWebView? _web;
    private bool _initDone;
    private string? _pagePath;
    private bool _loadingPage;
    private int? _restoreScrollY;
    private int _version;   // 連続した描画で、古い結果を捨てるための番号

    public TodoPreview()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_vm is not null) _vm.PreviewChanged -= OnPreviewChanged;
        _vm = DataContext as IPreviewHost;
        if (_vm is not null)
        {
            _vm.PreviewChanged += OnPreviewChanged;
            OnPreviewChanged(_vm.CurrentPreview, false);
        }
    }

    private async void OnPreviewChanged(PreviewSource? source, bool reload)
    {
        try
        {
            await ShowAsync(source, reload);
        }
        catch (Exception ex)
        {
            // 非同期の例外はここで受けないと、見えないまま消える。ログには本文を書かない。
            _vm?.Log($"プレビューの表示で例外: {ex.GetType().Name}: {ex.Message}");
            ShowMessage($"プレビューを表示できませんでした：{ex.Message}");
        }
    }

    private void ShowMessage(string text)
    {
        MessageText.Text = text;
        MessageText.IsVisible = true;
        WebHost.IsVisible = false;
    }

    private async Task ShowAsync(PreviewSource? source, bool reload)
    {
        var version = ++_version;
        if (_vm?.PreviewUnavailableReason is { } reason)
        {
            ShowMessage($"プレビューを作れません：{reason}（編集・保存は使えます）");
            return;
        }
        switch (source)
        {
            case null:
                ShowMessage("");
                return;
            case PreviewSource.Message message:
                ShowMessage(message.Text);
                return;
        }

        if (!EnsureWebView()) return;
        var md = (PreviewSource.Markdown)source;

        string pagePath;
        try
        {
            var isDark = _vm?.IsDark ?? false;
            var previewDir = _vm!.PreviewDir;
            pagePath = await Task.Run(() =>
                PreviewFiles.Write(previewDir, md.CacheKey,
                    TodoRenderer.Render(md.Text, md.BaseFolder, isDark, OperatingSystem.IsMacOS(), md.Title)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _vm?.Log($"一時 HTML を書けない: {ex.Message}");
            ShowMessage($"プレビューを作れません：{ex.Message}（編集・保存は使えます）");
            return;
        }
        if (version != _version) return;

        MessageText.IsVisible = false;
        WebHost.IsVisible = true;

        var web = _web!;
        web.Background = BackgroundFor(_vm?.IsDark ?? false);
        var same = string.Equals(_pagePath, pagePath, StringComparison.OrdinalIgnoreCase);
        _pagePath = pagePath;
        if (same && reload)
        {
            // Refresh() ではスクロール位置が保たれない（mac の WKWebView）ので、前に取っておいて読み込み後に戻す。
            _restoreScrollY = PreviewNavigationPolicy.ParseScrollY(await web.InvokeScript("window.scrollY"));
            if (version != _version) return;
            _loadingPage = true;
            web.Refresh();
        }
        else
        {
            _restoreScrollY = null;   // 日を移るときは先頭から
            _loadingPage = true;
            web.Navigate(new Uri(TodoRenderer.FileUri(pagePath)));
        }
    }

    private static IBrush BackgroundFor(bool dark) => new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);

    // ── WebView の準備 ──

    private bool EnsureWebView()
    {
        if (WebViewDisabled)
        {
            ShowMessage("（テスト：WebView は作りません）");
            return false;
        }
        if (_initDone) return _web is not null;
        _initDone = true;

        try
        {
            var web = new NativeWebView { Background = BackgroundFor(false) };
            web.EnvironmentRequested += OnEnvironmentRequested;
            web.NavigationStarted += OnNavigationStarted;
            web.NavigationCompleted += OnNavigationCompleted;
            web.WebMessageReceived += OnWebMessageReceived;
            web.NewWindowRequested += OnNewWindowRequested;
            _web = web;
            WebHost.Children.Add(web);
            WebHost.IsVisible = true;
            MessageText.IsVisible = false;

            if (!IsEngineAvailable(web.AdapterInfo)) return FailWithRuntimeMissing();
            web.AdapterCreated += (_, _) =>
            {
                if (!IsEngineAvailable(web.AdapterInfo)) FailWithRuntimeMissing();
            };
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or PlatformNotSupportedException or NotSupportedException)
        {
            WebHost.Children.Clear();
            _web = null;
            _vm?.Log($"WebView を始められない: {ex.Message}");
            ShowMessage($"プレビューを始められませんでした：{ex.Message}（編集・保存は使えます）");
            _initDone = false;   // 次の描画でやり直す
            return false;
        }
    }

    /// <summary>Windows は WebView2 の Runtime が必要（無いと WebView1 に切り替えようとする）。作れたかでなく種類で見分ける。</summary>
    private static bool IsEngineAvailable(WebViewAdapterInfo? info) =>
        !OperatingSystem.IsWindows() || info is null || info.Type == WebViewAdapterType.WebView2;

    private bool FailWithRuntimeMissing()
    {
        _vm?.Log($"WebView2 Runtime が見つからない（{_web?.AdapterInfo?.Type.ToString() ?? "不明"}）");
        WebHost.Children.Clear();
        _web = null;
        ShowMessage("プレビューには「Microsoft Edge WebView2 Runtime」が必要ですが、このパソコンには入っていません。\n" +
                    (_vm?.RuntimeMissingNote ?? ""));
        return false;
    }

    private static void OnEnvironmentRequested(object? sender, WebViewEnvironmentRequestedEventArgs e)
    {
        if (e is WindowsWebView2EnvironmentRequestedEventArgs windows && WebView2UserDataFolder is { } folder)
            windows.UserDataFolder = folder;
    }

    // ── リンクの振り分け ──

    private void OnNavigationStarted(object? sender, WebViewNavigationStartingEventArgs e)
    {
        if (e.Request is not { } uri) return;
        var action = PreviewNavigationPolicy.Decide(uri, _pagePath, _loadingPage, isMarkdownPage: true);
        if (action == PreviewNavigationAction.Allow) return;

        e.Cancel = true;
        if (action == PreviewNavigationAction.Route)
            Dispatcher.UIThread.Post(() => Route(uri));
    }

    private void OnNavigationCompleted(object? sender, WebViewNavigationCompletedEventArgs e)
    {
        _loadingPage = false;
        if (_restoreScrollY is { } y)
        {
            _restoreScrollY = null;
            if (y > 0) _ = _web?.InvokeScript($"window.scrollTo(0,{y})");
        }
    }

    // ページからの知らせ（invokeCSharpAction）。受けるのは key:ArrowLeft・key:ArrowRight・key:BeginEdit と、
    // チェックボックスの toggle:<行>:<0|1> だけ。他は捨てる。
    private void OnWebMessageReceived(object? sender, WebMessageReceivedEventArgs e)
    {
        var key = PreviewKeyMessage.Parse(e.Body) switch
        {
            PreviewKey.ArrowLeft => (PageKey?)PageKey.ArrowLeft,
            PreviewKey.ArrowRight => PageKey.ArrowRight,
            PreviewKey.BeginEdit => PageKey.BeginEdit,
            _ => null,
        };
        if (key is { } k)
        {
            Dispatcher.UIThread.Post(() => _vm?.OnPageKey(k));
            return;
        }
        if (PreviewCheckboxMessage.Parse(e.Body) is { } box)
            Dispatcher.UIThread.Post(() => _vm?.OnCheckboxToggled(box.Line, box.Checked));
    }

    // target="_blank" や window.open：同じ振り分けにして、新しいウィンドウは作らない。
    private void OnNewWindowRequested(object? sender, WebViewNewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Request is { } uri) Dispatcher.UIThread.Post(() => Route(uri));
    }

    private void Route(Uri uri)
    {
        if (PreviewNavigationPolicy.IsExternal(uri))
            ShellOpen.Open(uri.AbsoluteUri);
        else if (uri.IsUnc)
            return;   // file://server/share/…（ネットワーク）は何もしない
        else if (uri.IsFile)
            _vm?.OpenLocalLink(uri.LocalPath);
    }
}
