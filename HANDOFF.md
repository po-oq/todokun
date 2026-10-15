# 引き継ぎ：メモの複数化（計画レビュー → 実装の手前）

作成：2026-10-15。前のセッションの要約。中身の詳細は下の文書にあるので、ここでは繰り返さない。

## いまの状態

- ブランチ `feature/memo-list-core`（`main` の `3b83511` から分岐）。**P1（Core）の 3 つ（1-1〜1-3）を実装した。コミットしていない**（頼まれたときだけ）。`main` は push していない。
- 追加：`MemoTitle`・`MemoLibrary`・`MemoIndex`・`MemoMigration`（いずれも `src/Todokun.Core/`）とそのテスト。`AppStartup`／`StartupResult` に `Migration`・`MemoNotice` を足した（起動時に `memo.md` を `memos/メモ.md` へ引き継ぐ）。画面・`MemoStore`・`MemoEditorSession` は未変更。
- テスト：`dotnet test Todokun.slnx` が 523＋41 件で全部通る。`dotnet build` は警告 0。
- 未確認：画面でのメモの一覧（P3 で載せる）。Windows 実機はすべて未実施。
- P1（3 コミット）・P2（5 コミット）・P3（3-1 `63bdc61`・3-2 `e9753f0`・3-3 `157c6b2`・3-4 `1ddacbc`）はコミット済み。**P4 の 4-1（文書の仕上げ）を実施した（未コミット）**：`README.md`（メモタブの画面・キー・`data/memos/`・`memo.md` の引き継ぎ・`cache/last-memo.txt`）、`docs/todo-requirements.md`（8.5 章を複数メモに・11 章のキー表と N・F の転送・13.3 の Windows 実機の確認項目）、`docs/memo-list-requirements.md`（正規化の順番を計画 7.1 にそろえた・Open Questions の決着・名前の入力でフォーカスが外れたら確定）、`docs/memo-list-design.html`（フォーカスが外れたら確定）、`docs/todokun-plan.md`／`.html` に付録 C。**実装と文書はすべて揃った。残りは確認だけ**：mac で受け入れ条件（Gherkin 5 本）の目視（ライト・ダーク）と、Windows 実機（`docs/todo-requirements.md` 13.3）。テストは 614＋67 件、警告 0。

## 読むもの（正はこちら）

| 文書 | 役割 |
|---|---|
| `docs/memo-list-requirements.md` | 要件（正） |
| `docs/memo-list-plan.md`／`.html` | 実装計画（正。**md と html は対で直す**。10 章「実装するセッションへの注意」を必ず読む） |
| `docs/memo-list-review.html` | 計画レビューの根拠（file:line つき）。計画の「（レビュー #n）」の出どころ |
| `docs/memo-list-design.html` | 触れる画面イメージ（名前変更のフォーカスが外れたときの扱いだけ計画と違う。4-1 で直す） |
| `docs/todo-requirements.md` 8.3〜8.6・11 章 | 既存の仕様（8.5 は「メモは 1 つ」のまま。4-1 で直す） |
| `HANDOFF.md`・`README.md` | プロジェクトの決まり（コミットは頼まれたときだけ、`--data-dir` を一時フォルダに、など） |

## 決まったこと（要点だけ。詳細は計画 9 章）

- Q1：起動時は**最後に開いていたメモ**（`cache/last-memo.txt`。2-5 で実装）。
- Q2：WebView にフォーカスがあっても Ctrl/Cmd+N・F を効かせる（mac は Tunnel、Windows はページから転送、重複は短い間で除去）。
- Q3：データルートが書けないときの古い `memo.md` は表示しない（知らせるだけ）。

## 次にやること

1. 4-1 をコミットするか利用者に確認する（頼まれたときだけコミット）。その後、ブランチ `feature/memo-list-core` を `main` に取り込むか（push・PR は頼まれたときだけ）。
2. （済）mac での引き継ぎの起動確認。
3. 要件の受け入れ条件（Gherkin 5 本）を mac で一通り確かめる（ライト・ダーク。品質ゲート 3：TODO タブ・←→・Ctrl+E が変わっていない）。Windows 実機の確認は 13.3 の項目。
4. フェーズの終わりごとに `dotnet test`・`dotnet build`（警告 0）・進捗と HANDOFF の更新 → 止まって報告。

## 実装で決めたこと（計画からの補足）

- `MemoTitle.Truncate(s, max)` を公開（`(n)` を足すときの切り詰めと抜粋で使う）。
- 予約名に `_` を足して 100 文字を超える場合（`CON.` ＋長い拡張子）は、もう一度切る。計画の「予約名は短いので 100 文字を超えない」は誤りだった。
- 一時ファイル名は `memos/.md.<guid>.tmp`（`.` 始まりなので一覧に出ない）。

## 注意

- 返答は日本語。コミット・push・PR は頼まれたときだけ。`main` に直接コミットしない（ブランチを切る）。
- Windows 実機の確認はすべて未実施（項目は計画 10.2）。

## 使うとよいスキル（Skill ツールで呼ぶ）

- `superpowers:executing-plans`：計画（`docs/memo-list-plan.md`）を自分で順に実装するとき。
- `superpowers:test-driven-development`：P1 は「テスト先行」。各コミットの前に。
- `superpowers:verification-before-completion`：フェーズの完了を報告する前（テスト件数・警告 0 の確認）。
- `superpowers:requesting-code-review`：P2 の後（画面を載せる前）にレビューを挟むとき。
- `anthropic-skills:peraichi`：レビュー結果や設計の変更を図つき HTML にまとめるとき（`docs/memo-list-review.html` と同じ形式）。
- `anthropic-skills:impl-plan`：計画そのものを大きく組み直す必要が出たとき（md と html の対を保つ）。
