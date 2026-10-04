# M0098 — ThirdPerson の地面のグリッドを UE と同じにする
- 日付: 2026-10-02
- 状態: 完了

## 目的
ユーザーのスクリーンショットで、ThirdPerson テンプレートの地面に崩れた模様（三角形や密な格子）が出ていた。UE の参照画像では、床と壁に 1m 角ほどの灰色のグリッドが均一に並ぶ。UE と同じ見た目にする。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | プロトタイプグリッドをワールド空間の手続き的グリッドで描く（ADR-0075） | 完了 |
| B | M0096 から未解決の「ギズモの R 軸の矢印の先端が掴めない」を調べて直す | 完了 |
| SDK | エンジンとテンプレートの変更（A）に合わせて SDK パッケージを 0.1.12 に上げた | 完了 |

## A. ワールド空間のグリッド
### 観測した事実（Fact）
- 21 アクター（床 1、立方体 12、四分円柱 4、斜面 4）が `MI_PrototypeGrid_Gray` を使う。UE の glTF 書き出しは、これを 4 つの GLB へ焼き付けていた。
- 床の GLB は、`baseColorTexture` が TEXCOORD_1 と `KHR_texture_transform` を使う 1 枚の画像（床・壁・斜面の領域を詰めたもの）。`GltfImporter` は `texCoord` を見ずに TEXCOORD_0 で読んでいた。これが崩れの直接の原因。
- 1024px の焼き付けでは、UV1 で読めても床全体がぼやける。
- UE の材質を実測した（UnrealEditor-Cmd で `tools/UnrealExport/inspect_prototype_grid.py`）:
  - 値: Grid Size 100cm、Sub Grid Number 5、SurfaceColor 0.18、GridColor 0.026042、SubGridColor 0.088542、Roughness 1。Grid=真、ObjectAligned=偽、TopSurfaceColor=偽。
  - 式: `lerp(lerp(Surface, SubGrid, 副線), Grid, 主線)`。線は `MF_ProcGrid` の "Wire" 出力で、`T_GridChecker_A`（512px、R がタイルの 4 辺に幅 5px の線）をワールド座標へ三面投影する。重みは `CheapContrast(|N|, 1) = saturate(3|N| − 1)`。主線のタイルは 1m、副線のタイルは 20cm。

### 実装
- **書き出し** (`unreal_export_common.py`): `prototype_grid_materials` がマニフェストの `materialGrids` に材質のパラメーターを記録する。Grid=偽・ObjectAligned=真・TopSurfaceColor=真の材質は、書き出しが失敗する。`export_lvl_firstperson.py` と `export_lvl_thirdperson.py` が呼ぶ。`add_material_grids_to_manifest.py` は、UE のレベルの書き出しを繰り返さずに既存のマニフェストへ追記する。
- **取り込み** (`CSharpGameEngine.Unreal.SceneImporter worldgrid`、`WorldGridGlbConverter`): 対象の GLB のジオメトリを新しいモデルへ複製し、材質を「SurfaceColor の `baseColorFactor` + `extras.CGE_world_grid`」に作り直す。冪等。両方の `run_export*.cmd` の最後で実行する。ThirdPerson テンプレートの 4 つと FirstPerson（`UserEcsProject`）の 4 つの GLB に適用した。床は 128KB → 7KB。
- **インポーター** (`GltfImporter`): `extras.CGE_world_grid` を `ImportedPbrMaterial.WorldGrid` へ読む。不正な値は警告を出して捨てる。`texCoord` が 0 以外のテクスチャは、警告を出して読み込まない（以前は黙って TEXCOORD_0 で読んでいた）。
- **描画**: `StageMaterialCreateInfo.WorldGrid` → `StageRenderer`（`ObjectCB` を 112 → 160 バイト）。`StageSceneDataBuilder.BakeMaterial` が接続する。`RenderServer` の復旧用アーティファクトにも値を書く。
- **シェーダー**: `tools/ShaderGraphMaintenance/edits/world_grid_2026_10_02.py`。ピクセルごとに、ワールド座標から主線・副線を解析的に計算する（ピクセルの足跡で箱フィルター。ミップの代わり）。グリッド無しの材質の出力は変わらない。ゴールデンの SHA-256 を更新した。

