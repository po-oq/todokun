# todokun（日次Markdown TODOアプリ） 実装計画

> **連動ルール**：この md と `todokun-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/todo-requirements.md`（全章。13章が PoC）。画面の資料は `docs/todokun-design.html`。仕様と食い違ったら仕様が正。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> 進め方：PoC → ロジック（テスト先行）→ 画面 → キー・起動 → 仕上げ。各フェーズの終わりに「mac で `dotnet build/test`」と、画面・キー・IME・WebView は **Windows 実機**で確かめ、本書の進捗（1 章）を更新して報告する。コミットは頼まれたときだけ（ここは区切りの目安）。Issue は使っていない（リポジトリにまだコミットが無い）ので、コミットメッセージに `Refs #N` は付けない。

## 1. 全体像

```
P0 PoC ─▶ P1 Core ─▶ P2 編集補助 ─▶ P3 Docs ─▶ P4 左ペイン ─▶ P5 右ペイン ─▶ P6 キー・起動 ─▶ P7 仕上げ
 2 commit   6 commit    3 commit      3 commit    3 commit      4 commit       3 commit          1 commit
```

- 新しい依存パッケージ：Avalonia 12.1.3・Avalonia.Desktop・Avalonia.Themes.Fluent・Avalonia.Controls.WebView 12.1.0・CommunityToolkit.Mvvm 8.4.2・Markdig 1.4.0（Miharikun と同じ版）。テストは xUnit・Avalonia.Headless.XUnit 12.1.3（Miharikun の `UiTests` と同じ構成）。それ以外は入れない。
- 守る制約：本番は Windows 11、mac は開発専用（配布物なし）。LLM・ネット接続なし。Markdig を参照するのは `Todokun.Docs` だけ。`Todokun.Core`・`Todokun.Presentation` は Avalonia・Markdig を参照しない。
- **共通化のフェーズは無い**。Miharikun は別リポジトリなので「コピーして削る」（11.7）。コピーした部品の名前空間は `Todokun.*` に揃える。
- いまのテスト：0（新規リポジトリ）。**以後、件数は減らさない**。
- 参照元の Miharikun は `https://github.com/po-oq/Miharikun`（clone して読む）。コピー元のファイルは 6 章の表に書く。

### 品質ゲート（各フェーズの終わり）
1. `dotnet build` 警告なし・`dotnet test` 全部通る（件数を減らさない）。
2. P3 以降：`Todokun.Docs` 以外が Markdig を参照していない（csproj を見る）。
3. 画面・キー・IME・WebView に触れるフェーズ（P4〜P6）は **Windows 実機**で、その章の確認項目を全部。mac だけで完了にしない（要件 15 章）。
4. 確かめるときは `--data-dir` で一時フォルダに向ける。本物のデータに触れない。

### 進捗（要件の旧 14 章の `[ ]` / `[x]` はここで管理する）
- [ ] P0 PoC　- [ ] P1 Core　- [ ] P2 編集補助　- [ ] P3 Docs　- [ ] P4 左ペイン　- [ ] P5 右ペイン　- [ ] P6 キー・起動　- [ ] P7 仕上げ

## 2. フェーズ P0：PoC（捨てる画面。要件 13 章）

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 0-1 | PoC の土台 | `poc/` に捨てる Avalonia アプリ（コミットしない、または別ブランチ）。`NativeWebView`・`TextBox`・`GridSplitter`・月グリッドの試作・キー到達を数えるカウンタを載せる | 13 章の各項目を、この画面で確かめられる |
| 0-2 | 結果の追記 | 13 章の各項目を `[x]` 確認済み／`[ ]` 保留にし、結果（数値・OS 別）を要件に追記。影響する仕様（カレンダーの作り方・Undo の方式・キー経路の一本化）を直す | 影響する仕様の章を直した |

完了条件：13 章が「確認済み」または「保留」に整理されている。**P6-1（キー経路）と P2・P5-4（Undo の方式）は、ここの結果で中身が決まる**（11.4・11.5）。

