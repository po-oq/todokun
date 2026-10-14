# メモタブ 設計書（2026-10-14）

> 状態：**レビュー待ち**。承認後に実装計画（`writing-plans`）へ進む。仕様の正は `docs/todo-requirements.md`。この設計が承認されたら、要件定義・計画（md と html を対で）に章を足す。

## 1. 目的と範囲

ウィンドウ上部に「TODO」「メモ」の 2 タブを置く。

- **TODO タブ**：今の画面（カレンダー＋一覧｜右ペイン）をそのまま移す。挙動は変えない。
- **メモタブ**：アプリ全体で 1 つのメモ（`data/memo.md`）。**操作は TODO の右ペインとまったく同じ**（ビュー／編集・保存後も編集に留まる・Esc／Ctrl+E でビューに戻る・Tab／Enter／Ctrl+Enter の編集補助・未保存の確認・ビューの WebView）。
- タブの見た目は Miharikun と同じ（`TabStrip`／`TabStripItem`、FontSize 13・MinHeight 0・Padding 12,6）。

### 利用者と合意した事項
| 項目 | 決定 |
|---|---|
| メモの数 | 1 つだけ（`data/memo.md`） |
| 保存後の動き | TODO に合わせる（編集に留まる） |
| 操作全般 | TODO を踏襲する（Miharikun のメモ画面の流儀ではなく） |
| 起動時のタブ | 毎回 TODO（前回のタブは記憶しない） |
| メモ内のローカルリンク | TODO と同じ（`LocalLinkRule`） |

### 範囲外（YAGNI）
- 外部でのファイル変更の監視（TODO も持たない。Miharikun のメモにはあるが、ここでは入れない）。
- 複数メモ・日付ごとのメモ・タブの記憶・タブのキー操作（Ctrl+Tab など）。
- チェックボックスのクリック切替（要件 16 章で保留中のまま）。

## 2. 方針：TODO の編集処理を共通化する

今の構造：

```
MainViewModel ── Editor: DayEditorSession（日・DayStore・DayIndex に結びつく）
RightPaneView ── Editor を直接見る（日付の見出し・編集補助のキーもここ）
```

変更後：

```
DocumentEditorSession（新・基底）  ビュー/編集・Draft/Base/IsDirty・保存・
  │                              ビューに戻る・確認・メッセージ・Preview の状態機械
  ├─ DayEditorSession（変更）   日・DayStore・DayIndex。保存先と表示文だけを持つ
  └─ MemoEditorSession（新）    MemoStore。保存先と表示文だけを持つ

RightPaneView → DocumentPaneView（改名・一般化）  DataContext = DocumentEditorSession
  ├─ TODO タブ：今のとおり（見出し＝日付）
  └─ メモタブ ：見出し＝「メモ」
```

**既存の 281＋34 件のテストが、共通化後も無修正で通ること**を、共通化の完了条件にする（挙動を変えない）。

### 基底が持つ（今の `DayEditorSession` から移す）
`Mode`／`Draft`／`Base`／`IsConfirming`／`Message`／`_generation`、`IsDirty`・`IsEditing`・`IsViewing`・`ModeText`・`HasMessage`、`BeginEdit`・`SetDraft`・`SaveAsync`・`LeaveEditAsync`・`CanCloseAsync`・`Norm`、コマンド 3 つ、`ContentChanged`、`CanBeginEdit`。

### 派生が決める（抽象メンバー）
| メンバー | Day | Memo |
|---|---|---|
| 内容の読み込み（`DayContent`） | `DayStore.Read(day)` | `MemoStore.Read()` |
| 書く／存在／削除 | `DayStore`＋`DayIndex` の更新 | `MemoStore` |
| 見出し（`HeaderText`） | `2026-10-14 (水)` | `メモ` |
| 空のときの案内 | 「この日のTODOはまだありません。『編集』で書けます」 | 「メモはまだありません。『編集』で書けます」 |
| 読めないときの文言 | 「この日のファイルは読めないため…」 | 「メモのファイルは読めないため…」 |
| 空で保存したときの確認文 | 「この日のファイルを削除します」 | 「メモのファイルを削除します」 |
| プレビューの `PreviewSource` | 既存どおり | `data/memo.md` を基準のフォルダにする |

`IUiServices.ConfirmDeleteAsync()` は文言を引数で受け取る形にする（呼び出し側の `AvaloniaUiServices` とテストの `FakeUiServices` を追従させる）。

## 3. メモの保存

- **`MemoStore`**（Core。`DayStore` と同じ作り）：`Read()`・`Write(text)`・`Exists()`・`Delete()`・`PathOf`。
  - 厳密な UTF-8（不正バイトは `Failed`）、読むとき BOM を外す、書くときは LF・BOM なし・`AtomicFile`。
  - ファイルが無い → `Missing`。読めない → `Failed`（空として編集・保存させない）。
- 保存先は `{DataRoot}/memo.md`。`DataRoot` は日ファイルと同じ（`--data-dir` 指定時はそのフォルダ）。
- 日ファイル用の `DayIndex`（一覧・カレンダーの印）はメモに関係させない。メモは日付ファイルの走査に入らないので、`memo.md` という名前が日ファイルと誤認されないことをテストで確かめる。
- データルートが書けないときは、TODO と同じ理由で「編集に入れない」（`dataWritable` を共有）。

## 4. 画面

