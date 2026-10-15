# メモの複数化 実装計画

> **連動ルール**：この md と `memo-list-plan.html`（図解）は対。**どちらかを直したら、もう一方も同じ内容に直す。**
> 仕様の正は `docs/memo-list-requirements.md`（メモの複数化）と `docs/todo-requirements.md`（8.3・8.4・8.5・8.6 章。8.5 章は 4-1 で直す）。設計の資料は `docs/memo-list-design.html`（触れる画面イメージ・決めごと）。
> この計画は、実装の分け方・コミット・ファイル構成と、**実装で迷いやすい所の決めごと（7 章）**。
> レビュー（`docs/memo-list-review.html`、2026-10-15）の指摘 #1〜#20 と、決めたいこと Q1〜Q3（推奨どおり）を反映済み。本文中の「（レビュー #n）」が対応する指摘。
> 進め方：Core（テスト先行）→ Presentation（共通化を先に）→ 画面 → 仕上げ。各フェーズの終わりに `dotnet test` と、mac で `--data-dir` を一時フォルダにして動作を確かめ、`docs/todokun-plan.md` の進捗と HANDOFF を更新して報告する。コミットは頼まれたときだけ（ここは区切りの目安）。Issue は無いので `Refs`／`Fixes` は付けない。

## 1. 全体像

```
P1 Core ──▶ P2 Presentation ──▶ P3 画面 ──▶ P4 仕上げ
 3 コミット    5 コミット          4 コミット   1 コミット
 (MemoTitle     (2-1 の共通化は      (一覧・追加・     (README・要件・
  MemoLibrary    挙動不変。           改名・削除・キー)   HANDOFF)
  MemoIndex・移行) 2-5 で最後に開いた
                 メモを覚える)
```

- 新しい依存パッケージ：無い。
- 守る制約：Core は UI・Markdig を参照しない／Presentation は Avalonia を参照しない（`README.md` の構成のとおり）。**Native AOT の警告 0 を保つ**（新しい `{Binding}` には `x:DataType`。`DataTemplate` は自分の `x:DataType`）。TODO タブ・日ファイルの挙動は変えない。
- **フェーズ P2 の最初のコミット（2-1）は挙動を変えない共通化**：`DocumentEditorSession` に、派生が決める口を 2 つ足す——「空で保存したときの扱い」（`DeleteWhenBlank`）と「開いている文書があるか」（`HasDocument`。メモの「メモなし」用。レビュー #2）。TODO の「空にして保存＝そのファイルを削除（確認あり）」が変わらないことが完了条件（品質ゲート）。
- いまのテスト：`Todokun.Tests` 389 件・`Todokun.UiTests` 41 件（`dotnet test Todokun.slnx` で確認済み。**件数は減らさない**。メモの単一前提のテストを直すときは、置き換え後の件数が元以上になるようにする）。

### 品質ゲート（2-1 の完了条件。P2・P3 の終わりでも 1〜4 を見る）
1. `dotnet test Todokun.slnx` が全部通る（389＋41 件を減らさない）。
2. `dotnet build Todokun.slnx` の警告 0・エラー 0。`PublishAot` の警告（IL2026／IL3050）を増やさない。
3. TODO タブの既存の挙動が変わらない：日を開く・←→・編集／保存／Esc／Ctrl+E／Ctrl+S・**空にして保存すると削除の確認が出て、承諾するとファイルが消える**・チェックボックスのクリック・起動時コピー・閉じるときの確認。
4. 確かめ方：自動テスト＋ `dotnet run --project src/Todokun -- --data-dir <一時フォルダ>`（本物のデータに触れない）。

## 2. フェーズ P1：Core（画面なし。テスト先行）

| # | コミット | 内容 | テスト |
|---|---|---|---|
| 1-1 | `MemoTitle`（新規・Core） | タイトルの正規化 `Normalize(string raw)`：7.1 の手順どおり（「取る」系は変わらなくなるまで繰り返す）。戻り値は正規化後の文字列（空なら空）。`ReservedNames`・`Replacements` を持つ | 7.1 の表の全行（NFC・全角置換・制御文字・前後の空白・末尾と先頭の `.`・末尾 `.md`・予約名・100 文字・サロゲートペアを割らない・全部が空白）。**冪等**：`"a.md "`・`"a.md.md"`・`"a.md."`・切った結果の末尾が `.md` になる 103 文字・生成した入力で `Normalize(Normalize(x)) == Normalize(x)`（レビュー #4）。予約名：`CON.txt`→`CON_.txt`・`nul .md`・`COM¹`・`CONIN$`（レビュー #10）。NFD の入力が NFC になる（レビュー #13） |
| 1-2 | `MemoLibrary`・`MemoIndex`（新規・Core） | `MemoLibrary(root)`：`<root>/memos/*.md`（7.2）。`Read(title)`／`Write(title, text)`／`Create(baseTitle)`（空のファイルを作り、重複回避後のタイトルを返す）／`Rename(old, new)`（上書きしない・大文字小文字だけの変更は `File.Move` を直接。7.2）／`Delete(title)`／`UniqueTitle(candidate, except)`（`(n)` を足しても 100 文字以内。レビュー #11）。`MemoIndex`：起動時に走査（タイトル・更新日時・先頭行の抜粋）し、`Add`／`Remove`／`Rename`／`Touch(title, text)`（更新日時と抜粋を直す。一覧に無ければ足す。レビュー #5）で更新、`Changed` を通知。並びは更新が新しい順（同時刻はタイトルの大文字小文字を無視した順）。`Search(query, reader)`（タイトル・本文。本文は読み込みの口を注入） | 走査（`.md` 以外・`.tmp`・`.` 始まり・サブフォルダを無視）／空フォルダ・フォルダが無い／作成の重複（`無題のメモ`→`無題のメモ (2)`、大文字小文字違い、NFC と NFD）／100 文字の同名に `(2)`（100 文字以内）／改名（既存と衝突→`(2)`、大文字小文字だけ（mac で実行）、同じ名前は何もしない、存在しない元、使用中で失敗＝元のまま）／削除（無いものは何もしない）／書き込みは LF・BOM なし・アトミック／抜粋（読めない＝「（読めません）」、4 KB の境目で切れた多バイト文字を読めないと誤らない、BOM 付き）／並び順と `Touch`（抜粋も変わる・無い名前は足す）／検索（タイトル一致・本文一致・大文字小文字・読めない本文は不一致） |
| 1-3 | `MemoMigration`（新規・Core）＋ `AppStartup` に組み込み | 既存の `memo.md` を `memos/` へ引き継ぐ（7.3。判定は「`memo.md` があるか」。レビュー #1）。結果（引き継いだ／何もしない／できなかった理由）を `StartupResult.Migration` に、メモタブに出すお知らせを `StartupResult.MemoNotice` に持たせる（TODO 側の `Notice` とは別。レビュー #12） | 引き継ぎ（内容が 1 バイトも変わらない・BOM や CRLF もそのまま）／`memo.md` が無ければ何もしない／**コピーに失敗 → 起動を続けて `memos/` ができる → 次の起動で引き継ぐ**／`memos/` に同じバイト列のメモがある → コピーせず `memo.md` を消すだけ／`memo.md` を消せない → 次の起動でも二重に作らない／`memos/メモ.md` が既にある（中身は違う）→ `メモ (2)`／一時ファイルだけが残っている → 一覧に出ず、次の起動で引き継ぐ／書けないデータルート |