### 除外したこと
- Round / TopDark など、`MI_PrototypeGrid_Gray` 以外のインスタンスの対応（このレベルは使わない。書き出しが失敗する）。
- 描画の材質アーティファクト（版 1）の変更。グリッドは隣に添える（ADR-0075 決定 3）。
- UE の斜めから見たときの異方性フィルターの再現。

### テスト（修正前に失敗することを確認）
- `GltfWorldGridImportTests`（7 件）: texCoord の警告、`CGE_world_grid` の読み取り、不正値 4 種。型が無く、コンパイルから失敗した。
- `WorldGridGlbConverterTests`（4 件）: 変換後の材質、ジオメトリが同じ、冪等、グリッド無しの変種に触らない、`materialGrids` が空なら失敗。
- `StageSceneDataBuilderTests.WorldGridGltf_ReachesTheStageMaterialAlongsideItsSurfaceColor`。
- `WorldGridGpuIntegrationTests`（WARP、4 件）: 床の主線・副線の階層、UV に依存しないこと、グリッド無しなら 1 色、壁の縦横の線。実際の画像を書き出して、床と壁の主線（1m）と副線（20cm）を目で確かめた。
- `ShaderGraphCompilerTests.ProductionStage_ExternalSubgraphsPreserveGoldenHlsl`: ゴールデンのハッシュを更新した。

### 実機の確認
- ThirdPerson テンプレートをビルドし、`CGE_AUTOMATION_*` のフックで WARP で 1 回起動して、スポーン視点を撮影した（`.artifacts/M0098/game/spawn_after.png`）。UE の参照画像と見比べて、壁・床・段差のグリッドの間隔と色が揃っている。参照画像にはキャラクターが無い点だけが違う。

## B. ギズモの R 軸の矢印の先端が掴めない（M0096 から）
### 観測した事実（Fact）
- M0096 の実機確認の記録を調べ直した（当時のスクリプト・ログ・画像を `.artifacts/M0098/gizmo-m0096/` に複製した）。
  - 失敗した実行（m96a-4）は、1078x839 の面の (610,454) を押していた。この画素は、描かれた赤いコーンの内側にある。
  - VSIX の行列（`SceneViewState`、`ScreenPointToRay`）で計算したギズモの中心・X の先端・ScaleX の位置は、画像の画素と一致する（(539,419.5)、(620.3,457.6)、(588.5,442.7)）。View リングは真円で、描画と判定の行列に食い違いはない。
- **ヒットテストは空ではなかった。**
  - 2 回目の移動の後の画像（`m96a-4/2-step2.png`）に、ドラッグの HUD（開始点と X 方向の破線）が出ている。箱とギズモは +X に約 1.55m 動いている。
  - 4 回目の移動の後と確定後の画像は、選択直後と 1 画素も違わない（ドラッグが取り消された）。ActivityLog にエラーは無い。
  - スクリプトが送った座標（610→630）から計算した移動量は 0.05m で、観測の約 1/30 だった。
  - 成功した U 軸のドラッグ（m96a-5）は、計算した 0.17254m が観測（`Position.U` 0.5 → 0.67254454）と一致する。
- 新しいテスト（下記）では、PrimitiveShowcaseBoxScene の既定のフレーミングで、4 つの画面サイズとも描いた矢印の全画素が掴めた。yaw/pitch の全周では、204/130197 画素が何にも当たらなかった。
- 空の画素を、レイと軸の線分の厳密な距離（線分を細かく標本化して求めた）で分類すると、2 つの原因に分かれた。
  - 116 画素: 厳密な距離は判定半径 0.08 以内なのに外れた。原因は `ClosestRaySegment` で、線分側を [0,1] へクランプした後でレイ側を解き直さず、軸に平行なときはレイ側を始点に固定していた。軸と浅い角度の視線で、コーンと先端に出る。
  - 88 画素: 厳密な距離が 0.0800〜0.0835、線分上の位置が 1.35〜1.39 だった。描いたコーンの底の半径 0.085 が判定半径 0.08 より大きく、縁が判定の外にはみ出していた。