## 3. フェーズ P1：Core（画面なし。テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 1-1 | `AppPaths`・`LaunchOptions` | `--data-dir <p>`・`--data-dir=<p>`・`TODOKUN_DATA_DIR`（引数が優先）→ なければ `<exeDir>/data`。`exeDir` は `Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory`。`CacheRoot` は常に `<exeDir>/cache`。相対パスはフルパスにする。書き込みできるかの判定（`ProbeWritable`：作成→空の一時ファイルを作って消す） | 引数／環境変数／既定の優先順位、相対パス、空の値、`--data-dir` の後ろに値が無い、書けない／存在しない／ファイルを指す |
| 1-2 | `AtomicFile`（コピー） | Miharikun の `Miharikun.Core/Storage/AtomicFile.cs` をコピー（名前空間のみ変更）。一時ファイル→`File.Replace`・20×10ms の再試行・`~RF*.TMP` の掃除 | 新規・上書き・親フォルダが無い・一時ファイルが残らない |
| 1-3 | `DayFile`・`DayStore` | 日ファイルのパス（`data/yyyy/MM/yyyy-MM-dd.md`・月は 2 桁）。`TryParse(path)`（形に一致し、**年月がフォルダと一致**するものだけ）。`Read`（UTF-8・BOM 許容・CRLF そのまま返す）、`Write`（LF に統一・BOM なし・`AtomicFile`）、`Delete` | 形に一致しない名前・年月ずれ・`.tmp`・他の拡張子を無視（消さない・触らない）、BOM 付き・CRLF の読み、LF での書き、読めないファイル |
| 1-4 | `DayIndex` | 起動時に `data/` を全走査して `SortedSet<DateOnly>` を持つ。`DaysIn(year, month)` は降順。`Previous(day)`・`Next(day)`（その日が無い日でも基準にできる。端は null）。`Add`・`Remove`（保存・削除のとき更新） | 月またぎ、年またぎ、基準日がファイルの無い日、端、未来の日、365 件でも一瞬（件数だけ確認） |
| 1-5 | `StartupCopy` | 11.2 の表のとおり。`Run(today)` は結果（`Copied(from)`・`NotNeeded`・`NoSource`・`Failed(reason)`）を返す | 11.2 の表の全行（一時フォルダ。書き込めないフォルダ・読めない元を含む） |
| 1-6 | `SingleInstance` | データルートごとの名前付き Mutex（名前は `todokun-` ＋ フルパス（小文字）の SHA-256 先頭 16 桁）。取れなければ false。解放は終了時 | 同じルートは 2 つ目が取れない、別ルートは取れる（mac で Mutex が使えない場合は 11.1 の代わり） |

完了条件：`Todokun.Tests` が通る。`Todokun.Core` は Avalonia・Markdig を参照しない。

## 4. フェーズ P2：編集補助ロジック（Core。テスト先行）

純粋関数。入力は `EditState(Text, Caret, SelStart, SelEnd)`、出力は `EditResult?`（null＝何もしない）。詳しい動きは 11.5。

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 2-1 | `Indent`・`Outdent` | Tab／Shift+Tab（選択の複数行・タブ文字・CRLF）。結果は**範囲の置換**（`Start`・`Length`・`Replacement`）とカーソル・選択 | 11.5 の表の行ごと。行頭・行末・字下げ 0・深い・選択が複数行・選択が行頭で終わる・CRLF |
| 2-2 | `ToggleCheck` | Ctrl/Cmd+Enter。`[ ]`⇄`[x]`、`[X]`→`[ ]`、チェック以外は何もしない（ただしキーは食う） | チェック以外・字下げあり・`[X]`・`*`／`+` のリスト（対象外）・カーソル位置が変わらない |
| 2-3 | `ContinueItem` | Enter。続行／空項目で抜ける／通常の改行（null） | 11.5 の表の行ごと。カーソルが行の途中・マーカーの前・選択あり・CRLF・空項目・字下げのみ |

完了条件：要件 10 章の表と 11.5 の表の全行がテストになっている。

## 5. フェーズ P3：Markdown レンダリング（Docs）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 3-1 | `Todokun.Docs` の土台と CSS | プロジェクト・`Assets/markdown-base.css`・`markdown-light.css`・`markdown-dark.css` を Miharikun からコピー（mermaid・highlight.js・vendor は持ち込まない。csproj の `EmbeddedResource` も CSS だけ） | 埋め込みリソースが読める |
| 3-2 | `TodoRenderer` | Miharikun の `MarkdownRenderer` を元に：Markdig パイプライン（`UseAutoIdentifiers(GitHub)`→`UseAdvancedExtensions`→`UseSoftlineBreakAsHardline`）、`ReadOnlyCheckboxes`、`NeutralizeHttpEquiv`、CSP＋nonce（script は全部 `<head>`）、`#見出し` のページ内移動、チェックボックスのクリック取り消し、`ScrollKeeperScript`、**キー転送スクリプト（11.6）**。`libFolder`・mermaid・hljs は無し。`Render(markdown, baseFolder, isDark, isMac, title)` | 入れ子 2 段・3 段、`[x]` `[X]` `[ ]`、番号付き内、コードブロック内を変換しない、単一改行が `<br>`、`-[ ]`（スペース無し）は変換しない、`<meta http-equiv>`・`javascript:`・生 `<script>` が効かない、nonce は script だけ |
| 3-3 | キー転送スクリプト | `key:ArrowLeft`・`key:ArrowRight`・`key:ToggleEdit` を `invokeCSharpAction` で送る。`isMac` で `metaKey`／`ctrlKey` を切り替え。受け側の判定 `PreviewKeyMessage.Parse(body)`（`"key:…"` の引用符つきも受ける。他は null） | 3 種＋引用符つき＋不明な文字列＋空を捨てる |