完了条件：テストが通る。ビルドは警告 0。この時点では画面・既存の `MemoStore`・`MemoEditorSession` は変えない（アプリの見た目は変わらない）。

## 3. フェーズ P2：Presentation

```
前：DocumentEditorSession ──▶ DayEditorSession（日。空保存＝削除）
                           └─ MemoEditorSession（メモ 1 つ。MemoStore。空保存＝削除）

後：DocumentEditorSession（派生が決める口：DeleteWhenBlank・HasDocument）
        ├─ DayEditorSession（変えない。空保存＝削除。文書は常にある）
        └─ MemoEditorSession（いま開いているメモ 1 つ。MemoLibrary。空保存＝空のまま残す。「メモなし」がある）
    MemoListViewModel（一覧・追加・改名・削除・絞り込み・選択の確認・一覧の知らせ）が MemoEditorSession を使う
```

| # | コミット | 内容 | 確認 |
|---|---|---|---|
| 2-1 | 派生が決める口を 2 つ足す（共通化・挙動不変） | `DocumentEditorSession` に `protected virtual bool DeleteWhenBlank => true;` と `protected virtual bool HasDocument => true;` を足す。**`DeleteWhenBlank`**：`SaveCoreAsync` の書く分岐の条件を `!IsBlank(Draft) \|\| !DeleteWhenBlank` にする（`StoreExists` の判定より前。`false` ならファイルが無くても空の本文を書く。レビュー #5）。**`HasDocument`**：`CanBeginEdit`・`BeginEdit`・`SaveCoreAsync`・`ToggleCheck` の先頭で見る。`false` なら何もしない（`BeginEdit` は `Message` に「メモがありません。＋で追加してください」）。値が変わったら派生が `OnPropertyChanged(nameof(CanBeginEdit))` を呼べるよう、基底に `protected void RefreshCanBeginEdit()` を置く。`DayEditorSession` は触らない | 品質ゲート 1〜4。**既存の空保存のテストは 1 行も変えずに通ること**。小さな派生を作るテストを足す：`DeleteWhenBlank=false`（空で保存すると書かれる・確認が出ない・`SaveResult.Saved`・`Content` は空の `Loaded`・**ファイルが無くても空のファイルができる**）、`HasDocument=false`（`BeginEdit` が false で理由が出る・`CanBeginEdit` が false・保存とチェックが何もしない）、既定値では何も変わらない（レビュー #2・#5） |
| 2-2 | `MemoEditorSession` を「いま開いている 1 つ」に | `MemoLibrary` と `MemoIndex` を使う。`Title`（`HeaderText`）・`Open(title)`・`TryOpenAsync(title)`（`DayEditorSession.TryOpenDayAsync` と同じ：確認中なら false → `LeaveEditAsync` → 開く）・`RenameTo(final)`（保存先と見出しを替える。`Draft`／`Base`／`Mode` は触らず、`_generation` も増やさない。`Title`・`HeaderText` を通知し、ビューのときは `ContentChanged(true)` でプレビューを作り直す。レビュー #20）・`Clear()`（開いているメモが無い状態＝見出し「メモなし」・`HasDocument=false`）。`DeleteWhenBlank => false`。`AfterWritten` で `MemoIndex.Touch(title, 本文)`。`Preview`：空（空白だけを含む）の `Loaded` は「本文がありません。『編集』で書けます」、メモなしは「メモがありません。＋で追加」。古い `MemoStore` は使われなくなる（削除は 2-4） | `MemoEditorSessionTests` の単一前提の行を置き換える（空保存＝残る・開く・別メモへの切替の確認・改名しても未保存が残る・改名で見出しが通知される・ビューで改名するとプレビューが作り直される・メモなしで Ctrl+E／保存／チェックが何もしない）。既存の「読めないメモは編集に入れない」「保存の失敗でも `Content` を変えない」はそのまま |
| 2-3 | `MemoListViewModel`（新規） | 一覧の項目（`MemoListItem`：`Title`・`Snippet`・`ModifiedText`・`IsSelected`・`IsRenaming`・各コマンド）、`Query`・`Items`・`Count`・`IsEmpty`・`Notice`（一覧の知らせ。7.6）、`AddAsync`・`BeginRename`・`CommitRename`・`CancelRename`・`DeleteAsync(title)`・`OpenAsync(title)`（7.4 の「要求 → 確認 → 確定」）。確認ダイアログは `IUiServices` 越し（`ConfirmDeleteAsync` を使い回す）。**確認の間は `Editor.IsConfirming` を立てる**：基底に `public Task<T> RunConfirmingAsync<T>(Func<Task<T>> ask)`（`IsConfirming` を `try/finally` で立て下ろし）を足して使う（既存の 2 か所は書き換えない＝挙動不変。レビュー #7）。失敗の出し先は 7.6（一覧の操作は `Notice`、開いているメモの読み書きは `Editor.Message`） | 7.4 の表の行ごと。確認中の再入、キャンセル、保存の失敗で選択を変えない、開いているメモのクリックは何もしない、削除の確認中は `IsConfirming`・確認の後に対象が消えていたら何もしない、改名の確定が 2 回走らない、改名中のクリック・＋・削除・タブ切替は先に確定、＋で作ったメモがすぐ開く、0 件・絞り込みで 0 件、削除後に隣（見えている並び）を選ぶ、開いているメモの改名で未保存が残る、開いていないメモの削除で編集が残る、外で消えたメモを開くと一覧から外れて知らせる、失敗の知らせが次に開いても消えない |
| 2-4 | `MainViewModel` に組み込み・`App` の組み立て・古い `MemoStore` を削除 | `Memo`（`MemoPaneViewModel`）に `List` を持たせる。起動時は一覧の先頭を開く（無ければ「メモなし」。2-5 で「最後に開いていたメモ」にする）。`StartupResult.MemoNotice` を `Memo.Editor.Message` に出す。`AppKey` に `NewMemo`・`FindMemo`・`RenameMemo`・`DeleteMemo` を足し、`OnWindowKey` で振り分ける（7.5。N・F は `fromWebView` でも扱い、短い間の重複を捨てる）。`PageKey` に `NewMemo`・`FindMemo` を足し、`MemoPaneViewModel.OnPageKey` で受ける（ページ側の転送は 3-4）。`AnyDirty`／`CanCloseAsync` は変えない。`App.axaml.cs` で `MemoLibrary`・`MemoIndex` を作って渡す。**UI テストの `MemoTabTests.cs` の単一前提の行もここで直す**（準備で `memos/メモ.md` を作る。レビュー #16） | `MemoTabViewModelTests` を置き換え（起動時の選択・キーの振り分け・TODO タブでは効かない・WebView からの N が 1 回だけ効く・閉じる確認が従来どおり・引き継ぎのお知らせがメモタブに出る）。`MemoTabTests`（UI）が通る。品質ゲート 1〜4 |
| 2-5 | 最後に開いていたメモを覚える（Q1） | 新規 `MemoLastOpened`（Core）：`<cache>/last-memo.txt` にタイトル 1 行（LF・BOM なし・アトミック。`preview/` の掃除の対象外）。開いたら書く（同じなら書かない）。書けない・読めないときは黙って諦める（ログだけ。本文は書かない）。起動時はこれを開き、無い・読めない・一覧に無ければ先頭。改名で書き直す、削除で消す | 起動時に最後のメモが開く／ファイルが無い・壊れている・メモが消えた→先頭／cache が書けなくても動く／改名・削除の追従 |