### 4.1 ウィンドウ
```
Window
└─ Grid（行: Auto / *）  Margin は今の Root に合わせる
   ├─ 行0 TabStrip：[TODO] [メモ]
   └─ 行1 Grid（両方を載せたまま IsVisible で切替。WebView を壊さない）
        ├─ TodoHost：今の Root（300,6,* の 3 列）をそのまま入れる
        └─ MemoHost：DocumentPaneView（メモ）
```
- タイトルは今の `WindowTitle` のまま。
- 選択中のタブは `MainViewModel.SelectedTab`（`Todo`／`Memo`。既定 `Todo`）。保存しない。

### 4.2 キー
- 受け口は今のウィンドウの Tunnel のまま。**現在のタブの対象にだけ**効かせる。
  - `←` `→`：TODO タブのビューのときだけ（今のとおり）。メモタブでは何もしない（食わない）。
  - `Ctrl/Cmd+E`：現在のタブのセッションに「編集に入る／戻る」を送る（TODO と同じ冪等の規則、編集直後 50ms は戻さない）。メモ用の「編集直後」時刻はメモのホストが持つ。
- ページ（WebView）からのキー：メモのホストは `BeginEdit` だけ受ける（`←→` は無視）。
- 編集欄のキー（Tab／Shift+Tab／Ctrl+Enter／Enter／Ctrl+S／Esc、`Key.ImeProcessed` の除外、範囲置換での Undo）は、`DocumentPaneView` にそのまま移る。TODO とメモで同じコードを通る。

### 4.3 プレビュー
- 現在 `MainViewModel` が `IPreviewHost` を兼ねている。メモ用に `MemoPaneViewModel`（`IPreviewHost` を実装。`PreviewDir`・`IsDark`・`OpenLocalLink` は TODO と共通の実装を使う）を足す。
- **リスク**：`NativeWebView` が 2 つ同時に生きる。Windows 実機の確認項目に足す。mac では実装時に確認する。作れない・動かないときの退路は、メモの WebView だけ「タブを最初に開いたときに作る」（Miharikun と同じ）。

### 4.4 閉じるとき
- 今の `OnClosing` を、「TODO が未保存なら確認 → メモが未保存なら確認」の順に拡張する。どちらかがキャンセル・保存失敗なら閉じない。確認の前に、未保存の側のタブへ切り替えて見せる。

### 4.5 タブを移るとき
- 編集中でもタブは移れる（入力は各セッションが保つ。確認は出さない）。未保存の状態は、閉じるときと TODO 内の日移動のときだけ確認する（今のまま）。

## 5. 見た目
- `AppStyles.axaml` に `TabStripItem`（FontSize 13・MinHeight 0・Padding 12,6）を足す。色は既存のトークン（FluentTheme 準拠）。Miharikun と同じ見た目が出ることを、ライト／ダークの画像で確かめる。

## 6. テスト
| 層 | 内容 |
|---|---|
| Core | `MemoStore`：無い・読める・BOM 付き・不正バイト（`Failed`）・LF 正規化・BOM なしで書く・書けない・消す・`memo.md` が日ファイルとして走査されない |
| Presentation | `DocumentEditorSession` の共通テスト（既存の `DayEditorSession` のテストを、Day／Memo の両方で走らせる）：編集に入る・保存後も編集・未保存の確認（保存／破棄／キャンセル）・空保存の削除確認・読めないとき編集不可・書けないとき編集不可 |
| Presentation | `MainViewModel`：`SelectedTab` の既定・タブ切替で TODO の日移動が無効・`OnWindowKey` が現在のタブへ・閉じる確認の順序 |
| UI（ヘッドレス） | タブ切替で両方が生きる・メモの編集→保存→ビュー・編集補助キーがメモでも効く・閉じるときの確認（両方未保存） |
| 見た目 | `TODOKUN_SHOT_DIR` でライト／ダーク×（TODO／メモ）×（ビュー／編集） |
| 回帰 | 既存の 281＋34 件が無修正で通る |

## 7. フェーズ案（計画では分け方を確定する）
1. **共通化（挙動は変えない）**：`DocumentEditorSession` の抽出・`DocumentPaneView` への改名・`ConfirmDeleteAsync` の文言引数化。既存テスト全通過で完了。
2. **Core**：`MemoStore`（テスト先行）。
3. **ViewModel**：`MemoEditorSession`・`MemoPaneViewModel`・`MainViewModel` のタブ状態・キー経路・閉じる確認。
4. **画面**：タブ行・`MemoHost`・スタイル・見た目の確認。
5. **仕上げ**：要件・計画・README の更新（md と html を対で）、Windows 実機の確認項目（WebView 2 つ・メモの IME）を 13.3 に追加。

各フェーズの終わりで止まって、「やること・結果・未確認」を報告する。

## 8. リスク
| リスク | 対策 |
|---|---|
| 共通化で TODO の挙動が変わる | フェーズ 1 を単独で終え、既存テストを無修正で通す。`_generation` の増やす位置・`ContentChanged` の順は変えない |
| `NativeWebView` を 2 つ作ると不安定 | mac で確認。駄目ならメモの WebView を遅延生成。Windows 実機の確認項目に追加 |
| ウィンドウのキー受けが 2 タブで食い違う | 現在のタブだけに送る（4.2）。`OnWindowKey` のテストで固定 |
| `memo.md` が日ファイルの走査に混ざる | `DayFile` の名前規則（`yyyy-MM-dd.md`）に合わないことをテストで確かめる |
| 空で保存するとメモのファイルが消える（TODO と同じ規則） | 確認ダイアログを出す。利用者の「TODO の動作を踏襲」に従う |
