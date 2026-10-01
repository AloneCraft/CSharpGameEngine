# M0096 — ギズモの反映・上下反転の既定・RunsAfter 順序・角度の turns・プレビュー統合・層の修正
- 日付: 2026-10-01
- 状態: 進行中

## 目的
M0095 の後にユーザーから受けた不具合報告と設計判断を、M0095 と同じ手順（修正前に失敗するテスト → 最小修正 → 全テスト → 文書の整合）で実装する。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | ランタイムビューで、選択したギズモを操作しても確定まで画面のオブジェクトが動かない | 完了（82bf9b0、SDK 0.1.5 は d3b6c4e） |
| B | Options の上下反転（Look / Orbit）: チェックを入れたときの今の挙動を、チェックなしの既定にする | 完了（8589960） |
| C | 実行時に使われない `VsixStrings.en-US.resx`、obj だけ残った旧ディレクトリ、`UserEcsProject/Generated/` の削除 | 完了（本コミット） |
| D | 整数 `[SystemOrder]` を廃止し `[RunsAfter]` の相対順序に一本化（同着は完全修飾名順） | 未着手 |
| E | シーン JSON の角度を turns（1 = 1 回転）で保存し、Inspector は度で表示 | 未着手 |
| F | Scene ビューの 2 プロセス（ランタイムビューと編集ワールド）の統合 | 未着手 |
| G | Runtime.Physics / Character の Windows 依存の解消と、`Physics.Cli` の UserEcsProject 依存の除去 | 未着手 |

## A. ギズモ操作をランタイムビューに反映
- 原因: ドラッグ中、VSIX は動かしたオブジェクトの行列を `RenderPreviewFrame.Objects[].World` で送るが、ランタイムビュー（`RenderPreviewRuntimeSession.UpdateFrame`）は `Objects` を選択表示にしか使わない。ゲームのワールドは文書の値で描くので、テキストに書き込まれる確定までオブジェクトは動かず、ギズモだけが動いていた。
- 修正:
  - `RenderPreviewFrame.TransformOverrides`（ノードパスと親相対の Position / Rotation / Scale、RUF）を追加した。省略可能なので、プロトコルの版は 6 のまま。
  - VSIX はドラッグ中、ドラッグの根ノードの値を確定と同じ計算（`TryCalculateLocal(PreviewWorld, ParentWorld)`）で求めて毎フレーム送る。子孫は親相対なので送らない。
  - ホストは対象ノードの `Transform` の Position / Rotation / Scale だけを差し替え、Frame など他のメンバーは文書の値のまま、文書の編集と同じ反射の経路で設定する。
  - キャンセルではもとの文書を送り直す。
- 保険: ランタイムビューへ送る JSON を、Roslyn の解ではなくテキストのコンテナの現在値（開いている文書ではエディタのバッファ）から読むようにした。差分が空の解析結果でも、最後に送った JSON と本文が違えば送り直す。モデルの差分は Designer が扱う値しか見ないため。
- テスト:
  - `RenderPreviewRuntimeSessionTests.DragTransformOverrides_MoveTheObjectUntilTheDocumentIsResent`: 上書きで画像が変わり、キャンセルで送り直す元の文書で戻り、同じ値を確定した文書の画像と一致する。ホストの適用を外すと失敗する。
  - `RenderPreviewSceneBuilderTests.DragOverrides_AreParentRelativeAndKeyedByNodePath`: 回転した親の下でも、上書きは親相対の値になる。
  - `LogicalQuaternionConventionTests`: Designer の `System.Numerics` の回転（ヨー = U、ピッチ = R、ロール = F）と、エンジンの `EulerRuf` が同じ回転になる。
- 実機（CgeAutomation ハイブ、`PostMessage` によるマウス操作でフォーカスは奪わない、`PrimitiveShowcaseBoxScene`）:
  - U 軸の矢印を 70 px 上へドラッグした。ボタンを押したままの画像で箱・影・ギズモが一緒に上へ動いた（選択直後との差 129,698 px）。離した後の画像との差は HUD とハンドルのハイライトの 8,590 px だった。バッファは `Position.U` 0.5 → 0.6725。
  - R 軸のスケールハンドルのドラッグでも、ドラッグ中に箱が伸び、確定後の画像との差は 630 px。
  - 文書は「保存しない」で閉じ、ファイルは変わっていないことを確認した。
- 未解決の観測: R 軸の矢印の先端（画面上で赤く描かれた画素）を押しても掴めなかった（押下位置のヒットテストが空）。今回の修正とは別の現象として切り分けを続ける。

## B. 上下反転の既定
- 変更: Look（RMB）と Orbit（Alt+LMB）の垂直の基準を反転した。チェックなしで、下ドラッグで `Pitch` が下がる（下を向く）。それまでチェックを入れたときの向き。
- ピクセル完全一致（クリックした点がカーソルに追従する）は「右ドラッグで Yaw 増・下ドラッグで Pitch 増」でしか成り立たない。基準の垂直が逆になったので、条件を `InvertLookX && InvertLookY` に変えた。
- 設定の移行は版 3。値は基準に対する反転のまま変換しない。チェックの無い利用者は新しい既定の向きに、チェックを入れていた利用者はそれまでの既定の向きになる。版 1→2 の変換は版 1 のときだけ行う。
- テスト: 基準の向き（Look と Orbit）とピクセル一致の条件は修正前に失敗、修正後に成功する。版 2 の設定は変換されないことを固定した。
- Options の文言は「上下回転を反転する」で、向きを書いていないので変更しない。

## C. 削除
- `VsixStrings.en-US.resx`: 中身は日本語で、実行時には使われていなかった。アセンブリの `NeutralLanguage` が en-US なので、`ResourceManager` は en-US を要求されても本体の既定リソース（`VsixStrings.resx`、英語）を返し、en-US のサテライトを参照しない。参照箇所もテストも無い。
- obj だけ残った旧ディレクトリ: 承認時は 16 個と数えていたが、同じ基準（追跡ファイルなし、中身は 2026-08-21 の NuGet 復元の中間ファイル 5 個だけ、gitignore 対象）で 24 個あった。数え違いと判断し、24 個とも削除した。
  - `src/` の `Compiler.Hlsl`、`Geometry`、`Graphics.D3D12`、`Import.Fbx`、`Import.Gltf`、`Platform.Windows`、`Scene`、`Spatial.Abstractions`、`Spatial.Blender`、`Spatial.Fbx`、`Spatial.Gltf`、`Spatial.Unity`、`Spatial.Unreal`
  - `tests/` の `Compiler.Hlsl.Tests`、`Geometry.Tests`、`Graphics.D3D12.Tests`、`Import.Fbx.Tests`、`Import.Gltf.Tests`、`Scene.Tests`、`SceneAuthoring.Cli.Tests`、`Spatial.Tests`、`UserGameProject.Tests`
  - `tools/CSharpGameEngine.SceneAuthoring.Cli`、`UserGameProject/`
- `UserEcsProject/Generated/`: 2026-08-21 の生成物（存在しない `TransformPrecisionGenerator` の出力を含む）。プロジェクトはここへ出力していない。
- 文書: `Architecture.md` が `UserGameProject/` を実在の `.csproj`、`SceneAuthoring.Cli` を現役のブリッジとして書いていたので、現在のもの（`UserEcsProject/`、ThirdPerson テンプレート、現在の CLI ブリッジ）に直した。`UserEcsProject/README.md` の `Generated/` への参照も直した。README の他の部分は以前から古い（別タスクとして提案）。
- 全テスト成功。ビルド後も削除したディレクトリは再作成されない。
