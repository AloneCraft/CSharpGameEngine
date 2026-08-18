# M0044: 書き戻しを全文置換から最小差分へ

## 目的

M0043でスクロール**位置**は正しくなった(ユーザー確認済み)。残っていたのは
**「一瞬一番上までスクロールしてから、その場所に戻る」というちらつき**。

ユーザーから3つの問いを受けた:

1. 全体書き換えだからこうなるのか?
2. 部分書き換えは無理か?
3. 不可能ならエディタの描画を抑制してから最終結果を表示できないか?

## 実測(憶測をやめて先に事実を取った)

M0040〜M0043と4回、症状の原因を推測しては外してきた。今回は**まず測った**。

一時的な計測テストで `SceneTextChangePlanner.Plan(SourceText,SourceText)` の出力を観測した結果:

| ケース | 変更の個数 | 書き換えられる文字数 | ファイル長 |
|---|---|---|---|
| 1リテラルだけ変更 | 1 | **18,531** | 18,531 |
| 離れた2箇所を変更 | 1 | **18,531** | 18,531 |

**単一プロパティの変更でもファイル全体を1個の変更として置換していた。**
`SourceText.GetTextChanges` は、比較対象が `WithChanges` 由来でない無関係な `SourceText` のとき
共通接頭辞・接尾辞の切り詰めを行わず、全体を覆う1個の `TextChange` を返す。
`CommitAsync` は `SourceText.From(Edited.Root.ToFullString(),…)` で毎回まっさらな `SourceText` を
作っていたため、常にこの経路に落ちていた。

つまり**ユーザーの見立て「全体書き換えだからでは?」が正しかった**。バッファ全体が削除・再挿入
されるので、ビューのレイアウト基準点ごと作り直され、表示が一度最上部へ飛んでから戻っていた。

同じ計測で `SyntaxTree.GetChanges` を測ると:

| ケース | 変更の個数 | 書き換えられる文字数 |
|---|---|---|
| 1リテラルだけ変更 | 1 | **10** |
| 離れた2箇所を変更 | **2** | **11** |

往復一致(旧テキストへ変更群を適用した結果が編集後ツリーの全文と一致)も確認できた。

## 実装したこと(3つの問いへの回答)

### Q2 → 部分書き換えは可能。`SyntaxTree.GetChanges` へ切り替えた

`Edited.Root` は `Context.Root`(元ツリーのroot)へ `ReplaceNode` した結果でgreen nodeを共有して
いるため、構文ツリー同士の差分なら実際に変わった領域だけを取り出せる。
`SceneTextChangePlanner` へ `Plan(SyntaxTree,SyntaxTree)` を追加し、`CommitAsync` をこちらへ
切り替えた。副次効果として `Edited.Root.ToFullString()` によるファイル全文の文字列化も消えた
(M0038の遅さ調査で挙げた項目の1つ)。

`SourceText` 版は血縁を作れない呼び出し元(`ComponentIconGlyphMargin`)のために残し、
「この経路は差分が粗い」ことをXMLコメントで明記した。

### Q3 → `ITextEdit` のバッチ適用を試したが、実機退行のため撤回した

VSには描画を止める公開APIが無く、等価な仕組みは `ITextBuffer.CreateEdit()` による一括適用に
あたる。当初これへ変えたが、**実機でギズモ編集が成功扱いのまま何も書き込まれなくなる退行**が
出たため `Buffer.Replace` のループへ戻した。

切り分けの根拠: ツリー差分側は本番と同じ経路(注釈付き `ApplyBatch`・同型兄弟20件)を再現した
一時テストで、変更1個・往復一致・重なり無しを確認できている。一方この適用部はVS SDK依存で
ユニットテストできない唯一の新規コードだった。実績のある形を保つ方を優先する。

差分が最小化された結果、通常の編集では変更が1個になるため、一括適用の利点はもともとほとんど
無かった(1個なら `Buffer.Replace` 1回と等価)。

### Q1 → 「全体書き換えだから」で正しかった

上記の実測のとおり。バッファへ渡す前に差分を取ってはいたが、その差分が全体を覆っていた。

## Evidence

`SceneTextChangePlannerTests.cs` へ実測値を固定するテストを追加(計8件):

- `Plan_SourceTextOverload_RewritesTheWholeFileEvenForOneLiteral` — SourceText版の粗さを
  「span開始0・長さ=ファイル全長」として明示的に固定(この特性が将来変わったら気付ける)
- `Plan_SyntaxTreeOverload_TouchesOnlyTheChangedLiteral` — ツリー版が数値リテラル分しか触らないこと
- `Plan_SyntaxTreeOverload_ReturnsSeparateChangesForDistantEdits` — 離れた2箇所が独立2変更になること
- `Plan_SyntaxTreeOverload_AppliedChangesReproduceTheEditedTreeExactly` — **最重要の契約**。
  差分の出所を変えても適用結果が編集後ツリーの全文と完全一致すること

加えて `CommitAsync` は毎回**検算**する: ツリー差分を `Context.Text` へ適用した結果が
`Edited.Root.ToFullString()` と一致しなければ、従来の全文差分へ自動的に落とす。差分の出所を
変えることはソースを壊しうるため、書き込みが無言で消えるより遅くても確実に反映される方を優先する。

`SceneTextBufferWriter.TryApply` 本体(`ITextBuffer`/`ITextUndoHistory`)はVS SDK依存の
ため既存方針どおりユニットテスト対象外。ソリューション全体1060/1060(既存1054+新規6、
リグレッションなし)。

## 実機確認項目

| # | 確認対象 |
|---|---|
| 1 | 1ノードをドラッグして確定したとき、コードエディタが一瞬でも最上部へ飛ばない(今回の主目的) |
| 2 | 複数ノードを選択して同時にドラッグしても、エディタがちらつかない |
| 3 | 編集後のC#ソースの内容が従来どおり正しい(差分の出所を変えたことの回帰確認) |
| 4 | Ctrl+Zで1回の編集がまとめて元に戻る(1操作=1 Undo単位の回帰確認) |
| 5 | コンポーネントアイコンの設定(`ComponentIconGlyphMargin`)が従来どおり動く(回帰確認) |