完了条件：`Todokun.Tests` の Docs が通る。出力 HTML に `mermaid`・`highlight` の文字が出ない。

## 6. フェーズ P4：画面（左ペイン）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 4-1 | `Todokun.Presentation`：`CalendarViewModel` | 11.3。月グリッド（42 セル）・点・今日・選択・前後月の日は押せない・◀▶・日曜始まり・土青／日赤の区別用のプロパティ | VM のテスト：月の切り替え、点の有無、前後月は `IsEnabled=false`、選択が月外のとき強調なし |
| 4-2 | `DayListViewModel` | 表示月の日を降順・`M/d (曜)`（曜日は配列で。culture に依らない）。選択のハイライト | VM のテスト：降順・曜日・月の切り替えで連動 |
| 4-3 | `Todokun`：`MainWindow`・`LeftPaneView`・テーマ | `GridSplitter`（最小幅＝カレンダーが収まる幅）、`Colors.Light/Dark.axaml`・`AppStyles.axaml` を Miharikun から削ってコピー、`ThemeVariant.Default`（OS 追従）、タイトルバー（`--data-dir` 指定時だけパスを出す） | Headless：月切替と一覧の連動・ハイライト・ライト/ダーク。**Windows 実機**：幅のドラッグ・最小幅・点・今日・選択の丸 |

完了条件：要件 8.1・8.2 の各項目を確かめる（PoC でカレンダーの作り方が決まっていること）。

## 7. フェーズ P5：画面（右ペイン）

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 5-1 | `DayEditorSession` | 11.4 の状態の部品（View／Edit・Saved・Draft・Dirty・保存・空保存の削除・失敗）。`IUiServices` の確認ダイアログ（保存／破棄／キャンセル、削除／キャンセル）・タイマーは interface 越し | 11.4 の表の行ごと（Core の一時フォルダ＋ダイアログはフェイク） |
| 5-2 | `TodoPreview`（`MarkdownPreview` の切り出し） | 11.7 のとおり。`PreviewNavigationPolicy`・`LocalLinkRule`・`ShellOpen`・`PreviewFiles` をコピー。`WebView2UserDataFolder` は `cache/webview2`。`cache/` が書けないとき・Runtime 未導入のときは案内 | Headless：`WebViewDisabled` で案内の出し入れ。`PreviewNavigationPolicy`・`LocalLinkRule` のテストは Miharikun のものを流用 |
| 5-3 | `RightPaneView` | 上バー（日付・モード・「✎ 編集」／「保存」「ビューに戻る」「未保存」）、`TodoPreview` と `TextBox` を同じ場所で `IsVisible` で入れ替え（重ねない）、案内文、編集に入ったらフォーカス・カーソル先頭、`Ctrl/Cmd+S`（`HotkeyConfiguration.CommandModifiers`。編集中だけ）、保存失敗の表示、`TextBox`：`AcceptsReturn`・折り返しあり・等幅・縦スクロール | Headless：切り替え・未保存の表示・保存キー。**Windows 実機**：書く→保存→再起動後も残る、入れ子のチェックボックス表示、スクロール位置の復元 |
| 5-4 | 編集補助のキー | `TextBox` のキーを Tunnel で受けて P2 の関数へ（Tab・Shift+Tab・Ctrl/Cmd+Enter・Enter）。適用方法は PoC の Undo の結果（11.5） | Headless：キー → テキストの変化。**Windows 実機・mac**：Undo が 1 操作で戻る、IME の変換確定の Enter で項目が増えない |

完了条件：要件 8.3・8.4・10 章を確かめる。

## 8. フェーズ P6：キー操作と起動時の流れ

