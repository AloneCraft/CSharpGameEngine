# ADR-0075 — UE のプロトタイプグリッドを、焼き付けではなくワールド空間の手続き的グリッドで再現する
- 状態: Accepted（2026-10-02、ユーザーの依頼: 「ThirdPerson の地面のグリッドを UE と同じにする」）
- 関連: ADR-0017（UE レベル取り込み）、ADR-0067/0068（ThirdPerson テンプレート）、ADR-0069（ライティングの取り込み）、M0098

## 背景
2026-10-02 に観測した事実:
- ThirdPerson テンプレートの床に、崩れた模様（三角形や密な格子）が出ていた。UE の参照画像（`.artifacts/UnrealExport/Lvl_ThirdPerson/ue_reference_spawn_camera.png`）では、床と壁に 1m 角ほどの灰色のグリッドが均一に並ぶ。
- レベルの 21 アクター（床 1、立方体 12、四分円柱 4、斜面 4）が `MI_PrototypeGrid_Gray`（親 `M_PrototypeGrid`）を使う。
- UE の glTF 書き出し（`bake_material_inputs=USE_MESH_DATA`、1024×1024）は、この材質を 4 つの GLB（床・立方体・四分円柱・斜面）へ焼き付けた。
  - 床の GLB は、`baseColorTexture` が TEXCOORD_1（ライトマップ用 UV）と `KHR_texture_transform` を使う 1 枚の画像だった。
  - 立方体・四分円柱・斜面は TEXCOORD_0 だが、同じく領域を詰めた画像だった。
- `GltfImporter` はメッシュに UV を 1 組しか持たず、`texCoord` を見ずに TEXCOORD_0 で読んでいた。UV1 用の画像を UV0 で貼っていたのが崩れの直接の原因。
- UE の材質を実測した（`tools/UnrealExport/inspect_prototype_grid.py`、結果は `.artifacts/UnrealExport/Lvl_ThirdPerson/prototype_grid.json`）:
  - `MI_PrototypeGrid_Gray`: Grid Size 100cm、Sub Grid Number 5、SurfaceColor 0.18、GridColor 0.026042、SubGridColor 0.088542（いずれも線形のグレー）、Roughness 1。スイッチは Grid=真、ObjectAligned=偽（ワールド整列）、TopSurfaceColor=偽。
  - 基本色は `lerp(lerp(SurfaceColor, SubGridColor, 副線), GridColor, 主線)`。
  - 線は `MF_ProcGrid` の出力 "Wire" で、`T_GridChecker_A`（512px。R チャンネルがタイルの 4 辺に幅 5px の線）をワールド座標へ投影して引く。主線のタイルは Grid Size、副線のタイルは Grid Size / Sub Grid Number（20cm）。
  - 三面投影: UE の XZ・YZ・XY 平面の 3 つを、頂点法線から作った重み `CheapContrast(|N|, 1) = saturate(3|N| − 1)` で混ぜる（`CheapContrast` の式も UE から取得した）。
- 1024px の焼き付けでは、床全体を 1 枚に詰めるので解像度が足りない。UE と同じ見た目には、ワールド座標でグリッドを描く必要がある。

## 決定

### 1. グリッドは材質の手続き的な性質として持つ
- glTF では `materials[].extras.CGE_world_grid` に置く。`extras` はアプリケーション固有のデータ用で、拡張として宣言する必要がなく、SharpGLTF.Core だけで読み書きできる。
  ```json
  { "CGE_world_grid": { "gridSizeMeters": 1.0, "subGridDivisions": 5, "wireHalfWidth": 0.009765625, "gridColor": [r,g,b], "subGridColor": [r,g,b] } }
  ```
- 線の間の面の色は、材質の `baseColorFactor`（UE の SurfaceColor）。焼き付けた `baseColorTexture` は付けない。
- `wireHalfWidth` は線の半幅をセルの大きさに対する割合で表した値で、主線と副線で同じ。`T_GridChecker_A` の PNG を書き出して測った 5/512。
- 値が不正（0 以下の間隔、副分割 0、半幅が 0〜0.5 の外、色が 3 要素でない）なら、`CGE_world_grid` を名指しした警告を出し、グリッド無しで読み込む。

### 2. 描画（Stage シェーダー）
- ピクセルごとにワールド座標から線を計算する。UV には依存しない。
- 線のカバレッジは、ピクセルの足跡（`fwidth`）で箱フィルターした解析式にする。足跡がセルより大きくなると、線の平均の被覆率（2 × 半幅）へ滑らかに移る。UE ではテクスチャのミップがこの役目をする。
- UE の座標軸の対応は、UE の +Z = エンジンの +Y、UE の +Y = エンジンの −Z。線は整数位置について対称なので、投影は XY（UE の XZ）、ZY（UE の YZ）、XZ（UE の XY）になり、模様は変わらない。
- 重みは `saturate(3|N| − 1)`。`N` は頂点法線（法線マップを含めない。UE も VertexNormalWS を使う）。
- `ObjectCB` を 112 → 160 バイトに広げ、材質ごとに `WorldGridParams`（間隔、副分割数、半幅。間隔 0 がグリッド無し）、`WorldGridColor`、`WorldSubGridColor` を渡す。スロット幅は 512 バイトなので、他の定数の位置は変わらない。グリッド無しの材質は、出力が 1 ビットも変わらない。
- シェーダーの編集は `tools/ShaderGraphMaintenance/edits/world_grid_2026_10_02.py`（`edit_stage_graph.py` の手順）。