完了条件：品質ゲート 1〜4。この時点では画面はまだ 1 つのメモの見た目のまま（`MemoPane` は今の `DocumentPaneView`）でよいが、`Memo.Editor` は一覧の選択に追従して動く。

## 4. フェーズ P3：画面

| # | コミット | 内容 | 確認（mac・`--data-dir` を一時フォルダに） |
|---|---|---|---|
| 3-1 | メモタブの左ペイン（一覧・検索・件数・知らせ） | `MainWindow.axaml` のメモタブを `Grid ColumnDefinitions="260,6,*"`（`GridSplitter`・最小幅 200）に。新規 `MemoListView`：ヘッダー（「メモ」・件数・「＋」）・一覧の知らせ（`Notice`。ヘッダーの下に 1 行）・検索欄・項目（タイトル／抜粋＋更新日／ホバーで「✕」）・空状態。項目はボタンの並び（`ListBox` を使わない。既存の `DayItems` と同じ流儀）。右は今の `DocumentPaneView` | 項目クリックで右が切り替わる／開いているメモのクリックで何も起きない／未保存なら確認／選択の見た目／絞り込み／0 件の空状態／知らせの表示／TODO タブと行き来しても選択が残る／ダーク・ライト／最小幅 |
| 3-2 | 追加・タイトル修正 | 「＋」で作ったメモを開き、一覧の先頭でその場で入力状態。項目のダブルクリック（1 回目のクリックで開けたときだけ）・F2・右ペイン上部のタイトルのクリックで修正。確定は Enter・フォーカスが外れたとき（確定は 1 回だけ）、取消は Esc。改名欄の Enter／Esc は編集欄の Esc と同じ受け方（7.5）。置換されたら小さく知らせる（7.6） | 追加→入力→Enter で編集モードで開く／Esc／入力中に別のメモをクリック／禁則文字（`A/B:テスト?`）／同名／全部空白／長い名前／日本語入力（mac の合成入力では確かめにくい＝Windows 実機に回す） |
| 3-3 | 削除 | 「✕」・Delete（mac は `Cmd+⌫` も）→ 確認ダイアログ（『◯◯』を削除します。元に戻せません。未保存の変更があるときは「保存していない変更も失われます」を足す）→ 削除後に隣を選ぶ。0 件になったら空状態。Delete の対象は 7.5 | 開いているメモ／別のメモの削除／Tab で選んだ項目の削除／確認でキャンセル／読み取り専用にして削除（失敗の理由が一覧の知らせに出る） |
| 3-4 | キー操作と UI テスト | `MainWindow.axaml.cs` の Tunnel で `Ctrl/Cmd+N`・`Ctrl/Cmd+F`・F2・Delete（mac は `Cmd+⌫` も）を受ける（7.5）。`KeyForwarderScript`・`PreviewKeyMessage` に `key:NewMemo`・`key:FindMemo` を足す（Windows の WebView2 用。`preventDefault`・押し続けは送らない）。一覧の ↑↓ でフォーカス移動。`Todokun.UiTests` に `MemoListTests` を追加（`AutomationId`：`MemoAdd`・`MemoSearch`・`MemoItem_<title>`・`MemoDelete_<title>`・`MemoRenameBox`・`MemoNotice`） | キーだけで 追加→修正→切替→削除ができる／ビューの本文をクリックした後も `Cmd+N`・`Cmd+F` が 1 回だけ効く／編集中の Delete がメモを消さない／日本語入力中の Enter・Esc を扱わない |

完了条件：`docs/memo-list-requirements.md` の Acceptance Criteria（Gherkin 5 本）を、mac で一通り確かめる。ライト・ダークの両方。品質ゲート 3 をもう一度（TODO タブ・←→・Ctrl+E が変わっていない）。

## 5. フェーズ P4：仕上げ