| # | コミット | 内容 | テスト／確認 |
|---|---|---|---|
| 6-1 | キー経路 | 11.6。ウィンドウの Tunnel の受け口（ビューモードの ←→、どこでも `Mod+E`）とページ経路（`OnWebMessageReceived`）。**二重到達の対策は PoC の結果に従う** | Headless：ビューモードだけ効く・編集モードの ←→ はカーソル移動。**Windows 実機**：1 回の押下で 1 日だけ移る（カレンダー・一覧・WebView のどこにフォーカスがあっても）、長押しで連続して移っても最後の日が表示される |
| 6-2 | 起動の流れ | `App` の組み立て：`SingleInstance`→`AppPaths`→`ProbeWritable`→`DayIndex`→`StartupCopy`→今日を選択・ビューモード。コピー失敗・データルートが書けない・2 つ目の起動の案内 | 一時フォルダで：コピーあり／なし／失敗、書けない（編集不可・閲覧可）、2 つ目の起動 |
| 6-3 | 閉じる | ウィンドウを閉じるとき未保存なら確認（Closing は 2 回目も来る・ダイアログは Post で出す。Miharikun の `MainWindow` の流れに倣う） | Headless：キャンセルで閉じない。**Windows 実機**：Alt+F4・閉じるボタン |

完了条件：要件 7・8.4・11 章を確かめる。

## 9. フェーズ P7：仕上げ

| # | コミット | 内容 |
|---|---|---|
| 7-1 | 仕上げ | `scripts/publish.ps1`（`-r win-x64 --self-contained -p:PublishSingleFile=true`・`IncludeNativeLibrariesForSelfExtract`）、README（使い方・データの場所・`cache/`・`--data-dir`・キー操作）、`.gitignore`（`bin/`・`obj/`・`cache/`・`data/`・`.DS_Store`）、要件 13 章の整理、本書の進捗の更新 |

完了条件：PATH を通した場所から `todokun` で起動して通す。起動 3 秒以内。要件 12 章の非機能（365 ファイル・壊れたファイル・ログに本文を書かない）を確かめる。

## 10. ファイル構成（追加・変更）

```
Todokun.slnx                           0-1/1-1 新規
src/Todokun.Core/
├─ AppPaths.cs・LaunchOptions.cs        1-1 新規：データルート・cache・引数
├─ AtomicFile.cs                        1-2 新規：Miharikun からコピー
├─ DayFile.cs・DayStore.cs              1-3 新規：パス・読み書き・削除
├─ DayIndex.cs                          1-4 新規：ファイルがある日・前後探索
├─ StartupCopy.cs                       1-5 新規：起動時コピー
├─ SingleInstance.cs                    1-6 新規：Mutex
├─ AppLog.cs                            5-2 新規：cache/logs/app.log（本文を書かない）
└─ Editing/EditState.cs・ListEditing.cs 2-1〜2-3 新規：編集補助の純粋関数
src/Todokun.Docs/
├─ Assets/markdown-*.css                3-1 新規：Miharikun からコピー
├─ TodoRenderer.cs                      3-2 新規：MarkdownRenderer を削った版
└─ PreviewKeyMessage.cs                 3-3 新規：ページからのキーの知らせの判定
src/Todokun.Presentation/
├─ CalendarViewModel.cs・DayListViewModel.cs   4-1・4-2 新規
├─ DayEditorSession.cs                  5-1 新規：編集の状態の部品
├─ PreviewNavigationPolicy.cs・LocalLinkRule.cs  5-2 新規：Miharikun からコピー
├─ IPreviewHost.cs・PreviewSource.cs    5-2 新規：縮めた版（11.7）
├─ Services/IUiServices.cs              5-1 新規：ダイアログ・タイマー
└─ MainViewModel.cs                     5-1・6-2 新規：日の選択・モード・起動の結果
src/Todokun/
├─ Program.cs・App.axaml(.cs)           4-3・6-2 新規：組み立て
├─ MainWindow.axaml(.cs)                4-3・6-1・6-3 新規：分割・Tunnel・閉じる
├─ Views/LeftPaneView.axaml(.cs)        4-3 新規
├─ Views/RightPaneView.axaml(.cs)       5-3・5-4 新規
├─ Views/TodoPreview.axaml(.cs)         5-2 新規：MarkdownPreview の切り出し
├─ PreviewFiles.cs・ShellOpen.cs        5-2 新規：Miharikun からコピー
└─ Themes/Colors.*.axaml・AppStyles.axaml  4-3 新規：Miharikun から削ってコピー
tests/Todokun.Tests/                    各コミット：Core・Docs・Presentation（xUnit）
tests/Todokun.UiTests/                  4-3・5-3・5-4・6-1：Avalonia.Headless（`WebViewDisabled=true`）
scripts/publish.ps1・README.md・.gitignore   7-1 新規
```

## 11. 実装の決めごと（迷いやすい所）


