using System.Reflection;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Markdig;

namespace Poc;

public class MainWindow : Window
{
    // ── 状態 ──
    private readonly string _dataDir;
    private readonly string _cacheDir;
    private readonly List<DateOnly> _days = [];
    private DateOnly _selected;
    private DateOnly _month;
    private bool _editMode;
    private bool _isMac = OperatingSystem.IsMacOS();
    private readonly bool _noPrevent = Program.Args.Contains("--no-prevent");
    private readonly bool _noHandleLR = Program.Args.Contains("--tunnel-passive"); // Tunnel で Handled にしない（二重到達を観測するため。既定もしない）

    // ── 部品 ──
    private readonly Grid _monthGrid = new() ;
    private readonly ListBox _list = new();
    private readonly TextBlock _monthLabel = new();
    private readonly TextBlock _status = new() { FontFamily = new FontFamily("Menlo, Consolas, monospace"), FontSize = 11, TextWrapping = TextWrapping.Wrap };
    private readonly Grid _host = new();
    private readonly TextBox _edit = new() { AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Menlo, Consolas, monospace"), IsVisible = false };
    private NativeWebView? _web;
    private readonly TextBlock _message = new() { IsVisible = false, TextWrapping = TextWrapping.Wrap, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private Grid _root = new();
    private ColumnDefinition _leftCol = new(300, GridUnitType.Pixel);

    // ── 計測 ──
    private readonly Dictionary<string, int> _count = new();
    private readonly Dictionary<string, double> _lastAt = new();
    private readonly List<double> _winRepeat = new();   // ウィンドウ経路の同じキーの間隔（<1s）
    private readonly List<double> _pageRepeat = new();  // ページ経路（performance.now の差。repeat=true のとき）
    private double _lastPagePt = -1;
    private int _renderGen;
    private string? _pagePath;
    private bool _loading;
    private int? _restoreY;

    public MainWindow()
    {
        Title = "todokun PoC (P0)";
        Width = 1000; Height = 700;
        _dataDir = Path.GetFullPath(Program.Opt("--data-dir") ?? Path.Combine(Path.GetTempPath(), "todokun-poc-data"));
        _cacheDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Program.Opt("--cache-dir") ?? Path.Combine(_dataDir, "..", "todokun-poc-cache", "x")))!);
        Log.Init(Program.Opt("--log") ?? Path.Combine(_dataDir, "..", "todokun-poc-log.jsonl"));
        Log.Write("start", new { os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, _dataDir, _cacheDir, processPath = Environment.ProcessPath, noPrevent = _noPrevent });

        SeedData();
        _selected = _days.Max();
        _month = new DateOnly(_selected.Year, _selected.Month, 1);