### 3. 描画経路の中の置き場所
- `ImportedPbrMaterial.WorldGrid`（Import）→ `StageSceneDataBuilder.BakeMaterial` → `StageMaterialCreateInfo.WorldGrid`（Windows）→ `StageRenderer`。
- 描画用の材質アーティファクト（`RenderMaterialArtifactDefinition`、版 1）は変えない。これはテクスチャを持つ PBR のレコードで、手続き的なグリッドは別の性質として隣に添える。復旧用のアーティファクトの閉包（`RenderServer.BuildArtifactClosure`）には、グリッドの値を書く（材質が変わればハッシュが変わるようにする）。

### 4. UE 取り込みでの変換
- 書き出し: `unreal_export_common.prototype_grid_materials` が、`M_PrototypeGrid` 系の材質インスタンスのパラメーターをマニフェストの `materialGrids`（材質パスをキーにした辞書）に記録する。対応するのは、Grid=真、ObjectAligned=偽、TopSurfaceColor=偽の構成だけで、それ以外は黙って捨てず、書き出しを失敗させる。
- 取り込み: `CSharpGameEngine.Unreal.SceneImporter worldgrid <manifest.json> <project-directory>`（`WorldGridGlbConverter`）が、`materialGrids` の材質を使う描画資産の GLB を書き換える。
  - ジオメトリ（POSITION / NORMAL / TANGENT / TEXCOORD_0 と添字）だけを新しいモデルへ複製し、材質を作り直す。SharpGLTF.Core にはテクスチャの取り外しと画像の削除が無いため。グリッドは UV を使わないので、焼き付け用の TEXCOORD_1 は落とす。
  - 書き換え済みの GLB に再実行しても、バイト列は同じ（冪等）。
  - 既存の GLB には、UE を再実行せずにこの変換だけを当てられる。マニフェストへの `materialGrids` の追記は `tools/UnrealExport/add_material_grids_to_manifest.py` が行う。
- ThirdPerson テンプレートと FirstPerson（`UserEcsProject`）の両方に適用した。同じ `MI_PrototypeGrid_Gray` を使うため。

### 5. 他の UV 集合のテクスチャ
- `GltfImporter` は、`texCoord` が 0 以外のテクスチャを、警告を出して読み込まない（ファクターだけの材質になる）。黙って TEXCOORD_0 で読むことをやめる。

## 検討した代替案
- **UV1 を読めるようにする**: メッシュに 2 組目の UV を持たせることになる。それでも 1024px の焼き付けでは床全体がぼやけ、UE と同じ見た目にならない。却下。
- **`T_GridChecker_A` をエンジンへ持ち込み、三面投影でサンプルする**: UE と同じ式だが、アセット 1 枚と、ミップ・異方性フィルターの設定を持つことになる。線は解析式で書けるため、テクスチャ無しで済ませた。
- **glTF の拡張として登録する**: SharpGLTF.Core のカスタム拡張の登録が要る。`extras` で足りるので採らなかった。
- **材質アーティファクトの版を上げる**: 影響が大きい（固定サイズのペイロード、既存のテスト）。グリッドは別の性質なので、アーティファクトの外に置いた。

## 帰結
- ThirdPerson テンプレートと FirstPerson の床・壁・段差が、UE と同じ 1m グリッドになった。床の GLB は 128KB から 7KB になった。
- 解析的な線は、UE のミップ付きテクスチャと、斜めから見たときのフィルターが同じではない（画面空間の導関数を使うので、傾いた面では UE より少し太くぼやける）。
- 材質ごとに `ObjectCB` が 48 バイト増える。描画のコストは、グリッド付きの材質のピクセルだけ、線 6 本分の計算が増える。
- Round / TopDark などの別のインスタンスは、使うレベルが現れたときに対応する（今は書き出しが失敗する）。

## 検証
- `GltfWorldGridImportTests`（texCoord の警告、`CGE_world_grid` の読み取り、不正値）。
- `WorldGridGlbConverterTests`（変換後の材質、ジオメトリが同じこと、冪等、グリッド無しの変種に触らないこと）。
- `StageSceneDataBuilderTests.WorldGridGltf_ReachesTheStageMaterialAlongsideItsSurfaceColor`。
- `WorldGridGpuIntegrationTests`（WARP。床の主線・副線の階層、UV に依存しないこと、グリッド無しなら 1 色、壁の縦横の線）。実際の画像でも確認した。
- ThirdPerson ゲームを WARP で起動して撮影し、UE の参照画像と見比べた。

## 関連する機能
- F-L0-002（StageRenderer）、F-L0-005（GltfImporter.ImportScene）、F-L7-005（UE レベル取り込み）、F-L8-032（ThirdPerson テンプレート）