### 11.1 起動の順序と置き場
1. `SingleInstance`（データルートごとの Mutex）。取れなければ「すでに起動しています」を出して終了。`--data-dir` が違えば別ルートなので起動できる。
2. `LaunchOptions` → `AppPaths`（`DataRoot`・`CacheRoot`）。
3. `ProbeWritable(CacheRoot)`：書けなければ WebView を作らない（`TodoPreview` に案内。編集・保存は使える）。ログは書けなくても止まらない。
4. `ProbeWritable(DataRoot)`：書けなければ理由を画面に出し、**編集に入れない**（閲覧はできる）。
5. `DayIndex` を全走査 → `StartupCopy.Run(today)` → 今日を選択・ビューモード。
6. 背景で `PreviewFiles.CleanOld(cache/preview)`（1 日より古いものだけ。`lib/` は無いので直下のファイルだけ）。

- Mutex が mac で使えない・不安定なら、`<DataRoot>/.todokun.lock` を `FileShare.None` で開く方式に替える（クラッシュしても自動で解放される）。P1-6 で決める。
- WebView2 の作業フォルダ（`cache/webview2`）は、`--data-dir` が違う 2 つのプロセスで共有される。同時に開いて問題ないか PoC（13 章の `cache/` 項目）で確かめる（要確認）。

### 11.2 起動時コピー（要件 7 章）
| 今日のファイル | 今日より前の元 | 結果 |
|---|---|---|
| ある | — | 何もしない（上書きしない） |
| 無い | 空白だけでない最新がある | その内容を今日のファイルとして `DayStore.Write`（LF 統一）。ビューで開く |
| 無い | 無い（または空白だけのものだけ） | 何もしない（今日は「ファイルなし」） |
| 無い | 新しい順に読んで、読めないファイルがある | 読めたもので最新の「空白だけでない」を使う。全部読めなければ失敗扱い |
| 無い | 元はあるが書けない | 失敗：理由を画面に出して起動を続ける（今日は「ファイルなし」） |
| — | 未来の日付のファイルしかない | 何もしない（未来は対象外） |

- 元の探索は `DayIndex` の日を新しい順に、**今日より前**だけ。内容が空白だけ（`IsNullOrWhiteSpace`）のものは飛ばす。

### 11.3 日の選択・カレンダー・左右キー
- 状態：`SelectedDay`（常に 1 日。ファイルの有無は問わない）、`DisplayedMonth`。
- カレンダー：日曜始まり・6 行 × 7 列。その月の日だけ押せる（前後月は薄い表示・`IsEnabled=false`）。点は `DayIndex` にある日。◀▶ は `DisplayedMonth` だけ変える（`SelectedDay` は変えない）。
- 日を選ぶ（カレンダー・一覧・左右キー）→ `SelectedDay` を更新 →（編集中なら 11.4 の確認）→ ビューを描く。
- 左右キー：`Previous(SelectedDay)`／`Next(SelectedDay)` が null なら何もしない。あれば `SelectedDay` を更新し、**`DisplayedMonth` をその日の月にする**（月をまたぐと左ペインも追従）。
- ファイルの無い日を選んだとき：案内文を出し、保存したときに作る（要件 8.2）。

### 11.4 編集の状態の部品（`DayEditorSession`）
持つもの：`Day`、`Saved`（string?。null＝ファイルなし）、`Mode`（View／Edit）、`Base`（編集を始めた時点の文字）、`Draft`。`IsDirty` ＝ `Mode==Edit && Norm(Draft) != Norm(Base)`（`Norm` は CRLF→LF だけ）。

| 操作 | 結果 |
|---|---|
| 日を開く（View） | `Saved` を読む（読めなければその日だけエラー表示。他の日は使える）。`Mode=View` |
| 編集へ（Ctrl/Cmd+E・「✎ 編集」） | データルートが書けなければ何もしない（理由を出す）。`Base=Draft=Saved ?? ""`。`Mode=Edit`、フォーカス・カーソル先頭 |
| 入力 | `Draft` を更新。`IsDirty` を再計算（「未保存」の表示） |
| 保存（Draft が空白だけではない） | `DayStore.Write`（LF）。成功→`Saved=Norm(Draft)`、`Base=Draft`、`DayIndex.Add`、**編集に留まる**。失敗→理由を出し、編集のまま入力を失わない |
| 保存（Draft が空白だけ・ファイルがある） | 確認「この日のファイルを削除します。よろしいですか？［削除］［キャンセル］」。削除→`DayStore.Delete`・`Saved=null`・`DayIndex.Remove`・`Base=Draft`、編集に留まる。キャンセル→何もしない |
| 保存（Draft が空白だけ・ファイルが無い） | 何も作らない。確認も出さない。`Base=Draft`（未保存の表示が消える） |
| ビューに戻る（Esc・Ctrl/Cmd+E・ボタン） | `IsDirty` でなければそのまま `Mode=View`。`IsDirty`→確認「保存しますか？［保存］［破棄］［キャンセル］」。保存→上の保存を実行し、成功したら View。保存の途中でキャンセルされたら（空保存の削除確認など）中止して編集に留まる。破棄→`Draft` を捨てて View。キャンセル→編集に留まる |
| 別の日を開く（編集中） | 「ビューに戻る」と同じ確認。通ったら日を開く |
| 閉じる | 同じ確認。保存に失敗したら閉じない |
| 外でファイルが変わった | 検知しない（要件 4 章の対象外）。編集中の入力は変えない |