        Content = BuildUi();
        BuildCalendar();
        RefreshList();

        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyUpEvent, (_, e) => { if (e.Source is not TextBox) { } }, RoutingStrategies.Tunnel);
        Opened += async (_, _) => await OnOpened();
        ActualThemeVariantChanged += async (_, _) => { Log.Write("themeChanged", new { v = ActualThemeVariant.ToString() }); await RenderDay(reload: true); };
    }

    // ───────────────────────────── データ ─────────────────────────────
    private void SeedData()
    {
        Directory.CreateDirectory(_dataDir);
        // 本物のデータではなく PoC 専用。tmp 配下の既定、または --data-dir
        string[] days = ["2026-09-28", "2026-09-30", "2026-10-02", "2026-10-09", "2026-10-12", "2026-10-13", "2026-10-14"];
        foreach (var d in days)
        {
            var date = DateOnly.ParseExact(d, "yyyy-MM-dd");
            var path = Path.Combine(_dataDir, date.Year.ToString("0000"), date.Month.ToString("00"), d + ".md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            if (!File.Exists(path)) File.WriteAllText(path, Sample(d));
            _days.Add(date);
        }
        _days.Sort();
    }

    private static string Sample(string d)
    {
        var sb = new StringBuilder();
        sb.Append($"# {d}\n\n");
        sb.Append("- [ ] 親タスク A\n  - [x] 子 A-1\n  - [ ] 子 A-2\n    - [ ] 孫 A-2-1\n    - [x] 孫 A-2-2\n      - [ ] ひ孫 A-2-2-1\n- [x] 親タスク B（完了）\n  - [x] 子 B-1\n- 通常の項目（チェックなし）\n  - [ ] 通常の下のチェック\n  - 通常の下の通常\n- [ ] 日本語の項目 ☐ 位置揃えの確認\n\n");
        sb.Append("1. 番号付き\n   - [ ] 番号付きの中のチェック\n   - [x] 完了\n\n");
        sb.Append("```\n- [x] コードブロック内は変換しない\n```\n\n");
        sb.Append("単一の改行\nは改行として出る\n\n");
        for (var i = 0; i < 60; i++) sb.Append($"- スクロール用の行 {i}\n");
        return sb.ToString();
    }

    private string DayPath(DateOnly d) => Path.Combine(_dataDir, d.Year.ToString("0000"), d.Month.ToString("00"), d.ToString("yyyy-MM-dd") + ".md");

    // ───────────────────────────── 画面 ─────────────────────────────
    private Control BuildUi()
    {
        _root = new Grid();
        _root.ColumnDefinitions.Add(_leftCol);
        _leftCol.MinWidth = 252; // カレンダーが収まる幅
        _root.ColumnDefinitions.Add(new ColumnDefinition(6, GridUnitType.Pixel));
        _root.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        // 左：カレンダー（固定）＋一覧
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        var nav = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(4) };
        var prev = new Button { Content = "◀" }; prev.Click += (_, _) => { _month = _month.AddMonths(-1); BuildCalendar(); RefreshList(); };
        var next = new Button { Content = "▶" }; next.Click += (_, _) => { _month = _month.AddMonths(1); BuildCalendar(); RefreshList(); };
        _monthLabel.HorizontalAlignment = HorizontalAlignment.Center; _monthLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_monthLabel, 1); Grid.SetColumn(next, 2);
        nav.Children.Add(prev); nav.Children.Add(_monthLabel); nav.Children.Add(next);
        _monthGrid.Margin = new Thickness(4);
        _monthGrid.Height = 6 * 36 + 24;
        for (var i = 0; i < 7; i++) _monthGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
        for (var i = 0; i < 7; i++) _monthGrid.RowDefinitions.Add(new RowDefinition(i == 0 ? 24 : 36, GridUnitType.Pixel));
        _list.SelectionChanged += (_, _) =>
        {
            if (_list.SelectedItem is ListBoxItem { Tag: DateOnly d } && d != _selected) _ = OpenDay(d, "list");
        };
        Grid.SetRow(_monthGrid, 1); Grid.SetRow(_list, 2);
        left.Children.Add(nav); left.Children.Add(_monthGrid); left.Children.Add(_list);

        var split = new GridSplitter { Width = 6, Background = Brushes.Gray, ResizeDirection = GridResizeDirection.Columns };
        Grid.SetColumn(split, 1);

        // 右：バー＋(WebView | TextBox)
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var bar = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(6) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var bEdit = new Button { Content = "✎ 編集/ビュー" }; bEdit.Click += (_, _) => Toggle("button");
        var bTheme = new Button { Content = "ライト/ダーク" }; bTheme.Click += (_, _) => RequestedThemeVariant = ActualThemeVariant == ThemeVariant.Dark ? ThemeVariant.Light : ThemeVariant.Dark;
        var bReport = new Button { Content = "カウンタ記録" };
        bReport.Click += (_, _) => Log.Write("counters", new { counts = _count, winRepeat = Stats(_winRepeat), pageRepeat = Stats(_pageRepeat) });
        var bReset = new Button { Content = "リセット" }; bReset.Click += (_, _) => { _count.Clear(); _lastAt.Clear(); UpdateStatus(); };
        buttons.Children.Add(bEdit); buttons.Children.Add(bTheme); buttons.Children.Add(bReport); buttons.Children.Add(bReset);
        bar.Children.Add(buttons); bar.Children.Add(_status);
        Grid.SetRow(bar, 0);
        _host.Children.Add(_message);
        _host.Children.Add(_edit);
        Grid.SetRow(_host, 1);
        right.Children.Add(bar); right.Children.Add(_host);
        Grid.SetColumn(right, 2);

        _edit.AddHandler(KeyDownEvent, OnEditKeyDownTunnel, RoutingStrategies.Tunnel);
        _edit.AddHandler(KeyDownEvent, OnEditKeyDownBubble, RoutingStrategies.Bubble, handledEventsToo: true);
        _edit.AddHandler(KeyUpEvent, (_, e) => Log.Write("edit.keyup", new { key = e.Key.ToString(), mods = e.KeyModifiers.ToString() }), RoutingStrategies.Tunnel);
        _edit.AddHandler(TextInputEvent, (_, e) => Log.Write("edit.textinput", new { text = e.Text }), RoutingStrategies.Tunnel);
        _edit.TextChanged += (_, _) => Log.Write("edit.textchanged", new { len = _edit.Text?.Length, hasCR = _edit.Text?.Contains('\r') });
        _edit.Text = "- [ ] あ\n  - [ ] い\nふつう";

        _root.Children.Add(left); _root.Children.Add(split); _root.Children.Add(right);
        return _root;
    }

    private static readonly string[] Wd = ["日", "月", "火", "水", "木", "金", "土"];

    private void BuildCalendar()
    {
        // 月グリッド（自前の 7×6）。前後月は IsEnabled=false、ファイルがある日は点、今日・選択は丸
        for (var i = _monthGrid.Children.Count - 1; i >= 0; i--) _monthGrid.Children.RemoveAt(i);
        _monthLabel.Text = $"{_month.Year}年 {_month.Month}月";
        for (var c = 0; c < 7; c++)
        {
            var h = new TextBlock { Text = Wd[c], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (c == 0) h.Foreground = Brushes.IndianRed; else if (c == 6) h.Foreground = Brushes.SteelBlue;
            Grid.SetColumn(h, c); _monthGrid.Children.Add(h);
        }
        var first = _month;
        var start = first.AddDays(-(int)first.DayOfWeek);
        var today = DateOnly.FromDateTime(DateTime.Now);
        for (var i = 0; i < 42; i++)
        {
            var d = start.AddDays(i);
            var inMonth = d.Month == _month.Month;
            var has = _days.Contains(d);
            var isSel = d == _selected && inMonth;
            var isToday = d == today;
            var num = new TextBlock { Text = d.Day.ToString(), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = inMonth ? 1 : 0.35 };
            if (isSel) num.Foreground = Brushes.White;
            else if (d.DayOfWeek == DayOfWeek.Sunday) num.Foreground = Brushes.IndianRed;
            else if (d.DayOfWeek == DayOfWeek.Saturday) num.Foreground = Brushes.SteelBlue;
            var circle = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = num };
            if (isSel) circle.Background = Brushes.SteelBlue;
            if (isToday) { circle.BorderBrush = Brushes.Orange; circle.BorderThickness = new Thickness(2); }
            var cell = new Grid();
            cell.Children.Add(circle);
            if (has && inMonth)
                cell.Children.Add(new Border { Width = 4, Height = 4, CornerRadius = new CornerRadius(2), Background = Brushes.Gray, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center });
            Control host;
            if (inMonth)
            {
                // その月の日だけ Button（押せる）。前後月は Button にしない（＝押せない・フォーカスも来ない）
                var btn = new Button { Content = cell, Background = Brushes.Transparent, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center, Tag = d };
                btn.Click += (_, _) => _ = OpenDay(d, "calendar");
                host = btn;
            }
            else host = cell;
            Grid.SetRow(host, 1 + i / 7); Grid.SetColumn(host, i % 7);
            _monthGrid.Children.Add(host);
        }
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var d in _days.Where(d => d.Year == _month.Year && d.Month == _month.Month).OrderByDescending(d => d))
        {
            var item = new ListBoxItem { Content = $"{d.Month}/{d.Day} ({Wd[(int)d.DayOfWeek]})", Tag = d };
            _list.Items.Add(item);
            if (d == _selected) _list.SelectedItem = item;
        }
    }

    // ───────────────────────────── 日の移動・描画 ─────────────────────────────
    private async Task OpenDay(DateOnly d, string via)
    {
        _selected = d;
        _month = new DateOnly(d.Year, d.Month, 1);
        BuildCalendar(); RefreshList();
        Log.Write("openDay", new { day = d.ToString("yyyy-MM-dd"), via });
        await RenderDay(reload: false);
    }

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseSoftlineBreakAsHardlineBreak().Build();

    private string BuildHtml(string md, bool dark)
    {
        string Res(string n)
        {
            using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("poc." + n)!;
            return new StreamReader(s).ReadToEnd();
        }
        var body = Markdown.ToHtml(md, Pipeline).Replace("<input disabled=\"disabled\" type=\"checkbox\"", "<input type=\"checkbox\" tabindex=\"-1\"");
        var script = """
            (function () {
              var IS_MAC = __ISMAC__, PREVENT = __PREVENT__;
              window.__keyCount = 0;
              document.addEventListener('keydown', function (e) {
                var k = null;
                if (!e.ctrlKey && !e.metaKey && !e.altKey && !e.shiftKey) {
                  if (e.key === 'ArrowLeft') k = 'ArrowLeft';
                  if (e.key === 'ArrowRight') k = 'ArrowRight';
                }
                var mod = IS_MAC ? e.metaKey : e.ctrlKey;
                if (mod && !(IS_MAC ? e.ctrlKey : e.metaKey) && !e.altKey && !e.shiftKey && (e.key === 'e' || e.key === 'E')) k = 'BeginEdit';
                if (!k || e.defaultPrevented) return;
                if (PREVENT) e.preventDefault();
                window.__keyCount++;
                if (typeof invokeCSharpAction === 'function') {
                  invokeCSharpAction('dbg:' + JSON.stringify({ k: k, repeat: e.repeat, pt: Math.round(performance.now()*10)/10, n: window.__keyCount, prevented: e.defaultPrevented }));
                  if (!(k === 'BeginEdit' && e.repeat)) invokeCSharpAction('key:' + k);
                }
              });
              // クリックで WebView 内にフォーカスが入ったかの記録
              document.addEventListener('mousedown', function () { if (typeof invokeCSharpAction === 'function') invokeCSharpAction('dbg:' + JSON.stringify({ mousedown: true, hasFocus: document.hasFocus() })); });
            })();
            """.Replace("__ISMAC__", _isMac ? "true" : "false").Replace("__PREVENT__", _noPrevent ? "false" : "true");
        var nonce = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16));
        return $"<!doctype html><html lang=\"ja\"><head><meta charset=\"utf-8\">" +
               $"<meta http-equiv=\"Content-Security-Policy\" content=\"script-src 'nonce-{nonce}'; object-src 'none'; frame-src 'none'; form-action 'none'\">" +
               $"<title>poc</title><style>{Res("markdown-base.css")}{(dark ? Res("markdown-dark.css") : Res("markdown-light.css"))}</style>" +
               $"<script nonce=\"{nonce}\">{script}</script></head><body>{body}</body></html>";
    }

    private async Task RenderDay(bool reload)
    {
        var gen = ++_renderGen;
        if (_web is null) return;
        var path = DayPath(_selected);
        var md = File.Exists(path) ? File.ReadAllText(path) : "（ファイルなし）";
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        Directory.CreateDirectory(Path.Combine(_cacheDir, "preview"));
        var html = Path.Combine(_cacheDir, "preview", _selected.ToString("yyyy-MM-dd") + ".html");
        File.WriteAllText(html, BuildHtml(md, dark));
        _web.Background = new SolidColorBrush(dark ? Color.FromRgb(0x1E, 0x1E, 0x1E) : Colors.White);
        var same = string.Equals(_pagePath, html, StringComparison.OrdinalIgnoreCase);
        _pagePath = html;
        if (same && reload)
        {
            var y = await _web.InvokeScript("window.scrollY");
            _restoreY = double.TryParse(y?.Trim('"'), out var dv) ? (int)dv : null;
            Log.Write("reload", new { scrollYBefore = y });
            if (gen != _renderGen) return;
            _loading = true;
            _web.Refresh();
        }
        else
        {
            _restoreY = null;
            _loading = true;
            _web.Navigate(new Uri(new Uri(html).AbsoluteUri));
        }
    }

    // ───────────────────────────── WebView ─────────────────────────────
    private void EnsureWebView()
    {
        var web = new NativeWebView { Background = Brushes.White };
        web.EnvironmentRequested += (_, e) =>
        {
            if (e is WindowsWebView2EnvironmentRequestedEventArgs w) w.UserDataFolder = Path.Combine(_cacheDir, "webview2");
        };
        web.NavigationStarted += (_, e) => Log.Write("nav.start", new { uri = e.Request?.ToString() });
        web.NavigationCompleted += async (_, e) =>
        {
            _loading = false;
            Log.Write("nav.completed", new { focus = FocusedName() });
            if (_restoreY is { } y && y > 0) { _ = _web!.InvokeScript($"window.scrollTo(0,{y})"); Log.Write("scrollRestore", new { y }); }
            _restoreY = null;
            await Task.Delay(300);
            var hasFocus = await web.InvokeScript("document.hasFocus()");
            var sy = await web.InvokeScript("window.scrollY");
            Log.Write("nav.after300ms", new { documentHasFocus = hasFocus, scrollY = sy, avaloniaFocus = FocusedName() });
        };
        web.WebMessageReceived += (_, e) =>
        {
            var body = e.Body ?? "";
            Dispatcher.UIThread.Post(() =>
            {
                if (body.Contains("dbg:", StringComparison.Ordinal))
                {
                    Log.Write("page.dbg", new { raw = body });
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(body[(body.IndexOf("dbg:", StringComparison.Ordinal) + 4)..].TrimEnd('"'));
                        if (doc.RootElement.TryGetProperty("pt", out var pt))
                        {
                            var v = pt.GetDouble();
                            if (doc.RootElement.GetProperty("repeat").GetBoolean() && _lastPagePt >= 0) _pageRepeat.Add(v - _lastPagePt);
                            _lastPagePt = v;
                        }
                    }
                    catch { }
                    return;
                }
                Log.Write("page.msg", new { raw = body, focus = FocusedName(), mode = _editMode ? "edit" : "view" });
                var k = body.Trim('"');
                if (!k.StartsWith("key:")) return;
                var key = k[4..];
                Bump("page:" + key);
                if (key == "ArrowLeft") MoveDay(-1, "page");
                else if (key == "ArrowRight") MoveDay(+1, "page");
                else if (key == "BeginEdit" && !_editMode) { SetMode(true, "page:BeginEdit"); }
            });
        };
        web.NewWindowRequested += (_, e) => e.Handled = true;
        _web = web;
        _host.Children.Insert(0, web);
        Log.Write("webview.created", new { adapter = web.AdapterInfo?.Type.ToString() });
        web.AdapterCreated += (_, _) => Log.Write("webview.adapter", new { adapter = web.AdapterInfo?.Type.ToString() });
    }

    private string FocusedName()
    {
        var f = TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement();
        return f is null ? "null" : f.GetType().Name + (f is Control c && !string.IsNullOrEmpty(c.Name) ? ":" + c.Name : "");
    }

    // ───────────────────────────── キー（ウィンドウ Tunnel） ─────────────────────────────
    private KeyModifiers CmdMod => Application.Current?.PlatformSettings?.HotkeyConfiguration.CommandModifiers ?? KeyModifiers.Control;

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        string? tag = null;
        if (e.Key is Key.Left or Key.Right && e.KeyModifiers == KeyModifiers.None) tag = e.Key == Key.Left ? "ArrowLeft" : "ArrowRight";
        else if (e.Key == Key.E && e.KeyModifiers == CmdMod) tag = "ModE";
        if (tag is null) return;
        var now = Log.Now;
        _lastAt.TryGetValue("w:" + tag, out var prev);
        _lastAt["w:" + tag] = now;
        if (prev != 0 && now - prev < 1000) _winRepeat.Add(now - prev);
        Log.Write("win.keydown", new
        {
            key = e.Key.ToString(), mods = e.KeyModifiers.ToString(),
            source = e.Source?.GetType().Name, focus = FocusedName(), mode = _editMode ? "edit" : "view",
            handled = e.Handled, sincePrevMs = prev == 0 ? (double?)null : Math.Round(now - prev, 1),
            isImeProcessed = e.Key == Key.ImeProcessed,
        });
        if (_editMode)
        {
            if (tag == "ModE") { Bump("avalonia:ModE"); SetMode(false, "avalonia:ModE"); e.Handled = true; }
            return; // 編集中の ←→ は TextBox に任せる
        }
        Bump("avalonia:" + tag);
        if (tag == "ArrowLeft") { MoveDay(-1, "avalonia"); if (!_noHandleLR) e.Handled = true; }
        else if (tag == "ArrowRight") { MoveDay(+1, "avalonia"); if (!_noHandleLR) e.Handled = true; }
        else if (tag == "ModE") { SetMode(true, "avalonia:ModE"); e.Handled = true; }
    }

    private static object Stats(List<double> v) => v.Count == 0 ? new { n = 0 } :
        new { n = v.Count, minMs = Math.Round(v.Min(), 1), medianMs = Math.Round(v.OrderBy(x => x).ElementAt(v.Count / 2), 1), maxMs = Math.Round(v.Max(), 1) };

    private void Bump(string key)
    {
        _count[key] = _count.GetValueOrDefault(key) + 1;
        UpdateStatus();
    }

    private void UpdateStatus() =>
        _status.Text = $"{_selected:yyyy-MM-dd} mode={(_editMode ? "EDIT" : "VIEW")}  " + string.Join("  ", _count.OrderBy(k => k.Key).Select(k => $"{k.Key}={k.Value}"));

    private void MoveDay(int dir, string via)
    {
        var idx = _days.IndexOf(_selected);
        var target = dir < 0 ? _days.Where(d => d < _selected).Cast<DateOnly?>().LastOrDefault() : _days.Where(d => d > _selected).Cast<DateOnly?>().FirstOrDefault();
        Log.Write("move", new { dir, via, from = _selected.ToString("yyyy-MM-dd"), to = target?.ToString("yyyy-MM-dd") });
        if (target is { } t) _ = OpenDay(t, via);
        UpdateStatus();
    }

    private void Toggle(string via) => SetMode(!_editMode, via);

    private void SetMode(bool edit, string via)
    {
        _editMode = edit;
        _edit.IsVisible = edit;
        if (_web is not null) _web.IsVisible = !edit;
        if (edit) { _edit.Focus(); _edit.CaretIndex = 0; }
        Log.Write("mode", new { edit, via, focus = FocusedName() });
        UpdateStatus();
    }

    // ───────────────────────────── TextBox（編集補助の横取り） ─────────────────────────────
    private void OnEditKeyDownTunnel(object? sender, KeyEventArgs e)
    {
        Log.Write("edit.keydown.tunnel", new
        {
            key = e.Key.ToString(), mods = e.KeyModifiers.ToString(), ime = e.Key == Key.ImeProcessed,
            physical = e.PhysicalKey.ToString(), symbol = e.KeySymbol, device = e.KeyDeviceType.ToString(),
            handled = e.Handled, caret = _edit.CaretIndex, len = _edit.Text?.Length,
        });
        if (e.Key == Key.ImeProcessed) return;
        var text = _edit.Text ?? "";
        if (e.Key == Key.Tab && (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None)
        {
            e.Handled = true;
            var ls = LineStart(text, _edit.CaretIndex);
            if (e.KeyModifiers == KeyModifiers.None) ReplaceRange(ls, 0, "  ", _edit.CaretIndex + 2);
            else if (text.AsSpan(ls).StartsWith("  ")) ReplaceRange(ls, 2, "", Math.Max(ls, _edit.CaretIndex - 2));
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == CmdMod)
        {
            e.Handled = true; // 改行を混入させない
            var ls = LineStart(text, _edit.CaretIndex);
            var le = text.IndexOf('\n', ls); if (le < 0) le = text.Length;
            var line = text[ls..le];
            var i = line.IndexOf("- [ ] ", StringComparison.Ordinal);
            var j = line.IndexOf("- [x] ", StringComparison.Ordinal);
            if (i >= 0) ReplaceRange(ls + i + 3, 1, "x", _edit.CaretIndex);
            else if (j >= 0) ReplaceRange(ls + j + 3, 1, " ", _edit.CaretIndex);
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None)
        {
            var ls = LineStart(text, _edit.CaretIndex);
            var le = text.IndexOf('\n', ls); if (le < 0) le = text.Length;
            var line = text[ls..le];
            var trimmed = line.TrimStart(' ');
            if (trimmed.StartsWith("- [ ] ", StringComparison.Ordinal) || trimmed.StartsWith("- [x] ", StringComparison.Ordinal))
            {
                e.Handled = true;
                var indent = line[..(line.Length - trimmed.Length)];
                var ins = "\n" + indent + "- [ ] ";
                ReplaceRange(_edit.CaretIndex, 0, ins, _edit.CaretIndex + ins.Length);
            }
        }
    }

    private void OnEditKeyDownBubble(object? sender, KeyEventArgs e)
    {
        Log.Write("edit.keydown.bubble", new { key = e.Key.ToString(), mods = e.KeyModifiers.ToString(), ime = e.Key == Key.ImeProcessed, handled = e.Handled });
        if (e.Key == Key.Escape && !e.Handled && e.Key != Key.ImeProcessed)
        {
            Log.Write("edit.escape.exit", new { });
            SetMode(false, "Esc");
        }
    }

    // 方式 S：選択範囲の置換（SelectedText）
    private void ReplaceRange(int start, int length, string replacement, int caretAfter)
    {
        _edit.SelectionStart = start;
        _edit.SelectionEnd = start + length;
        _edit.SelectedText = replacement;
        _edit.CaretIndex = caretAfter;
    }

    private static int LineStart(string t, int caret)
    {
        var i = t.LastIndexOf('\n', Math.Max(0, Math.Min(caret, t.Length) - 1));
        return caret == 0 ? 0 : i + 1;
    }


    // ───────────────────────────── mac：NSEvent を本物の経路に流す ─────────────────────────────
    private IntPtr _nsWindow;

    private void Snap(string label, object? extra = null) =>
        Log.Write("snap", new
        {
            label, counts = _count.OrderBy(k => k.Key).ToDictionary(k => k.Key, k => k.Value),
            selected = _selected.ToString("yyyy-MM-dd"), mode = _editMode ? "edit" : "view",
            avaloniaFocus = FocusedName(), firstResponder = MacEvents.FirstResponderClass(_nsWindow), extra,
        });

    private async Task Press(ushort code, string chars, ulong flags, int repeats = 0, int intervalMs = 33)
    {
        MacEvents.Key(_nsWindow, code, chars, flags, true);
        for (var i = 0; i < repeats; i++)
        {
            await Task.Delay(intervalMs);
            MacEvents.Key(_nsWindow, code, chars, flags, true, isRepeat: true);
        }
        await Task.Delay(20);
        MacEvents.Key(_nsWindow, code, chars, flags, false);
    }

    private void ClickAt(Control c, double dx, double dy)
    {
        var p = c.TranslatePoint(new Point(dx, dy), this)!.Value;
        var h = _root.Bounds.Height;
        MacEvents.Mouse(_nsWindow, 1, p.X, p.Y, h);
        MacEvents.Mouse(_nsWindow, 2, p.X, p.Y, h);
    }

    private async Task Drive()
    {
        _nsWindow = TryGetPlatformHandle()!.Handle;
        const string L = "\uF702", R = "\uF703";
        var prevent = !_noPrevent;
        Log.Write("drive.begin", new { handleDescriptor = TryGetPlatformHandle()!.HandleDescriptor, preventDefault = prevent });
        await Task.Delay(500);
        Snap("T0 初期");

        // T1：何もクリックしていない状態で → を 1 回
        _count.Clear(); await Press(MacEvents.KeyLeft, L, 0); await Task.Delay(700);
        Snap("T1 クリック前に ← 1回");

        // T2：WebView をクリック → ← → を 1 回ずつ
        _count.Clear();
        ClickAt(_web!, _web!.Bounds.Width / 2, 150); await Task.Delay(600);
        Snap("T2a WebView をクリックした直後");
        await Press(MacEvents.KeyLeft, L, 0); await Task.Delay(800);
        Snap("T2b WebView フォーカスで ← 1回");
        await Press(MacEvents.KeyRight, R, 0); await Task.Delay(800);
        Snap("T2c WebView フォーカスで → 1回");

        // T3：Cmd+E（WebView フォーカス・ビュー）
        _count.Clear();
        await Press(MacEvents.KeyE, "e", MacEvents.Command); await Task.Delay(800);
        Snap("T3a WebView で Cmd+E 1回（ビュー→編集になるはず）");
        await Press(MacEvents.KeyE, "e", MacEvents.Command); await Task.Delay(800);
        Snap("T3b 編集中に Cmd+E 1回（ビューに戻るはず）");

        // T4：長押し（WebView フォーカス）。リピート 25 回 / 33ms
        _count.Clear();
        ClickAt(_web!, _web!.Bounds.Width / 2, 150); await Task.Delay(500);
        await Press(MacEvents.KeyLeft, L, 0, repeats: 25, intervalMs: 33); await Task.Delay(1200);
        Snap("T4 WebView で ← 長押し（down 1 + repeat 25）");

        // T5：カレンダーのボタンをクリックしてから → 1回
        _count.Clear();
        var btn = _monthGrid.Children.OfType<Button>().First();
        ClickAt(btn, 10, 10); await Task.Delay(600);
        Snap("T5a カレンダーのボタンをクリックした直後");
        await Press(MacEvents.KeyRight, R, 0); await Task.Delay(800);
        Snap("T5b カレンダーにフォーカスで → 1回");

        // T6：一覧をクリックしてから ← 1回
        _count.Clear();
        ClickAt(_list, 40, 12); await Task.Delay(600);
        Snap("T6a 一覧をクリックした直後");
        await Press(MacEvents.KeyLeft, L, 0); await Task.Delay(800);
        Snap("T6b 一覧にフォーカスで ← 1回");

        // T7：GridSplitter をマウスでドラッグ（左ペイン幅）
        var w0 = _leftCol.ActualWidth;
        var h = _root.Bounds.Height;
        MacEvents.Mouse(_nsWindow, 1, 303, 300, h);
        foreach (var x in new[] { 350, 420, 520 }) { MacEvents.Mouse(_nsWindow, 6, x, 300, h); await Task.Delay(60); }
        MacEvents.Mouse(_nsWindow, 2, 520, 300, h); await Task.Delay(300);
        var w1 = _leftCol.ActualWidth;
        MacEvents.Mouse(_nsWindow, 1, w1 + 3, 300, h);
        foreach (var x in new[] { 300.0, 200, 100, 20 }) { MacEvents.Mouse(_nsWindow, 6, x, 300, h); await Task.Delay(60); }
        MacEvents.Mouse(_nsWindow, 2, 20, 300, h); await Task.Delay(300);
        Log.Write("splitter", new { before = w0, afterDragRight = w1, afterDragFarLeft = _leftCol.ActualWidth, min = _leftCol.MinWidth });

        // T8：編集モードでの ← （TextBox に任せる）と Tab / Cmd+Enter / Enter（実イベント）
        _count.Clear();
        SetMode(true, "drive"); _edit.Text = "- [ ] あ\n- [ ] い"; await Task.Delay(300);
        _edit.CaretIndex = 5;
        await Press(MacEvents.KeyLeft, L, 0); await Task.Delay(300);
        Log.Write("T8a", new { caretAfterLeft = _edit.CaretIndex, counts = _count, selected = _selected.ToString("yyyy-MM-dd") });
        await Press(MacEvents.KeyTab, "\t", 0); await Task.Delay(300);
        Log.Write("T8b Tab", new { text = _edit.Text, focus = FocusedName() });
        await Press(MacEvents.KeyEnter, "\r", MacEvents.Command); await Task.Delay(300);
        Log.Write("T8c Cmd+Enter", new { text = _edit.Text });
        _edit.CaretIndex = _edit.Text!.IndexOf('\n');
        await Press(MacEvents.KeyEnter, "\r", 0); await Task.Delay(300);
        Log.Write("T8d Enter", new { text = _edit.Text, hasCR = _edit.Text!.Contains('\r') });
        // 普通の行（チェック項目でない）の Enter：TextBox の既定の改行が \n か
        _edit.Text = "ふつう"; _edit.CaretIndex = 3; await Task.Delay(100);
        await Press(MacEvents.KeyEnter, "\r", 0); await Task.Delay(300);
        Log.Write("T8e plainEnter", new { text = _edit.Text, hasCR = _edit.Text!.Contains('\r'), len = _edit.Text.Length });
        // Esc（Bubble・IME でない）で抜ける
        await Press(MacEvents.KeyEsc, "\u001b", 0); await Task.Delay(300);
        Snap("T8f Esc 後");
        // T9：スクロール位置の復元・テーマ切替での再描画・スクリーンショット
        var shots = Program.Opt("--shots") ?? Path.Combine(_dataDir, "..");
        await OpenDay(DateOnly.Parse("2026-10-14"), "drive"); await Task.Delay(800);
        Log.Write("shot.light", new { ok = MacEvents.Shot(_nsWindow, Path.Combine(shots, "shot-light.png")) });
        await _web!.InvokeScript("window.scrollTo(0, 400)"); await Task.Delay(300);
        var y0 = await _web.InvokeScript("window.scrollY");
        RequestedThemeVariant = ThemeVariant.Dark; await Task.Delay(1500);
        var y1 = await _web.InvokeScript("window.scrollY");
        var bg = await _web.InvokeScript("getComputedStyle(document.body).backgroundColor");
        Log.Write("T9 theme", new { scrollBefore = y0, scrollAfter = y1, bodyBg = bg, actual = ActualThemeVariant.ToString() });
        Log.Write("shot.dark", new { ok = MacEvents.Shot(_nsWindow, Path.Combine(shots, "shot-dark.png")) });
        RequestedThemeVariant = ThemeVariant.Light; await Task.Delay(1000);
        Log.Write("drive.end");
        if (Program.Args.Contains("--exit-after-drive")) Close();
    }


    // ───────────────────────────── IME（mac の日本語入力） ─────────────────────────────
    private async Task DriveIme()
    {
        _nsWindow = TryGetPlatformHandle()!.Handle;
        var original = MacEvents.CurrentInputSourceId();
        var all = MacEvents.InputSources().Select(x => x.Id).ToList();
        Log.Write("ime.sources", new { original, all });
        var target = all.FirstOrDefault(i => i.Contains("Kotoeri", StringComparison.OrdinalIgnoreCase) && i.Contains("Japanese", StringComparison.OrdinalIgnoreCase))
                     ?? all.FirstOrDefault(i => i.Contains("Japanese", StringComparison.OrdinalIgnoreCase));
        if (target is null) { Log.Write("ime.skip", new { reason = "日本語の入力ソースが無い" }); return; }
        try
        {
            Log.Write("ime.select", new { target, ok = MacEvents.Select(target) });
            await Task.Delay(800);
            Log.Write("ime.current", new { id = MacEvents.CurrentInputSourceId() });
            SetMode(true, "ime");
            _edit.Text = "- [ ] あ"; _edit.Focus(); _edit.CaretIndex = _edit.Text.Length;
            await Task.Delay(500);

            async Task Step(string label, Func<Task> act)
            {
                Log.Write("ime.step", new { label, text = _edit.Text, caret = _edit.CaretIndex, mode = _editMode ? "edit" : "view" });
                await act();
                await Task.Delay(500);
                Log.Write("ime.after", new { label, text = _edit.Text, caret = _edit.CaretIndex, mode = _editMode ? "edit" : "view" });
            }

            // 変換中（未確定）にする：かな入力なので 'a' のキーで未確定の文字が出る
            await Step("1: 'a' で変換開始", () => Press(0, "a", 0));
            await Step("2: 変換中の Tab", () => Press(MacEvents.KeyTab, "\t", 0));
            await Step("3: 変換中の Esc", () => Press(MacEvents.KeyEsc, "\u001b", 0));
            _edit.Focus();
            await Step("4: もう一度 'a' で変換開始", () => Press(0, "a", 0));
            await Step("5: 変換中の Enter（確定）", () => Press(MacEvents.KeyEnter, "\r", 0));
            await Step("6: 確定直後の Enter（IME なし）", () => Press(MacEvents.KeyEnter, "\r", 0));
            await Step("7: 'a' → Esc → Esc（変換取消のあと素の Esc）", async () => { await Press(0, "a", 0); await Task.Delay(300); await Press(MacEvents.KeyEsc, "\u001b", 0); await Task.Delay(300); });
        }
        finally
        {
            Log.Write("ime.restore", new { original, ok = MacEvents.Select(original) });
        }
        Log.Write("ime.end");
        if (Program.Args.Contains("--exit-after-drive")) Close();
    }

    // ───────────────────────────── 起動後・自己テスト ─────────────────────────────
    private async Task OnOpened()
    {
        EnsureWebView();
        await RenderDay(reload: false);
        await Task.Delay(800);
        var wv = _web!;
        var tl = wv.TranslatePoint(new Point(wv.Bounds.Width / 2, 120), this);
        var origin = this.PointToScreen(tl ?? new Point(0, 0));
        var scaling = this.RenderScaling;
        Log.Write("geom", new
        {
            windowPos = new { x = Position.X, y = Position.Y }, scaling,
            webviewClickPx = new { x = origin.X, y = origin.Y },
            webviewClickPt = new { x = origin.X / scaling, y = origin.Y / scaling },
            leftWidth = _leftCol.ActualWidth, leftMin = _leftCol.MinWidth, root = _root.Bounds.Width,
            listPt = ToScreenPt(_list), calPt = ToScreenPt(_monthGrid), editPt = ToScreenPt(_edit),
        });
        if (Program.Args.Contains("--selftest")) await SelfTest();
        if (Program.Args.Contains("--drive")) await Drive();
        if (Program.Args.Contains("--ime")) await DriveIme();
        if (Program.Args.Contains("--exit-after-selftest")) Close();
    }

    private object ToScreenPt(Control c)
    {
        var p = c.TranslatePoint(new Point(c.Bounds.Width / 2, Math.Min(40, c.Bounds.Height / 2)), this) ?? new Point();
        var s = this.PointToScreen(p);
        return new { x = s.X / RenderScaling, y = s.Y / RenderScaling };
    }

    private async Task SelfTest()
    {
        // (1) TextBox の Undo：全文置換 vs 範囲置換
        foreach (var method in new[] { "text", "selection" })
        {
            _edit.IsVisible = true;
            _edit.Text = "- [ ] a\n- [ ] b";
            _edit.Focus();
            await Task.Delay(100);
            var before = _edit.Text;
            for (var n = 1; n <= 2; n++)
            {
                _edit.CaretIndex = 0;
                if (method == "text") { var t = _edit.Text!; _edit.Text = "  " + t; _edit.CaretIndex = 2; }
                else { _edit.SelectionStart = 0; _edit.SelectionEnd = 0; _edit.SelectedText = "  "; _edit.CaretIndex = 2; }
            }
            var afterTwo = _edit.Text;
            _edit.Undo();
            var afterUndo1 = _edit.Text;
            _edit.Undo();
            var afterUndo2 = _edit.Text;
            _edit.Undo();
            var afterUndo3 = _edit.Text;
            Log.Write("selftest.undo", new { method, before, afterTwo, afterUndo1, afterUndo2, afterUndo3, canUndo = _edit.CanUndo });
        }
        // (2) TextBox.Text の改行（\r\n を入れて読み戻す）
        _edit.Text = "a\r\nb\nc";
        Log.Write("selftest.newline", new { readBackHasCR = _edit.Text!.Contains('\r'), len = _edit.Text.Length, newLineProp = _edit.NewLine });
        _edit.Text = "";
        _edit.IsVisible = _editMode;
        // (3) Tunnel で Tab を横取りできるか（RaiseEvent）
        if (!_editMode) { _edit.IsVisible = true; }
        _edit.Text = "- [ ] x";
        _edit.Focus();
        await Task.Delay(100);
        var kd = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Tab, KeyModifiers = KeyModifiers.None, Source = _edit };
        _edit.RaiseEvent(kd);
        Log.Write("selftest.tab", new { text = _edit.Text, handled = kd.Handled });
        var kd2 = new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = Key.Enter, KeyModifiers = CmdMod, Source = _edit };
        _edit.RaiseEvent(kd2);
        Log.Write("selftest.modEnter", new { text = _edit.Text, handled = kd2.Handled });
        _edit.IsVisible = _editMode;
    }
}
