# M0115 — 描画の L4 化と Forward / Forward+(URP 方式)
- 日付: 2026-10-25
- 状態: 計画(2026-10-25 に ADR-0081・ADR-0083 を Accepted。M0114 の完了後に始める)
- 決定: [ADR-0081](../Decisions/ADR-0081-Urp-Render-Paths.md)、[ADR-0083](../Decisions/ADR-0083-Rendering-And-Physics-Layers.md) §1
- ABI: `stage-v15`

## 目的
パス構成・光源の振り分け・深度の方針を L4/L5 の移植可能なコードへ移し、L0 は D3D12 の実行だけにする。Forward+ を URP 方式(タイル × 対数殻の Z-bin)にし、Forward(物体ごとに最大 8 灯)を足す。

## 段階
1. **境界を動かす(画素は変えない。対数の 7 SHA と既存の WARP 画素の期待値を 1 つも書き換えない)**: `src/CSharpGameEngine.Rendering` に `View/`・`Pipeline/`・`Backend/`・`Server/` を置く。値型、`StageGpuLayouts`(DXC の反射で検査)、CPU の光源・影・フレーム計画、`StagePipelinePlanner`(今の `RenderFrame` の順)と `StagePipelineDesc`、ポート `IStageRenderBackend`、`RenderServer`/`RenderFrameState` の L4 への移動と `D3D12RenderServer.Create`、`Camera` → `RenderCamera`、シーンの Component `Camera`/`PrimitiveMesh` を Runtime.Windows の共有ソースへ(名前空間は不変)、`EngineLayerSpec` の新しい規則、`EveryStageInputPathExists`、`GeneratedEditorPreviewBridgeEmitter` と InputPaths の付け替え。TestHost の `frame-plan`。
2. **Forward+ を URP 方式にする**: `ClusterLightGrid`(タイルは 8 px から倍々、Z-bin は深度の値 `d` で切る、t6 の `ClusterWords` 1 本)。`ClusterLightGridTests`(総当たりのオラクルで取りこぼし 0)。ヒートマップ。
3. **Forward と選ぶ入口**: `ForwardObjectLightAssignment`、`RenderPath`、`Level.RenderPath`、`CGE_RENDER_PATH`。Deferred 系の指定は型付きのエラー。
4. **カタログ・文書・版**: Stage `rendering.render-paths`、F-L0-011・F-L4-020 を Implementing に。

## 完了条件
- Forward+ の 7 SHA が M0114 と同じ。Forward と Forward+ がビット一致(各物体の局所光が 8 以下)。
- 層の試験に合格。`ShaderArtifactHotReloadTests`(PSO キャッシュが成果物の差し替えで捨てられる)。
- 性能の予算を満たす。

## 範囲外
Deferred 系(M0116)、触らない L0 の CPU アルゴリズム(`AreaLightLtc`・`LumenSurfaceCacheLayout` など)、入力・音声サーバー、実行時の glTF/WIC。

## 機能 ID
F-L0-011、F-L4-020、F-L0-002、F-L4-002、F-L4-004、F-L4-007