- 状態遷移：View ⇄ Edit。Edit から出る操作は全て「`IsDirty` なら確認」を通す。保存は Edit に留まる（図 11.4）。

### 11.5 編集補助の動き（P2。要件 10 章の補足）
- 入力は `EditState`。改行は `\n`／`\r\n` を**そのまま保つ**（行の内容に `\r` を含めない。挿入する改行は、テキストにある改行に合わせ、無ければ `\n`）。
- 出力は `EditResult(Start, Length, Replacement, Caret, SelStart, SelEnd)`＝**連続した 1 か所の置換**。`Apply(text)` で全文も得られる。画面は、PoC で Undo が自然に効く方（範囲の置換 `SelectedText`／全文の置換）を選ぶ。

| 操作 | 条件 | 結果 |
|---|---|---|
| Tab | 選択なし | カーソル行の行頭に 2 スペース。カーソル +2 |
| Tab | 選択あり | かかっている全行の行頭に 2 スペース。**選択が行頭（桁 0）で終わるとき、その最後の行は含めない**。選択も同じだけ動く |
| Shift+Tab | 行頭が `␠␠` | 2 つ取る |
| Shift+Tab | 行頭が `␠` 1 つだけ | 1 つ取る |
| Shift+Tab | 行頭が `\t` | タブ 1 つを取る |
| Shift+Tab | 行頭が空白でない | 何もしない（null）。カーソルは行頭より前に動かない |
| Ctrl/Cmd+Enter | `^(\s*)- \[([ xX])\] ` の行 | `[ ]`→`[x]`、`[x]`・`[X]`→`[ ]`。カーソル位置は動かさない |
| Ctrl/Cmd+Enter | チェック項目でない | 何もしない。**ただしキーは食う**（改行を入れない） |
| Enter | チェック項目で、カーソルがマーカー（`- [ ] `）の末尾以降・選択なし | 改行して、同じ字下げ（そのままコピー）＋`- [ ] `。カーソルより後ろの文字は新しい項目の本文になる。カーソルは新しい項目の本文の先頭 |
| Enter | チェック項目で、本文が空（マーカーだけ）・カーソルが末尾 | その行を空行にする（字下げも消す）。リストから抜ける |
| Enter | チェック項目で、カーソルがマーカーの内側・字下げの中・選択あり | 通常の改行（null。`TextBox` に任せる） |
| Enter | チェック項目でない行 | 通常の改行（null） |

- `*`・`+` のリストは描画ではチェックボックスになるが、編集補助の対象にしない（`- ` だけ。要件 10 章）。
- IME：変換中・確定の Enter は項目を続行しない。受けるのは「IME に処理されなかった Enter」だけ。IME 中に届くキーの種類（`Key.ImeProcessed` かどうか）は PoC の記録に従う（**要確認**）。

### 11.6 キーの経路（P6-1）
- ページ→アプリ：`TodoRenderer` が `<head>` に入れるスクリプトが、`keydown` で次を `invokeCSharpAction` に送る。`e.defaultPrevented` なら送らない。
  - `ArrowLeft`／`ArrowRight`（修飾キーなし）→ `key:ArrowLeft`／`key:ArrowRight`。`preventDefault` する。
  - `Mod+E`（Windows は `ctrlKey`、mac は `metaKey`。Alt・Shift なし）→ `key:ToggleEdit`。`preventDefault` する。
  - `isMac` は C# が描画時にスクリプトへ埋め込む。
- アプリ側の受け：`PreviewKeyMessage.Parse`（Windows は `"key:…"` と引用符つきで来ることがある。**3 種以外は捨てる**）。`Dispatcher.UIThread.Post` で VM へ。
- ウィンドウの `KeyDown` は **Tunnel**（ListBox・ボタン・カレンダーが矢印キーを先に食わないように）。ビューモードのときだけ ←→（修飾キーなし）を日の移動に使う。`Mod+E` はどのモードでも。編集モードの ←→ は `TextBox` に任せる。
- 1 回の押下がアプリに 2 回届く可能性（Windows は未確認。Miharikun の Esc は冪等で気づけない）。PoC の結果で、次のどちらかにする：
  - A：WebView にフォーカスがあるときはページ経路だけ、ないときは Avalonia 経路だけ（フォーカスの判定方法は PoC で決める）。
  - B：二重に来ないなら、両方を受けるだけ。