| # | コミット | 内容 |
|---|---|---|
| 4-1 | 仕上げ | `README.md`（画面・キー・データの場所 `data/memos/`・`memo.md` の引き継ぎ・`.` 始まりのファイルは一覧に出ないこと）、`docs/todo-requirements.md` の 8.5 章・11 章のキー表（Ctrl/Cmd+N・F のページからの転送）・13.3 の Windows 実機の確認項目、`docs/todokun-plan.md` に付録 C（md と html を対で）、`HANDOFF.md`、`docs/memo-list-requirements.md`（Open Questions の決着、「振る舞いの詳細」の正規化の順番を 7.1 にそろえる・先頭の `.` を取る）、`docs/memo-list-design.html`（名前変更はフォーカスが外れたら確定） |

## 6. ファイル構成（追加・変更）

```
src/Todokun.Core/
├─ MemoTitle.cs              1-1 新規：タイトルの正規化
├─ MemoLibrary.cs            1-2 新規：memos/ の読み書き・改名・削除
├─ MemoIndex.cs              1-2 新規：一覧（更新順）・抜粋・検索
├─ MemoMigration.cs          1-3 新規：memo.md の引き継ぎ
├─ AppStartup.cs             1-3 変更：移行を呼ぶ・MemoNotice
├─ MemoLastOpened.cs         2-5 新規：最後に開いたメモ（cache/last-memo.txt）
└─ MemoStore.cs              2-4 削除（単一メモの読み書き）

src/Todokun.Docs/
├─ KeyForwarderScript.cs     3-4 変更：key:NewMemo・key:FindMemo
└─ PreviewKeyMessage.cs      3-4 変更：同上を受ける

src/Todokun.Presentation/
├─ DocumentEditorSession.cs  2-1 変更：DeleteWhenBlank・HasDocument の口／2-3 RunConfirmingAsync
├─ MemoEditorSession.cs      2-2 変更：いま開いている 1 つ・Open・RenameTo・Clear
├─ MemoListViewModel.cs      2-3 新規：一覧・追加・改名・削除・絞り込み・知らせ
├─ MemoPaneViewModel.cs      2-4 変更：List を持つ・PageKey の N・F
├─ PreviewSource.cs          2-4 変更：PageKey に NewMemo・FindMemo
└─ MainViewModel.cs          2-4 変更：起動時の選択・AppKey の振り分け・MemoNotice／2-5 最後に開いたメモ

src/Todokun/
├─ App.axaml.cs              2-4 変更：MemoLibrary・MemoIndex を渡す／2-5 MemoLastOpened
├─ MainWindow.axaml          3-1 変更：メモタブを 左ペイン＋スプリッター＋右ペインに
├─ MainWindow.axaml.cs       3-4 変更：Tunnel に新しいキー
├─ Views/MemoListView.axaml(.cs)  3-1〜3-3 新規：一覧・検索・追加・改名・削除・知らせ
├─ Views/TodoPreview.axaml.cs     3-4 変更：key:NewMemo・key:FindMemo を PageKey に
├─ Views/DocumentPaneView.axaml(.cs) 3-2 変更：メモのときだけ見出しのクリックで改名（IDocumentPane の口）
└─ Themes/AppStyles.axaml    3-1 変更：一覧の項目のスタイル

tests/Todokun.Tests/
├─ MemoTitleTests.cs         1-1 新規
├─ MemoLibraryTests.cs       1-2 新規
├─ MemoIndexTests.cs         1-2 新規
├─ MemoMigrationTests.cs     1-3 新規
├─ DocumentEditorSessionHookTests.cs 2-1 新規：DeleteWhenBlank・HasDocument
├─ MemoStoreTests.cs         2-4 削除（MemoLibraryTests に移す）
├─ MemoEditorSessionTests.cs 2-2 変更
├─ MemoListViewModelTests.cs 2-3 新規
├─ MemoTabViewModelTests.cs  2-4 変更
├─ MemoLastOpenedTests.cs    2-5 新規
└─ DocsTests.cs              3-4 変更：キーの転送

tests/Todokun.UiTests/
├─ MemoTabTests.cs           2-4 変更（memos/ 前提に）
└─ MemoListTests.cs          3-4 新規
```

## 7. 実装の決めごと（迷いやすい所）

### 7.1 タイトルの正規化（`MemoTitle.Normalize`）

入力は拒否しない。確定時に次の順で直す。**「取る」系（手順 3）は、変わらなくなるまで繰り返す**（1 回ずつだと `"a.md "`→`"a.md"`→`"a"` のように、もう一度かけると結果が変わる。レビュー #4）。

| 順 | 入力 | 扱い |
|---|---|---|
| 1 | 全体 | Unicode の NFC にそろえる（mac のファイル名の NFD と同名判定を合わせる。レビュー #13） |
| 2 | `\ / : * ? " < > \|` | 全角に置換：`＼ ／ ： ＊ ？ ” ＜ ＞ ｜` |
| 2 | 制御文字（U+0000〜001F。改行・タブ） | 半角スペース 1 つ |
| 3 | 前後の空白（全角空白を含む） | 取る |
| 3 | 末尾の `.md`（大文字小文字を問わない） | 取る |
| 3 | **先頭の `.`**（計画で足す） | 取る。`.` 始まりは一覧の走査が無視する（7.2）ので、タイトルにできない |
| 3 | 末尾の `.` | 取る（Windows が許さない） |
| 4 | 全部が空になった | 空を返す（呼び出し側が「元のタイトルのまま」にする。新規なら `無題のメモ`） |
| 5 | 100 文字を超える | 100 文字で切る（UTF-16 のサロゲートペアの途中で切らない）。切った後に手順 3 をもう一度（変わらなくなるまで） |
| 6 | 予約名（`CON PRN AUX NUL COM0〜9 LPT0〜9`・上付き数字の `COM¹²³`・`LPT¹²³`・`CONIN$`・`CONOUT$`。大文字小文字を問わない） | 判定は**最初の `.` の前の部分**（後ろの空白を除く）。直し方は**その部分の直後に `_`**（`con`→`con_`、`CON.txt`→`CON_.txt`。末尾に付けると `CON.txt_` のままで予約名が残る。レビュー #10）。予約名は短いので 100 文字を超えない |

