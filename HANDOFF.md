# todokun 引き継ぎメモ（2026-10-14 時点・main へマージ後）

次のセッション（新しいエージェント）が作業を続けるための要約。仕様・計画・差分は下のファイルが正で、ここには重複して書かない。

## いまの状態
- ブランチ：作業ブランチ `feature/implement-p0-p7` は **ローカルの `main` にマージ済み（fast-forward。`main` = `784eb57`）**。作業ブランチは残してある。**push・PR は未実施**（origin への反映は利用者が頼んだときだけ）
- 履歴：`d8b25f8`（P0 の結果と `poc/`）→ `4443842`（P1〜P7 の実装）→ `784eb57`（画面のモダン化）
- P0〜P7 は **mac で実装・確認済み**。**Windows 11 実機の確認はすべて未実施**（要件 15 章の完了条件）
- テスト：`Todokun.Tests` 281 件・`Todokun.UiTests` 34 件、ビルドは警告 0・エラー 0

## 正とする文書（読む順）
1. `docs/todo-requirements.md` — 仕様の正。**13.3 に「Windows 実機で残る確認」12 項目の一覧**がある
2. `docs/todokun-plan.md`（図解 `docs/todokun-plan.html`。**片方を直したら他方も同じ内容に直す**）— 11.6 のキー経路、14 章「実装するセッションへの注意」
3. `poc/README.md` — P0 の捨てる画面と、Windows 実機・実 IME の確認手順
4. `README.md` — 使い方・データの場所・キー操作・開発手順
5. 見た目の資料：`docs/todokun-design.html`

## 次にやること（優先順）
1. **Windows 実機の確認**（利用者が実機で行い、結果を貼ってもらう想定）。手順は `poc/README.md` と要件 13.3。とくに：
   - ←→ の二重到達（ページ経路／Avalonia 経路。`preventDefault` あり・なし）と長押し
   - WebView で `Ctrl+E` → 編集に入って戻らないこと
   - **IME**：変換確定の Enter・変換中の Tab・変換中の Esc（`Key.ImeProcessed` で届くか。mac では合成入力では確認できなかった）
   - Undo・Enter の改行コード（`\r\n` か）・WebView2 Runtime・`PublishSingleFile` の `ProcessPath`・起動 3 秒以内・同期フォルダ配下の保存
2. 結果に応じた修正と、要件 13 章・計画の `[ ]`→`[x]` 更新（md と html を対で）
3. `scripts/publish.ps1` は書いただけで **Windows で未実行**
4. 利用者から画面の見た目の追加要望があれば `src/Todokun/Themes/AppStyles.axaml` で調整（FluentTheme のまま、Miharikun と同じ方針）

## 決定済み（覆さない）
- ←→ の二重到達は **A で一本化**：Tunnel は `e.Source`／フォーカスが `NativeWebView` なら捨てる。C（重複除去）は閾値が取れず不採用（要件 13.2・計画 11.6）
- Mod+E は Avalonia 経路でも受け、ビューモードでは常に「編集に入る」（冪等）。編集直後 50ms 以内の再入力は戻さない（`MainViewModel.DoubleDeliveryWindowMs`）
- Undo は範囲置換（`SelectedText`）。`TextBox.Text` の全文置換は Undo が効かない
- Markdig の拡張名は `UseSoftlineBreakAsHardlineBreak()`（要件・計画は直し済み）
- カレンダーは自前の 7×6。日の選択は「要求 → 確認 → 確定」
- 一覧は `ListBox` ではなくボタンの並び（選択の巻き戻しを避けるため）

## 守ること・注意
- コミット・push・PR は**頼まれたときだけ**。本物のデータに触れない（`--data-dir` を一時フォルダに）
- 仕様と計画が食い違ったら**要件が正**。判断がつかなければ利用者に聞く
- 返答は日本語。利用者は「やること・結果・未確認」を短く分けた報告を好む（フェーズごとに止まって報告してきた）
- Miharikun（コピー元）は `https://github.com/po-oq/Miharikun`（確かめた版 `9a305a6`）。前セッションの clone はセッション専用の一時フォルダにあるので、必要なら clone し直す
- ヘッドレステストで ViewModel の `Draft` だけを直接変えると、入力欄の遅れた `TextChanged` が巻き戻す。テストでは入力欄の文字を変える（実際の入力は必ず入力欄を通る）
- mac のキー注入は OS 全体では不可（アクセシビリティ権限）。`poc/MacEvents.cs` のようにアプリ内から `NSEvent` を流す方式を使った
- 見た目の確認：`TODOKUN_SHOT_DIR=<フォルダ> dotnet test tests/Todokun.UiTests` でライト/ダーク・ビュー/編集の画像が出る

## 未確定・小さな残り
- ダークの WebView の地（`#1E1E1E`）とウィンドウの地（`#1E1F22`）が 1 段違う（気になれば揃える）
- 起動時の「すでに起動しています」の案内画面は目視していない（ロジックはテスト済み）
- 標準 `Calendar` は試していない（自前で決定済み）

## 推奨スキル（次のエージェントが Skill ツールで呼ぶもの）
- `superpowers:systematic-debugging` — Windows 実機で不具合の報告が来たとき（原因を確かめてから直す）
- `superpowers:verification-before-completion` — 「完了」「確認済み」と書く前に、実際にビルド・テストを走らせる
- `superpowers:receiving-code-review` — レビューの指摘を受けたとき（事実を確かめてから採否を決める）
- `superpowers:finishing-a-development-branch` — Windows の確認が済み、origin への push・PR など最終的な取り込みを決める段階（ローカルの main へのマージは済み）
- `code-review`（または `engineering:code-review`）— P3 後・P5 後に利用者が望んだレビュー
- `anthropic-skills:peraichi` — 見た目や設計の変更を、図入りのペライチ HTML で相談したいとき
- `anthropic-skills:impl-plan` — 大きな追加（例：チェックボックスのクリック切替、自動保存。要件 16 章の保留事項）に入るときの計画づくり