- 長押し：移動の要求は毎回 `SelectedDay` を更新するだけで軽い。描画は `TodoPreview` の世代番号（`_version`）で最後の 1 つだけが残る。

### 11.7 `MarkdownPreview` → `TodoPreview` の切り出し（P5-2）
| 持ち込む | 削る・変える |
|---|---|
| `EnsureWebView`・`IsEngineAvailable`・`FailWithRuntimeMissing`・`OnEnvironmentRequested`（`UserDataFolder=cache/webview2`）・`OnNavigationStarted`（`isMarkdownPage` は常に true）・`OnNavigationCompleted`（スクロール位置の復元だけ）・`OnNewWindowRequested`・`Route`・`ParseScrollY`・`WebViewDisabled`・世代番号 | `PreviewSource.File`・`DocumentKind`・html ページ（`_pageIsHtml`・Esc の後入れ）・`RenderMarkdownFileAsync`・`OnOutline`・`OutlineHeading`・`HeadingScrollRequested`・`_pendingHeadingId`・`EnsureLib`・`MarkdownAssets` |

- `IPreviewHost` は縮める：`PreviewDir`・`IsDark`・`EmptyText`・`RuntimeMissingNote`・`PreviewChanged(PreviewSource?, bool)`・`Log`・`OpenLocalLink`・`OnPageKey(string)`。`PreviewSource` は `Markdown(Text, BaseFolder, CacheKey, Title)` と `Message(Text)` だけ。
- `CacheKey` は日ファイルのフルパス。`PreviewFiles.Write` が同じ日を上書きする（日を移っても一時 HTML が増え続けない。1 日より古いものは起動時に消す）。
- `PreviewFiles`（アプリ側 internal）・`ShellOpen`・`AppLog` は、名前空間を揃えてコピーする。**`AppLog` は本文を書かない**。
- `OpenLocalLink` は `LocalLinkRule.Decide`＋`LinkProbe.Real`＋`ShellOpen`（Miharikun の `MemoViewModel.OpenLocalLink` と同じ流れ。アプリ内ドキュメントへの移動は無い）。
- 再描画（保存後・テーマ変更）は `reload=true`。同じページなら、Refresh の前に `window.scrollY` を取り、読み込み後に戻す（mac の WKWebView 対策）。日を移るときは先頭から。

### 11.8 保存まわり
- 書くとき：LF に統一・UTF-8 BOM なし・`AtomicFile`。読むとき：BOM・CRLF を許す。比較（`IsDirty`）は CRLF→LF だけ揃える。
- 一時ファイルは `<day>.md.<guid>.tmp`（日ファイルと同じフォルダ）。`DayFile.TryParse` に一致しないので無視される（消さない）。失敗時に残る `~RF*.TMP` は `AtomicFile` が掃除する。
- 日付は `DateTime.Now`（ローカル時間）。日付の文字は `yyyy-MM-dd`（`CultureInfo.InvariantCulture`）。
- `TextBox.Text` の改行が `\n` か `\r\n` かは **要確認**（保存は LF に統一するので影響しないが、11.5 の関数は両方に対応する）。

### 11.9 Markdig と描画
- パイプラインは `UseAutoIdentifiers(AutoIdentifierOptions.GitHub)` → `UseAdvancedExtensions()` → `UseSoftlineBreakAsHardline()`。順序を変えない（`UseAutoIdentifiers` は `UseAdvancedExtensions` より前）。
- 入れ子のチェックリストは、Miharikun.Docs で実測済み（2 スペース・3 段・タブ・空行つき）。番号付きの下に 2 スペースの `-` を書くと別のリストになる（要件 9 章のとおり対象外）。
- 通常項目が混ざったリストの黒丸と ☐ の位置揃えは、PoC で目視する（CSS の調整が要るかもしれない）。

## 12. リスクと対策