- 上の順は固定。冪等であること（テストで確かめる）。要件「振る舞いの詳細」の並び（`.md` を最後に取る）とは順番だけが違う。規則の中身は同じなので、4-1 で要件の記述をこの順にそろえる。
- 同名の判定（`UniqueTitle`）は **大文字小文字を無視**（`StringComparer.OrdinalIgnoreCase`。比べる前に NFC。Windows・mac の既定のファイルシステムに合わせる）。`買い物` があれば `買い物 (2)`、それもあれば `(3)`。**`(n)` を足して 100 文字を超えるときは、本体のほうを切る**（サロゲートペアを割らない。切った後に末尾の空白・`.` を取る。レビュー #11）。**自分自身は衝突させない**（`except` で外す。改名で同じ名前を確定したときに `(2)` が付かない）。
- 入力中の予告（「→ 『◯◯』で保存されます」）は `Normalize` の結果を出すだけ。

### 7.2 保存の場所と一覧の走査（`MemoLibrary`・`MemoIndex`）

- 場所：`<データルート>/memos/<タイトル>.md`。タイトルがファイル名（拡張子なし）。日ファイルの走査（`DayFile.TryParse` は年/月/日の 3 階層だけを受ける）に混ざらない。
- 走査：`memos/` の直下の `*.md` だけ。**サブフォルダ・`.` 始まり・`.md` で終わらない名前（`.md.<guid>.tmp` の一時ファイル、`~RF*.TMP`）は無視**。拡張子は大文字小文字を問わない。
- 1 件ずつ読む：タイトル（ファイル名）・更新日時（`LastWriteTimeUtc`）・抜粋（先頭の 4 KB だけ読み、最初の空でない行から `#` と `- [ ]` などを除いたもの）。抜粋のデコードは**厳しい UTF-8 の `Decoder` に `flush: false` で渡す**（4 KB の境目で切れた多バイト文字は持ち越されるだけで、読めないと誤らない）。BOM は飛ばす。不正なバイトで例外になったら抜粋を「（読めません）」に（レビュー #19）。数百件でも起動が重くならないよう、本文の全読み込みはしない。
- 本文の検索は一覧の走査とは別：検索が打たれたときに、その時点の項目の本文を読む（`Utf8TextFile.Read`）。結果は `(title, 更新日時)` をキーにメモリに持つ。読めない本文は不一致。
- 並び：更新日時の新しい順。**改名では更新日時を変えない**（`File.Move` は変えない）。保存したら `Touch(title, 本文)`（書いた後のファイルの更新日時と、保存した本文から作った抜粋。読み直さない。一覧に無ければ足す。レビュー #5）で先頭へ。並べ直すのは、保存・追加・改名・削除の確定時だけ（入力中には動かさない）。
- 改名：正規化後の名前が元と同じ（大文字小文字・NFC も同じ）なら何もしない。それ以外は `File.Move(old, new)`（上書きなし）。**大文字小文字だけの変更も `File.Move` を直接**使う（.NET は Windows・mac で扱える。1-2 のテストを mac で、Windows 実機で 1 回確かめる）。どうしても一時名が要るなら、一覧に出る名前（`<タイトル> (改名中).md`）にし、途中で止まってもメモが一覧から消えないようにする（`.tmp` にしない。レビュー #13）。
- 外で変わったファイルの監視はしない（TODO と同じ）。開く・削除するときに無ければ、一覧から外して知らせる（7.4）。

### 7.3 既存の `memo.md` の引き継ぎ（`MemoMigration`）

実行は `AppStartup` の中（データルートを書けるか確かめた後。二重起動の確認より後なので、同じデータルートの 2 つ目では走らない）。**判定は「`memo.md` があるか」**で、`memos/` の有無は見ない（レビュー #1。`memos/` の有無で判定すると、コピーに失敗した後に「＋」で `memos/` ができたとき、`memo.md` が二度と引き継がれない）。

| 状況 | 動き |
|---|---|
| `memo.md` が無い | 何もしない |
| `memo.md` がある・`memos/*.md` に**同じバイト列**のファイルがある | コピーはしない（前回コピー済みで、削除に失敗していた）。`memo.md` を消すだけ。消せなければお知らせに「古い memo.md が残っています（消して構いません）」 |
| `memo.md` がある・同じバイト列のファイルが無い | `memos/` の中の一時ファイル（`.md.<guid>.tmp`。走査が無視する名前）に**バイト列のまま**書く → `File.Move`（上書きなし）で `UniqueTitle("メモ")`.md にする → 成功したら `memo.md` を消す（消せなければ上と同じお知らせ） |
| 〃・一時ファイルへの書き込みか `File.Move` に失敗 | 一時ファイルを消す。`memo.md` は消さない。お知らせに理由を出す。次の起動でもう一度試す（その間に「＋」で `memos/` ができても、判定が `memo.md` なので取り残さない） |
| データルートが書けない | 移行しない。お知らせに「書き込めないため、これまでのメモ（memo.md）は書き込めるようになるまで表示されません」（Q3：表示はしない） |

- どの段で止まっても、次の起動で正しい状態に戻る（一時ファイルは一覧に出ず、コピー済みなら中身の比較で二重に作らない）。同じバイト列の比較は、`memo.md` と同じ長さのファイルだけを読んで比べる。
- 二重起動の確認を使えない（`SingleInstanceStatus.Unavailable`）ときに 2 つが同時に引き継いでも、`File.Move` は上書きしないので、後の方は失敗を知らせるだけ（次の起動で同じバイト列を見つけ、`memo.md` を消す）。
- **バイトのまま**にする理由：元の内容を 1 文字も失わない（要件 NFR）。BOM・CRLF・UTF-8 でない中身もそのまま渡す（読めないメモは、新しい一覧でも「読めません」と出て編集に入れない。これまでと同じ）。
- 移行の名前は `メモ`（`MemoTitle` を通す。同名があれば `メモ (2)`）。
- お知らせは `StartupResult.MemoNotice` に入れ、メモタブのバー（`Memo.Editor.Message`）に出す。TODO 側の `Notice`（起動時コピーの失敗・書けない）とは別（レビュー #12）。書けないときは両方に出る。

### 7.4 メモの選択・追加・改名・削除（要求 → 確認 → 確定）

