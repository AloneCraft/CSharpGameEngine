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
| 1-1 | ランタイムビューで値の編集（ギズモ、Inspector、JSON テキスト）が画像に反映されない | 完了（本コミット） |

## 1-1 ランタイムビューへの値編集
- 原因: 変換だけの差分は `SceneHwndHost.TryApplyIncrementalSnapshot` が設計用の描画オブジェクトだけを更新し、`ReplaceScene` を送らなかった。ランタイムビュー（`RenderPreviewRuntimeSession`）はフレームの描画オブジェクトの変換を使わず、文書の JSON からだけ値を受け取る。
- 修正: ランタイムビューのときは増分経路でも編集後の文書を `ReplaceScene` で送る。シーンプレビューの増分経路は変えない。
- 実機確認（CgeAutomation ハイブ、WARP、`UserEcsProject` の `PrimitiveShowcaseBoxScene.json`）: DTE で `Position.R` を 3 → 0 に書き換え、PrintWindow で Scene ビューを比較した。

| DLL | 編集前の2枚の差（ノイズ） | 編集前後の差 | 箱の位置 |
|---|---|---|---|
| 修正前 | 0 px | 864 px（タブの `*` のみ） | 変わらない |
| 修正後 | 0 px | 334,711 px | 移動する |

`SceneHwndHost` は WPF の HwndHost のため単体テストを置けず、確認はこの実機比較で行った。