### 推測（Hypothesis、実機では確認していない）
- M0096 の失敗は、自動化の副作用と考える。
  - `PostMessage` によるドラッグ中、`SetCapture` のために、スクリプトが送っていない実カーソル位置の `WM_MOUSEMOVE` が届いて、箱が大きく動いた。1.55m は、面の右下の外側のカーソル位置でないと出ない。
  - その後、キャプチャを失って（`CaptureLost` → `GizmoCanceled`）取り消された。
  - 「押下位置のヒットテストが空」という当時の解釈は誤りだった。

### 修正
- `TransformGizmo.ClosestRaySegment`: レイ（t≥0）と線分の最近点を厳密に解く（Ericson の手順）。
  - 線分側をクランプしたらレイ側を解き直し、レイ側を 0 へクランプしたら線分側を解き直す。
  - 平行の判定は、長さに依存しない相対しきい値にした。
  - `TryHitScale` も同じ関数を使うので、Scale の軸も浅い角度で外れなくなった。
- `BuildMoveOverlay`: コーンの底を判定半径 `AxisRadius`（0.08）で描く（以前は 0.085）。描いた矢印が判定のカプセルに収まる。判定半径は広げていない。

### 除外したこと
- Scale の端の立方体と Rotate のリングについて、「描画 ⊆ 判定」を確かめること（今回のテストは Move 矢印だけ）。計算では、Scale の立方体の角（軸から 0.106〜0.13）は、判定のカプセル（半径 0.075）の外にある。
- VS の実機での再確認。

### テスト（修正前に失敗することを確認）
- `GizmoVisibleHitConsistencyTests.EveryDrawnMoveArrowPixel_HitsAHandleThroughTheScenePath`
  - 内容: 描く三角形をアプリと同じ行列で画面に落とし、全画素を `ScreenPointToRay` → `TryHitTransform` で判定する。画面は 4 種（M0096 の 1078x839 を含む）、視点は既定のフレーミングと yaw/pitch の 60 視点。
  - 結果: 修正前 204/130197 → 最近点の修正だけで 88 → 両方の修正で 0。
- `GizmoVisibleHitConsistencyTests.EveryRayThroughTheDrawnMoveArrow_HitsTheMoveGizmo`（解像度に依存しない）
  - 内容: 描いた三角形の内側の点を通るレイを、518 方向（軸に厳密に平行な 6 方向を含む）から投げ、`TryHitMove` に当たることを確かめる。
  - 結果: 修正前 5963/149184 → 最近点の修正だけで 4787（すべてコーンの底の縁）→ 0。
- `TransformGizmoTests.TryHitMove_GrazingRayThroughTheDrawnArrowhead_HitsTheAxis`（軸と 10° のレイ）と `TryHitMove_RayParallelToTheAxisThroughTheArrowhead_HitsTheAxis`（軸に平行なレイ）: 修正前は失敗し、最近点の修正で成功した。
- 周辺: ギズモ・入力・スナップ・編集トランザクションの 140 件が成功した。
- 全テスト: `dotnet test CSharpGameEngine.slnx -c Debug --arch x64` で 19 プロジェクトすべて成功（合格 2,638、スキップ 2、失敗 0。VSExtension.Tests は 1,128 件）。最初の実行は、同時に動いていた別のビルドとのファイルの競合（`deps.json` の使用中、`project.assets.json` の RID 欠落）で 2 プロジェクトがビルドできず、再実行で解消した。変更したファイルに警告は無い。

### 残る不確実性
- 迷い込んだ移動の出どころは推測で、実機では再現していない。
- Scale の端の立方体は、描いた角が掴めない（上記の除外）。