日の選択（`MainViewModel.OpenDayAsync`）と同じ流儀：一覧の見た目（選択）は、確認が通ってから変える。**一覧が出す確認（削除）の間も `Editor.IsConfirming` を立てる**（`RunConfirmingAsync`。キー・閉じる・チェックボックス・ページのキーの守りは全部これを見ている。レビュー #7）。確認の前に一覧の版（操作のたびに増やす数）を取り、確認の後で変わっていたら、または対象のメモが無くなっていたら何もしない。`Open`・`Add`・`BeginRename`・`Delete` の入口でも、確認中なら何もしない。

| 操作 | いまのメモが… | 結果 |
|---|---|---|
| 開いているメモをクリック | 何でも | **何もしない**（選択の見た目だけ直す。編集も確認も変わらない。`OpenDayAsync` の「同じ日」と同じ。レビュー #6） |
| 別のメモをクリック | ビュー・または編集で未変更 | そのまま開く（ビューモード） |
| 〃 | 編集で未保存 | 「保存／破棄／キャンセル」。キャンセル・保存の失敗なら選択は変えない |
| 〃 | 確認ダイアログ中 | 何もしない |
| 開こうとしたメモが無い（外で消えた・改名された） | — | 開かない。一覧から外し、一覧の知らせに「『◯◯』が見つかりません」（レビュー #15） |
| ＋ | ビュー・未変更 | 空のメモを作る（`無題のメモ`・重複回避）→ **すぐそのメモを開く**（ビュー・空）→ その行が入力状態 → 確定・Esc で**編集モードに入る**（レビュー #8） |
| ＋ | 編集で未保存 | 先に「保存／破棄／キャンセル」。通ったら作る。キャンセルなら何も作らない |
| 入力状態で Esc | — | `無題のメモ`のまま残り、編集モードに入る（作った後なので消さない） |
| 入力状態でフォーカスが外れる | — | 確定と同じ（打った文字を失わない） |
| 改名（開いているメモ） | 編集で未保存でも | 保存先と見出しだけが替わる。未保存の入力・モードは変わらない。確認は出さない |
| 改名 | — | `Normalize` → 空なら元のまま・同名なら `(2)`（自分は除く）・変わったら一覧の知らせに出す（7.6） |
| 改名に失敗（外で変わった・使用中・パスが長い） | — | 元の名前のまま。理由を一覧の知らせに出す |
| 削除 | 開いているメモ（編集中でも） | 確認（未保存なら「保存していない変更も失われます」を足す）→ 承諾で消す。未保存の入力は捨てる。**隣（いま見えている並びで次、無ければ前）を開く**。見えている並びが 0 件なら「メモなし」（絞り込み中なら右ペインに「絞り込みに一致するメモがありません」。絞り込みは消さない。レビュー #9） |
| 削除 | 別のメモ | 同じ確認。承諾で消す。開いているメモの編集は変わらない |
| 削除しようとしたメモが既に無い | — | 一覧から外すだけ |
| 削除に失敗 | — | 消えない。理由を一覧の知らせに。一覧は変えない |
| 保存（開いているメモ） | — | 書く（空でも書く）→ `Touch` → 一覧の並びと抜粋が変わるが**選択は動かない** |

**改名の決めごと**（レビュー #8）：
- 改名中の項目は一覧に 1 つだけ。`CommitRename` は冪等：最初に `IsRenaming=false` にしてから処理し、2 回目は何もしない（Enter で確定 → 入力欄が消える → LostFocus、で 2 回来るため）。
- 改名中に `Open`・`Add`・`Delete`・タブ切替が来たら、**先に同期で確定**してから進める。消す対象が改名中の項目なら確定せず取消。
- ダブルクリックの改名は、1 回目のクリックで開けたとき（確認でキャンセルされなかったとき）だけ始める。

- 起動時：**最後に開いていたメモ**（2-5。`cache/last-memo.txt`）を開く。無い・読めない・一覧に無ければ一覧の先頭。0 件なら「メモなし」（右ペインに案内）。要件どおり（Q1）。
- 一覧の「いま開いている」印は、`MemoEditorSession.Title` が正（一覧の項目が自分で持たない）。

### 7.5 キーの経路（ウィンドウの Tunnel）

`MainWindow.OnKeyDownTunnel` に足す。**メモタブが見えているときだけ**（TODO タブでは何もしない）。

| キー | 条件 | 動き |
|---|---|---|
| `Ctrl/Cmd+N` | 見えているタブ＝メモ。**WebView にフォーカスがあっても扱う** | 追加（7.4）。編集の TextBox・検索欄にフォーカスがあっても効く |
| `Ctrl/Cmd+F` | 同上 | 検索欄にフォーカス（全選択） |
| `F2` | 同上＋WebView にフォーカスが無い＋フォーカスが TextBox でない＋名前変更中でない | 名前変更（対象は下の「Delete・F2 の対象」） |
| `Delete`（mac は `Cmd+⌫` も） | 同上（**フォーカスが TextBox でない**＝編集欄・検索欄・名前変更欄の Delete を食わない） | 削除（確認。対象は下） |
| `Esc`（検索欄） | 検索欄にフォーカス | 絞り込みを消す（編集の Esc とは別。編集欄の Esc は今までどおり「ビューに戻る」） |
| `Esc`／`Enter`（名前変更欄） | 名前変更中 | 取消／確定 |

- 既存の `←` `→`・`Ctrl+E` の経路は変えない。メモタブの `←→` は今までどおり何もしない。
- **WebView にフォーカスがあるときの N・F**（レビュー #3）：要件の「本文をクリックした後は常にこの状態」（`todo-requirements.md` 11 章）で効かないと困るので扱う。mac は Cmd+キーがページに届かず Avalonia に 1 回だけ届く（P0）ので、`fromWebView` でもそのまま扱う。Windows の WebView2 は Avalonia にキーが届かないので、ページの `keydown` で `key:NewMemo`・`key:FindMemo` を送る（`preventDefault`。WebView2 の検索バーを開かせない。押し続けは送らない）。Ctrl+N は冪等でない（2 回届くとメモが 2 つできる）ので、**`ModEToggle` と同じく短い間（`DoubleDeliveryWindowMs`）に 2 経路から届いた同じキーは 1 回とみなす**（両 OS で安全）。F は冪等だが同じ扱いにする。
- WebView にフォーカスがあるときの F2・Delete は扱わない（転送も足さない）。一覧の項目をクリックしてから使う。
- **Delete・F2 の対象**（レビュー #17）：フォーカスが一覧の項目にあるときは、その項目（`DataContext` の `MemoListItem`）。それ以外は開いているメモ。Tab で項目に移って押したときに、開いている別のメモが消えないように。
- **名前変更欄・検索欄の Enter／Esc**（レビュー #18）：編集欄の Esc と同じく、欄の KeyDown を **Bubble・`!e.Handled` のときだけ**受け、`Key.ImeProcessed` は扱わない（変換確定の Enter・変換取消の Esc で確定・取消しない）。変換中にフォーカスが外れたときに確定前の文字が入るかは要確認（Windows 実機の項目）。
- 一覧の ↑↓：項目はボタンの並びなので、`KeyboardNavigation.DirectionalNavigation` で矢印キーのフォーカス移動を試す。効かなければ `Tab` ／ `Shift+Tab` ＋ Enter／Space で操作できることを正とする（NFR の「キーボードだけ」は満たせる）。要確認（3-4）。

