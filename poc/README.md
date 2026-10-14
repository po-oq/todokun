# todokun PoC（P0）— 捨てる画面

要件 13 章・計画 2 章 0-1 の確認用。**製品コードではない**。コミットしない（`poc/` は未追跡のまま）。
本物のデータには触れない：データは `--data-dir`（既定は一時フォルダ）に**サンプルを自動生成**する。

## 起動

```bash
cd poc
dotnet run -- --data-dir /tmp/todokun-poc-data --log /tmp/todokun-poc-log.jsonl --cache-dir /tmp/todokun-poc-cache/x
```
Windows（PowerShell）：
```powershell
cd poc
dotnet run -- --data-dir $env:TEMP\todokun-poc-data --log $env:TEMP\todokun-poc-log.jsonl --cache-dir $env:TEMP\todokun-poc-cache\x
```
- `--no-prevent`：ページの `keydown` で `preventDefault` しない（二重到達の確認用）
- `--selftest [--exit-after-selftest]`：`TextBox` の Undo・改行・Tab 横取りの自己テスト
- `--drive [--exit-after-drive] [--shots <dir>]`：**mac 専用**。アプリ内から本物の `NSEvent` を流して ←→・Cmd+E・長押し・クリック・スプリッタ・テーマを確認し、スクリーンショットを撮る
- `--ime [--exit-after-drive]`：**mac 専用**。日本語入力ソースへ切り替えて試す（終了時に元に戻す）。合成入力では変換が始まらなかった（要件 13 章）
- `--mutex-hold <秒> <フォルダ>` / `--mutex-try <フォルダ>`、`--where`：Mutex・`ProcessPath` の確認

画面：左にカレンダーと一覧、右に WebView。ボタン「✎ 編集/ビュー」「ライト/ダーク」「カウンタ記録」「リセット」。
上部の文字に、経路別のカウンタ（`page:` ＝ページ経路、`avalonia:` ＝ウィンドウの Tunnel 経路）が出る。
記録は 1 行 1 JSON（`--log`）。種類：`win.keydown`（Avalonia 経路）・`page.msg`／`page.dbg`（ページ経路）・`edit.keydown.tunnel`／`.bubble`・`snap`・`counters`。

## 実キーボード・実 IME で確かめる手順（mac・Windows 共通）

1. 起動し、ビューモードで WebView の本文をクリックする。
2. **←→ を 1 回ずつ押す** → 日が 1 日ずつ動き、カウンタが `page:ArrowLeft=1` のように 1 つだけ増えること（`avalonia:` が増えたら二重）。**`--no-prevent` でも同じことをして比べる**。
3. **←→ を押し続ける**（長押し）→ 最後の日が表示されること。「カウンタ記録」を押すと `winRepeat`／`pageRepeat` に**キーリピートの間隔（ミリ秒）**が出る（実測値。要件 13 章に書く）。
4. WebView をクリックした後、**Ctrl+E（mac は Cmd+E）** → 編集に入り、そのまま戻らないこと。`win.keydown`／`page.dbg` のどちらに届いたかをログで見る。もう一度押すとビューに戻る。
5. カレンダーの日・一覧の項目をクリックしてから ←→ → 日が動くこと。
6. 編集モード（`TextBox`）で：`Tab`／`Shift+Tab`（字下げ）、`Ctrl/Cmd+Enter`（`- [ ]`⇄`- [x]`）、`Enter`（チェック項目の続行）、`Ctrl/Cmd+Z`（Undo が 1 操作で戻る）、`Esc`（ビューに戻る）。
7. **IME（日本語入力）**：編集モードで、変換中（下線が出ている状態）に次を押し、`edit.keydown.tunnel` の `key`・`ime` を見る。
   - 変換中の `Enter`（確定）→ 項目が増えないこと。確定直後の `Enter` は増えること。
   - 変換中の `Tab` → 字下げされないこと。
   - 変換中の `Esc` → 編集を抜けないこと（変換が取り消されるだけ）。変換していないときの `Esc` は抜ける。
   - ログの `key` が `ImeProcessed` になるか、`Enter`／`Tab`／`Escape` のままかを記録する（`ime:true` なら `Key.ImeProcessed`）。
8. 幅のドラッグ：境界を左右にドラッグし、最小幅（252pt）で止まること。**ドラッグ中に WebView がちらつくか**を目視する。
9. 「ライト/ダーク」→ ビューが再描画されること。スクロール位置が保たれること。

## Windows 実機の確認手順（未確認項目。要件 13 章）

- **WebView2 Runtime**：起動して `webview.adapter` が `WebView2` か（ログ）。未導入なら `WebView1` 等になる。
- **`ProcessPath`**：`dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out` → `out` を PATH に通し、別のフォルダから `todokun-poc --where`。`ProcessPath` が exe のフォルダを指すこと。そこに `data\`・`cache\` を作れること（読み取り専用のフォルダでは作れない＝ `ProbeWritable` が false になること）。
- **起動時間**：上の exe を初回（自己展開あり）と 2 回目で、起動から `nav.completed` までの `t`（ログ）を見る。3 秒以内か。
- **同期フォルダ（OneDrive など）**：`--data-dir` を同期フォルダ配下にして、`TextBox` で編集しながら保存を繰り返す（P1 の `AtomicFile` 実装後に確認でもよい）。
- **`cache/webview2` の共有**：`--data-dir` を変えて 2 つ起動し、両方で WebView が表示されること（`--cache-dir` は同じにする）。
- **手順 2〜9** を Windows で。とくに ←→ の二重到達（`page:` と `avalonia:` が両方増えないか）、`Ctrl+E`、`Esc`・`Tab`・`Enter` と IME。
- `Enter` で入る改行が `\r\n` か：編集モードで 2 行入力し、ログの `edit.textchanged`（`hasCR`）を見る。
