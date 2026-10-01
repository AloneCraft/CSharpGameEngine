# M0098 — ThirdPerson の地面のグリッドを UE と同じにする
- 日付: 2026-10-02
- 状態: 実装中（A 完了、B 調査前）

## 目的
ユーザーのスクリーンショットで、ThirdPerson テンプレートの地面に崩れた模様（三角形や密な格子）が出ていた。UE の参照画像では、床と壁に 1m 角ほどの灰色のグリッドが均一に並ぶ。UE と同じ見た目にする。

## 進捗
| 項目 | 内容 | 状態 |
|---|---|---|
| A | プロトタイプグリッドをワールド空間の手続き的グリッドで描く（ADR-0075） | 完了 |
| B | M0096 から未解決の「ギズモの R 軸の矢印の先端が掴めない」を調べて直す | 未着手 |
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