### 7.6 画面まわりの細部

- 一覧の項目：1 行目＝タイトル（未保存なら先頭に `●`。いま開いている編集中のメモだけ）、2 行目＝抜粋・更新日（「3 時間前」「10/14」。TODO の一覧と同じ曜日の配列流儀で、culture に依らない）。
- 名前変更の入力欄は、項目の中の `TextBox`（`Classes="mono"` ではなく UI のフォント）。入力のたびに `Normalize` の結果が元と違えば、下に小さく「→ 『◯◯』で保存されます」。
- **知らせの出し先を 2 つに分ける**（レビュー #14）：
  - **一覧の知らせ**（`MemoListViewModel.Notice`。一覧のヘッダーの下に 1 行）：一覧で起きたこと——改名の置き換え・同名（「『A/B テスト』→『A／B テスト』にしました」）、改名・削除の失敗、見つからないメモ。次の一覧の操作（開く・追加・改名・削除）で消える。`ShowContent` では消えないので、「改名欄からフォーカスが外れて確定 → 失敗 → 続けてクリックしたメモを開く」でも失敗の理由が残る。文には必ずメモの名前を入れる（「『会議』を削除できませんでした：…」）。
  - **`Editor.Message`**（右ペインのバー）：開いているメモの読み書き——保存の失敗・読めない・書けない・チェックボックス・引き継ぎのお知らせ（7.3）。今までどおり。
- 右ペイン上部のタイトル：メモタブのときだけクリックで名前変更（`DocumentPaneView` の見出しに `Cursor=IBeam` 相当・`Tapped`）。`IDocumentPane` に `bool CanRenameHeader` と `void BeginRenameHeader()` を足し、日は `false`。
- 「＋」の見た目：既存の `primary` クラス。件数は「3 件」。絞り込み中は「2 / 5 件」。
- 検索欄のプレースホルダー：「絞り込み（タイトル・本文）」。大文字小文字は無視。

### 7.7 プレビューと空のメモ

- `Preview`：`Loaded` が空（または空白だけ）なら `PreviewSource.Message("本文がありません。『編集』で書けます")`。**ページ（HTML）を作らない**ので、チェックボックスのクリックの経路は走らない。
- 空のメモに `ToggleCheck` が来ることはない（チェックボックスが無い）が、読み直したら中身があった場合は既存の処理（行の検証）に任せる。メモなし（`HasDocument=false`）では何もしない（2-1）。
- `PreviewSource.Markdown` の `BaseFolder` は `memos/`、`CacheKey` はメモのフルパス（改名で変わる。ビューのときは `RenameTo` が作り直す。古い一時 HTML は 1 日後の掃除で消える）。

## 8. リスクと対策

| リスク | 対策 |
|---|---|
| 共通化（2-1）で TODO の空保存の挙動が変わる | 単独のコミット。既存テストを 1 行も変えずに通す。`DeleteWhenBlank`・`HasDocument` の既定は `true`。品質ゲート 3 に項目を書いてある |
| 移行で `memo.md` の内容を失う・取り残す・二重に作る | 判定は「`memo.md` があるか」。一時ファイルに書いて `File.Move`（上書きなし）→ 成功後にだけ元を消す。同じバイト列があればコピーしない。どの段で止まっても次の起動でやり直せる（7.3）。テストで中身のバイト比較と「失敗 → `memos/` ができる → 次の起動」 |
| 改名の途中で止まる・外のファイルを上書きする | `File.Move`（上書きなし）。同名は先に `UniqueTitle` で逃がし、衝突したら例外で元のまま。大文字小文字だけの変更も `File.Move` を直接（一時名が要るなら一覧に出る名前。7.2。Windows 実機で確認） |
| 一覧の確認の間に、キー・閉じる・チェックボックスが素通りする | 一覧の確認も `Editor.IsConfirming` を立てる（`RunConfirmingAsync`。7.4） |
| Ctrl/Cmd+N が 2 経路から届いてメモが 2 つできる | `ModEToggle` と同じ短い間の重複の除去（7.5）。UI テストと Windows 実機で「1 回だけ」を数える |
| Delete キーで編集中の文字ではなくメモが消える／別のメモが消える | 7.5：フォーカスが `TextBox` なら食わない。対象はフォーカスのある項目。確認ダイアログに名前が出る。UI テストで「編集欄で Delete → メモは残る」「Tab で選んだ項目が消える」を確かめる |
| 日本語入力の変換確定の Enter で、名前が確定してしまう | 欄の KeyDown は Bubble・`!e.Handled`・`Key.ImeProcessed` を除く（編集欄の Esc と同じ流儀）。mac の合成入力では確かめにくい → Windows 実機の確認項目に足す |
| 改名の確定が 2 回走る・他の操作と順番がぶつかる | `CommitRename` は冪等。他の操作の入口で先に確定。＋は作ったらすぐ開く（7.4） |
| 失敗の知らせが見えないまま消える | 一覧の操作の知らせは `Notice`（`ShowContent` で消えない。7.6） |
| 件数が多くて起動・絞り込みが遅い | 走査は先頭 4 KB だけ。検索は押したときに読み、`(title, 更新日時)` で持つ。数百件の生成データでの目視確認を 3-1 に入れる |
| 空のメモが増えて散らかる | 要件で「残す」と決定済み。削除は「✕」で容易。ピン留め・自動削除は入れない |
| 書けないデータルートで、これまでのメモが見えない | 7.3：メモタブのお知らせで理由を出す。`memo.md` は触らない。表示はしない（Q3） |
| 読めない（UTF-8 でない）メモ | 一覧には出る（抜粋は「（読めません）」）。開くと理由が出て編集に入れない（今の規則のまま）。改名・削除はできる |

