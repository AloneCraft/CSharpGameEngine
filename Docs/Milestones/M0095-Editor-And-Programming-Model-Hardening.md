# M0095 — テンプレート・シーンエディター・プログラミングモデルの探索的改善
- 日付: 2026-10-01
- 状態: 進行中

## 目的
テンプレート、編集フロー、シーンエディター、プログラミングアーキテクチャ、エディターアーキテクチャを、
再現 → 最小修正 → 回帰確認の順で改善する。各項目は「修正前に失敗し、修正後に成功する」証拠を残す。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| 0-1 | FeatureStages のシーンが `$schema` に指す `.cge/scene.schema.json` が生成されなかった（stages は独自の `Directory.Build.targets` を持ち、スキーマ書き出しターゲットを取り込んでいなかった） | 完了（6584ad5） |
| 0-2 | `dotnet test CSharpGameEngine.slnx --arch x64` の後、SceneSchema.Cli の出力下に `win-x64\` が残り、SDK パッケージの再帰 glob が二重に取り込んで NU5118 で VSIX ビルドが失敗した | 完了（c23c7f4） |
| 1-1 | ランタイムビューで値の編集（ギズモ、Inspector、JSON テキスト）が画像に反映されない | 完了（b7765c1） |
| 1-2 | シーンタブを複数開き Hierarchy / Inspector にフォーカスがあると、1回の Ctrl+Z / Ctrl+Y / Ctrl+S がタブの数だけ実行されうる | 完了（40e1f11）。キー操作の実機確認は未実施 |
| 1-4 | シーンのタブを閉じても、次に前面へ来たのがシーン以外の文書だとピンが残り、Hierarchy / Inspector が閉じた文書を表示・編集し続ける | 完了（本コミット） |

## 1-1 ランタイムビューへの値編集
- 原因: 変換だけの差分は `SceneHwndHost.TryApplyIncrementalSnapshot` が設計用の描画オブジェクトだけを更新し、`ReplaceScene` を送らなかった。ランタイムビュー（`RenderPreviewRuntimeSession`）はフレームの描画オブジェクトの変換を使わず、文書の JSON からだけ値を受け取る。
- 修正: ランタイムビューのときは増分経路でも編集後の文書を `ReplaceScene` で送る。シーンプレビューの増分経路は変えない。
- 実機確認（CgeAutomation ハイブ、WARP、`UserEcsProject` の `PrimitiveShowcaseBoxScene.json`）: DTE で `Position.R` を 3 → 0 に書き換え、PrintWindow で Scene ビューを比較した。

| DLL | 編集前の2枚の差（ノイズ） | 編集前後の差 | 箱の位置 |
|---|---|---|---|
| 修正前 | 0 px | 864 px（タブの `*` のみ） | 変わらない |
| 修正後 | 0 px | 334,711 px | 移動する |

`SceneHwndHost` は WPF の HwndHost のため単体テストを置けず、確認はこの実機比較で行った。

## 1-2 キーボードの Undo / Redo / Save の持ち主
- 原因（コード経路）: 各シーンタブの `SceneHwndHost` は `BuildWindowCore` から `DestroyWindowCore` まで `CompositionTarget.Rendering` でキーをポーリングする。背面のタブも含め、Hierarchy / Inspector のフォーカスは全タブで「デザイナーにフォーカスあり」と判定され、各タブがグローバルなピン中シーンに対して `TryUndo` を呼んでいた。
- 修正: `SceneDesignerUndoCommandPolicy.IsKeyboardOwner` で、ピン中シーンを表示しているタブ（固定シーンを持たないツールウィンドウはピンに追従するので常に対象）だけがキー入力を処理する。
- テスト: `KeyboardOwner_IsExactlyTheTabShowingThePinnedScene`、`KeyboardOwner_DesignerWithoutAFixedScene_OwnsThePinnedScene`（ポリシーがない修正前はビルドエラー）。
- 未確認: 実際にキーを押す確認は、ユーザーのフォーカスを奪わない方針のため行っていない。手動確認項目「シーンタブ2枚 + Hierarchy フォーカスで Ctrl+Z を1回押し、1段だけ戻る」。

## 1-4 タブを閉じたときのピン解除
- 原因: `SceneJsonEditorPane.OnClose` がデザイナーを破棄するだけで、セッションのピンを外さなかった。次に前面へ来たタブがシーンならそのタブが新しくピンするが、シーン以外の文書ならピンが残る。
- 修正: 閉じるタブがピン中のシーンを表示しているときだけ `ClearPin` する（`SceneDesignerTargetFilter.ReleasesPinOnClose`）。別のタブのピンには触れない。
- テスト: `ClosingATab_ReleasesThePinOnlyWhenItShowsThePinnedScene`。
- 実機確認（CgeAutomation、DTE と UIA のみ。キー入力なし）: `README.md` と `PrimitiveShowcaseBoxScene.json` を開き、シーンを前面にして Hierarchy を表示 → `README.md` を前面にしてシーンのタブを閉じる → UIA で `ShowcaseBox` ノードを探す。

| DLL | シーンを開いている間 | シーンのタブを閉じた後 |
|---|---|---|
| 修正前 | 表示 | 表示されたまま |
| 修正後 | 表示 | 消える（空の状態） |