| リスク | 対策 |
|---|---|
| ←→ が 1 押下で 2 回届き、2 日飛ぶ（Windows 未確認） | PoC で回数を数える。二重なら 11.6 の A（経路を一本化）。長押しの確認も |
| `Cmd+E` を WKWebView が検索に使う（mac） | ページで `preventDefault`。効かなければ「✎ 編集」ボタンで代替（開発用なので許容） |
| `TextBox` の Tab・Ctrl+Enter が先に食われる／Undo が壊れる | Tunnel で受ける。Undo は範囲の置換に切り替えられる形（`EditResult`）で作る |
| IME の変換確定の Enter で項目が増える | 「IME に処理されなかった Enter」だけ受ける。PoC で Windows・mac を確かめる。P5-4 の完了条件にする |
| exe 隣が書込不可（`cache/`・`data/`） | `ProbeWritable` で先に判定。`cache/` は WebView を諦めて案内、`data/` は閲覧のみ（11.1） |
| 同期フォルダ配下で `File.Replace` が失敗 | `AtomicFile` の再試行。失敗は理由を出して編集のまま。PoC で確かめる |
| exe の更新でフォルダごと差し替えて `data/` を消す | README に書く（データは `data/`。更新は exe だけ置き換える）。可搬性を優先した決定 |
| `NativeWebView` と `GridSplitter` の幅変更でちらつく（mac） | `ScrollKeeperScript` を維持。PoC で確かめる。Windows が本番 |
| 2 つ目の起動が `webview2` を共有して衝突 | `--data-dir` が違う起動は、テスト用だけ。PoC で確かめる（要確認） |
| Miharikun のコードをコピーしたときの取り込み漏れ（mermaid・hljs・文書タブ向けの残骸） | P3-2 のテストで `mermaid`・`highlight` が出力に出ないことを確かめる。P5-2 で `File`・`Outline` 系を全部削る |

## 13. 決定事項

### 設計・要件で決めたこと（利用者と確認済み）
1. キャッシュ（WebView2 の作業フォルダ・一時 HTML・ログ）は `<exe>/cache/`。可搬性重視。`data/` とは別。
2. 二重起動はしない（データルートごと）。
3. 空にして保存したら、その日のファイルを削除する。削除前に確認ダイアログを出す。
4. 保存したあとは編集モードに留まる。
5. 前後の月の日（グリッドの端の日）は押せない。
6. 単一の改行は改行として表示する（`UseSoftlineBreakAsHardline`）。
7. 画面イメージから、カーソル行のハイライトと一覧の「今日」タグを外す。
8. ←→ の二重到達は、PoC で数えて決める。

### 計画で決めたこと（仕様にない細部。違えば言ってください）
- 編集補助の関数は「連続した 1 か所の置換」（`EditResult`）を返す。Undo の方式（範囲の置換／全文の置換）を PoC の結果で選べるようにするため。
- Tab で選択が行頭（桁 0）で終わるときは、その最後の行を含めない（一般的なエディタの動き）。
- Enter の続行で、カーソルより後ろの文字は新しい項目の本文にする。マーカーの内側・字下げの中・選択ありは通常の改行。
- Ctrl/Cmd+Enter はチェック項目でない行でも、キーは食う（改行を入れない）。
- 起動時コピーの内容は `DayStore.Write`（LF 統一）を通す。空白だけの元ファイルは飛ばす。
- 日ファイルの年月がフォルダの年月と一致しないものは無視する（`2026/10/2026-11-02.md` など）。
- 曜日の表示は配列（日月火水木金土）。culture に依らない。
- データルートが書けないときは、閲覧はできるが編集に入れない。
- 空白だけのファイルがある日は、カレンダーの点・一覧に出る（ファイルがあるため）。起動時コピーの元からは外れる。
- Mutex は名前付き Mutex を第一候補、mac で不安定ならロックファイル（11.1）。

## 14. 実装するセッションへの注意（必ず読む）

### 14.1 始める前に
- **この計画・要件・画面イメージ（`docs/` の 3 ファイル）が見えることを確かめる**。リポジトリにはまだコミットが無く、`docs/` は未追跡。別の作業ツリー（worktree）や別の環境のセッションには無い。見つからなければ、推測で進めずに利用者に聞く。
- Miharikun を clone して、6 章・11.7 に書いたファイルを実際に読む（`https://github.com/po-oq/Miharikun`）。名前は実在を確かめてから使う。
- 計画と要件に食い違いが無いか確かめる。あれば要件が正。判断がつかなければ聞く。
- **推測で実装しない**。要件に「要検証」「要確認」とあるものは PoC（P0）で確かめる。確かめずに P5・P6 に進まない。
- P0 の結果で変わる所：カレンダーの作り方（P4）、Undo の方式（P2・P5-4）、キー経路（P6-1）、Mutex の方式（P1-6）。
- コミットは頼まれたときだけ。本物のデータ（`<exe>/data`）に触れない（`--data-dir` を使う）。

### 14.2 各フェーズの終わり
- 品質ゲート（1 章）を確かめ、進捗（1 章）を更新する。
- 報告には、変えたこと、テスト結果（件数）、**Windows 実機でできなかった確認**を書く。**次のフェーズに進む前に止まって報告する**。
- P3 の後と P5 の後は、利用者が望めばレビューを挟む（差分と品質ゲートの結果を渡す）。

**状態：レビュー待ち**（別セッションに、この計画のレビューを頼む）。