## 9. 決定事項（確認済み。設計の質問 Q1〜Q5）

1. **Q1 タイトルの持ち方**：ファイル名＝タイトル（`data/memos/<タイトル>.md`）。
2. **Q2 禁則文字**：拒否せず、確定時に全角へ置き換えて知らせる。予約名・長さ・末尾のドット・同名（大文字小文字を無視）も正規化（7.1）。
3. **Q3 空のメモ**：タイトルだけで本文が空のメモは 1 件として残す（自動では消さない）。
4. **Q4 並び**：更新が新しい順。ピン留めは初回は入れない（要るときは `.pins` ファイル方式で後から足す）。
5. **Q5 画面**：上のタブは今のまま。メモタブの左に一覧、右は今のビュー／編集。削除はホバーの「✕」＋ Delete ＋確認。追加は「＋」／`Ctrl+N` でその場でタイトル入力。

レビューで決めたこと（`docs/memo-list-review.html` の Q1〜Q3。推奨どおり）：
- **起動時は最後に開いていたメモを開く**（要件どおり）。`cache/last-memo.txt` にタイトル 1 行で覚える（マシンごと・消してよい場所。データルートを汚さない）。無い・読めない・一覧に無ければ先頭（2-5）。
- **WebView にフォーカスがあっても Ctrl/Cmd+N・F を効かせる**。mac は届いたキーを使い、Windows はページから転送する。重複は短い間で捨てる（7.5）。
- **データルートが書けないときの古いメモは表示しない**（知らせるだけ。7.3）。

計画で決めたこと（仕様にない細部。違えば言ってください）：
- 名前変更の入力でフォーカスが外れたら**確定**（取消は Esc だけ。確定は 1 回だけ。7.4）。デザイン案の HTML は「外れたら取消」だったので、HTML も 4-1 で揃える。
- 名前変更で更新日時は変えない（並びが動かない）。保存したときだけ更新順が動く。
- **先頭の `.` を取る**（要件には末尾の `.` だけ）。`.` 始まりは走査で無視するため、タイトルにできない。取ったときは置き換えと同じく知らせる（「『.env』→『env』にしました」）。
- 正規化の順番は 7.1（「取る」系は変わらなくなるまで繰り返す。要件の記述は 4-1 でそろえる）。
- 引き継ぐメモの名前は `メモ`（要件の Open Question 4。同名があれば `メモ (2)`）。
- 左ペインの幅は TODO タブと別々（Open Question 1）。保存しない（起動のたびに 260）。
- 絞り込み結果のグルーピングはしない（Open Question 2）。削除の取り消し（ゴミ箱）は作らない（Open Question 3。確認ダイアログで防ぐ）。
- 未保存で「＋」を押したときは、別のメモへ移るのと同じ確認を出す（デザイン案の HTML はトーストだけだった）。
- 開いているメモの削除では、未保存の入力を捨てる。確認は削除の 1 回だけで、未保存があるときはその文に「保存していない変更も失われます」を足す。
- 削除後の「隣」は、いま見えている並び（絞り込み中は絞り込みの結果）で選ぶ。

## 10. 実装するセッションへの注意（必ず読む）

### 10.1 始める前に
- **この計画・要件（`docs/memo-list-requirements.md`）・デザイン案（`docs/memo-list-design.html`）・レビュー（`docs/memo-list-review.html`）が見えることを確かめる**。コミットされていないことがある（作成時点では未追跡）。別の worktree で始まったセッションには無い。見つからなければ、推測で進めずに利用者に聞く。
- 計画と仕様（`todo-requirements.md` の 8.5 章は「メモは 1 つ」と書いてある）の食い違いは、**8.5 章は 4-1 で直す**。それまでの間、複数化の要件（`memo-list-requirements.md`）が正。正規化の順番は、この計画の 7.1 が正。
- `HANDOFF.md`・`README.md` を読む。特に：コミット・push・PR は頼まれたときだけ／本物のデータに触れない（`--data-dir` を一時フォルダに）／返答は日本語で「やること・結果・未確認」を短く。
- `DocumentEditorSession` の確認ダイアログまわり（`_generation`・`IsConfirming`・`LeaveEditAsync`）は繊細。**同じ流儀を `MemoListViewModel` に写す**（`MainViewModel.OpenDayAsync`・`DayEditorSession.TryOpenDayAsync` が手本）。一覧の確認も `IsConfirming` を立てる（`RunConfirmingAsync`）。新しい非同期の経路をそれ以外に作らない。
- ヘッドレステストで ViewModel の `Draft` だけを直接変えると、入力欄の遅れた `TextChanged` が巻き戻す（HANDOFF）。UI テストでは入力欄の文字を変える。

### 10.2 各フェーズの終わり
- `dotnet test Todokun.slnx`・`dotnet build`、mac での動作確認（`--data-dir` を一時フォルダに）をして、`docs/todokun-plan.md` の進捗と `HANDOFF.md` を更新する。
- 報告には、変えたこと、テスト結果（件数）、できなかった確認（**Windows 実機はすべて未実施**。4-1 で要件 13.3 に足す項目：全角置換のファイル名・予約名（`CON.txt`・`COM¹`）・大文字小文字だけの改名・名前変更欄の IME（変換中の Enter・Esc・フォーカスが外れたとき）・WebView2 にフォーカスがあるときの Ctrl+N・Ctrl+F（1 回だけ・WebView2 の検索バーが出ない）・長いパス・同期フォルダ配下での改名）を書く。**次のフェーズに進む前に止まって報告する**。
- P2 の後（画面を載せる前）は、利用者が望めばレビューを挟む（差分と品質ゲートの結果を渡す）。

**状態：P1（Core）実装済み（ブランチ `feature/memo-list-core`）。P2 は 2-3 まで実装（2-2 まではコミット済み・2-3 は未コミット）。**
