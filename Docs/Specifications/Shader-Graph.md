# Shader Graph specification

- Status: Implemented with partial conformance
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0058](../Decisions/ADR-0058-Visual-Shader-Graph-And-Artifact-Runtime.md), [ADR-0059](../Decisions/ADR-0059-External-Shader-Subgraph-Composition.md), [ADR-0060](../Decisions/ADR-0060-Structured-Shader-Regions.md)
- Target layers: L2, L6, L7, L8

## Canonical assets

.cgeshadergraph is a root graph and .cgesubgraph is a reusable visual subgraph. Both are canonical UTF-8 JSON. Schema v1 stores stable GUID identity, target ABI, entry metadata, subgraphs, typed ports, nodes, edges, regions, external dependency IDs/paths, and editor layout.

Canonical serialization orders identities and argument names; it leaves out nulls, so a canonical file never holds one. A JSON null where the graph needs a value (the target, a list, a list item, a node type, an argument name or value, an edge port, a dependency path) is malformed JSON: loading throws a `JsonException` naming its path, as for any other malformed JSON. Semantic hashing removes node positions and region layout. Connection types must match exactly; conversions are explicit nodes. Unknown JSON node data survives load/save and validator diagnostics identify unsupported node types. A subgraph or node identity that appears twice (a node copied by hand in the JSON, a merged file) is a validation error (`DuplicateIdentity`), so generation reports it instead of failing.

The fixed root target is Stage (serialized value 0); removed values 1 and 2 are rejected as UnknownTarget. Stage has 14 entries: VSMain, PSMain, VSShadowMain, FullscreenVS, PostProcessPS, PreviewPSMain, PreviewCleanPSMain, OverlayVSMain, OverlayPSMain, ScreenVSMain, ScreenPSMain, ObserverVSMain, SurfacePrepassPSMain and LumenCardPSMain. The graph owns native colour, physical distance and conservative logarithmic depth. The engine-owned suffix supplies presentation, observer, surface and card entries; the old radial wrapper entries are removed. The stage-v14 contract keeps FrameCB at 960 bytes, repurposing the unused shadow matrix and atlas fields for InverseViewProjection and ViewportSize. DepthEncoding0/1 carry validated L4 RenderViewDepth packets; each object carries its ConservativeRasterDepth (in ObjectCB until stage-v16, in the instance table since stage-v17). Material sampler s0 is anisotropic 8/wrap, t16/t17 carry Lumen diffuse/reflections, and LumenParams selects them. The unused Model/Triangle renderers, ABI producers and root templates have been removed in M0119.

The current Stage ABI is `stage-v18` (M0129, see the GPU scene section below). `stage-v16` (M0115/M0125) appended four virtual directional clipmap matrices, page-table parameters and `DirectionalSoftShadowParams` to FrameCB, with t18 for the physical D32 pages and t19 for the per-frame uint page table. The last float4 holds tan(half sun diameter), maximum soft radius in metres, enabled and reserved. CSM and Virtual share PCSS sampling, receiver-plane correction and world-space bias when enabled. VSMain applies inverse-transpose normals under non-uniform scale; all presentation and Lumen surface entries share it. Optional virtual sampling falls back to the existing cascades. Runtime still consumes only offline artifacts. The reproducible edit is `tools/ShaderGraphMaintenance/edits/directional_soft_shadows_2026_10_26.py` via `edit_stage_graph.py`.

Dependencies resolve relative to their owning document and may be recursive. Resolution validates document identity and rejects missing files, recursion, node collisions, and top-level declaration-slot collisions. Production Stage owns Depth, PBR, Shadow, PCSS, and ACES as external typed-syntax Subgraphs whose original declaration slots are preserved during composition.

## Validation and lowering

The built-in catalog contains typed constant, arithmetic, vector, utility, output, and Subgraph Call nodes. The production migration additionally represents every parser AST node and typed child relation as visual nodes; no raw HLSL source, code, or token payload is stored.

Validation rejects missing required ports, type mismatch, unknown ports, graph cycles, missing subgraphs, subgraph recursion, invalid region ownership/nesting, missing target entries, and unbounded ForRange. If owns explicit Then and Else members. ForRange owns Body members and requires static non-negative bounds plus a maximum in 1..4096 that covers the range. Return must match a named Subgraph output type, and LoopIndex is valid only under a ForRange ancestor. Values may enter a branch from an outer scope but cannot escape or cross sibling branches. Output nodes are not legal inside structured regions.

The deterministic backend emits structured if/else, bounded for, and typed early-return HLSL. Control, value, LoopIndex, and Return nodes retain node-to-line source-map entries.

Custom HLSL and user DLL node providers are outside v1.

## Compiler CLI

The `shader-graph` command of tools/Cge (`Cge shader-graph`) provides validate, build, dump-hlsl, and publish. Validate/build/dump recursively compose external Subgraphs before graph validation and lowering. Build regenerates deterministic HLSL, compiles the fixed ABI with DXC, validates reflection, and writes one artifact. Generated HLSL and source maps are intermediate output only. Publish replaces the destination atomically. A failure is written in MSBuild's canonical form at the graph (`<graph>: error <Code>: <message>`, a warning as `warning`, with `[node <id>]` when one node is at fault; DXC diagnostics as `<hlsl>(<line>,<column>): Error: <message>`), so a build lists its cause and not only MSB3073; an unreadable or missing file is one such line without a stack trace.

MSBuild compiles explicit CgeShaderGraph items and their CgeShaderSubgraph inputs into obj/.../ShaderGraph and publishes into the output Shaders directory. The root SemanticHash and recursive DependencyHash are distinct. Matching hashes, target/ABI, fixed entries, and compiler/DXC provenance reuse an existing valid artifact without invoking DXC.

## Artifact format

.cgeshaderartifact is a versioned little-endian CGESHDR1 container. Stage retains artifact target value 1 and Compute value 4; retired values 2 and 3 are rejected and never reassigned. It records target, ABI hash, semantic/dependency hash, compiler and DXC provenance, entry-specific DXIL, reflection, PSO key, and SHA-256 checksum. Readers reject unsupported versions, checksum failure, target mismatch, ABI mismatch, missing or duplicate entries, invalid profiles, and empty bytecode/reflection.

## Runtime

D3D12 renderers consume artifacts only and validate them before creating a command list. Debug uses FileSystemWatcher; a pending artifact is loaded and replacement PSOs are built independently. Successful PSOs exchange at a frame boundary and old PSOs are released after the GPU fence. Failure preserves last-known-good bytecode/PSOs and exposes diagnostics. Release compiles out the watcher and performs one startup load.

## Visual Studio editor

The VSIX registers editor factories for both extensions. The WPF editor supplies canvas pan/zoom, a searchable built-in/prototype palette, typed connection rejection, multiple selection, copy/paste, duplicate/delete, comments/frames, structured If/ForRange/Return creation, entry tabs, properties, Error List mapping, save/build, external-change handling, external Subgraph listing/navigation, and Visual Studio Save/Undo/Redo commands. Region kind/name labels and editable control arguments make structured behavior visible without raw HLSL.

Localized 1033/1041 templates create Shader Graph or Shader Subgraph assets. A new root uses the native Stage template directly. The Stage template copies and coherently remaps the complete external dependency bundle using graph-prefixed filenames. Open Generated HLSL opens the semantic/dependency-hash-matched generated source.

## Stage oracles

| Feature | Stage | Evidence |
|---|---|---|
| F-L2-005 | shadergraph.schema | canonical/hash/type/cycle/region/subgraph tests |
| F-L7-049 | shadergraph.compiler | deterministic HLSL and fourteen-entry DXC tests |
| F-L6-004 | rendering.shader-artifacts | container/runtime/hot-reload tests |
| F-L8-031 | vsix.shader-graph | editor/factory/template/package tests |

## Known delta

External production Subgraphs and first-class structured If/ForRange/Return authoring/lowering are implemented. F-L2-005 and F-L7-049 Match this specification. Debug/Release 1041 CgeAutomation evidence now proves the Editor Factory, stable AutomationIds, layout/Undo/Redo/save/reload/diagnostic lifecycle, package identity, and normal shutdown; That UI evidence predates the M0119 switch to a Stage-only root template; the updated Stage-based UI automation has been built but not rerun.

## GPU layout verification (M0120)
L4 `Pipeline.StageFrameConstants` owns the managed FrameCB layout and, since stage-v17, `StageInstanceData`, `StageMaterialData` and `StageDrawConstants` own the instance table, the material table and the draw constants (they replaced `StageObjectConstants`); `StageGpuLayouts` derives offsets with Marshal.OffsetOf. DXC_OUT_REFLECTION is available as `HlslCompileResult.DxcReflection` from an actual compile. The L7-only `HlslShaderReflection` reads it and the compiler tests compare all CB and structured-buffer element field offsets/sizes and row-major matrices for Stage and Lumen. Lumen's StageInverseViewProjection explicitly aliases the Stage field InverseViewProjection. A card pixel entry reads the instance and material tables and has no FrameCB after optimization, which is tested as such.
This raw compiler data is separate from the artifact entry's existing JSON reflection metadata (entry/profile/stage/ABI); that serialized field and the runtime ABI have not changed. Runtime assemblies do not load DXC. The C# layout relocation of M0120 itself left the shader source/bytecode unchanged (FrameCB 960 bytes, ObjectCB 192 bytes, stage-v14 at that time).

## 対数ネイティブの契約(M0119)
現在の契約は`stage-v14`。Lumen の表面前パスは `SurfacePrepassPSMain` で既存の法線・粗さ・物理距離に加え `SV_Depth` を書く。対数カメラは共有の `EncodeRadialDepth` を使い、頂点は保守的な深度と明示的な近遠クリップ距離を使う。これにより、far 1e18 m・手前の面 1e7 m の場合でも、主画面と同じ面を Lumen が読む。正射影は線形のラスタ深度を返す。表面シェーダーは AO の物理距離生成にも使う。AO 用の旧シャドウ VS と専用 DSV は不要。主画面の `StagePixelOutput` は色 (`SV_Target0`)・物理距離 (`SV_Target1`)・深度を返す。距離は R32F の `SceneDistance` または DDGI の `CaptureDistance` に保存し、0 は背景。空と DDGI は双曲深度から位置を復元しない。現在のエントリー数は 14。標準経路の選択 API と旧 Model/Triangle は削除した。古いハッシュの成果物は再生成する。

M0119で、Stage グラフ本体の `VSMain`/`PSMain` が深度を持つようになった。ピクセル出力は `StagePixelOutput`(色と `SV_DepthGreaterEqual`)、単一の `VertexOutput` が VS と PS の契約。`CompleteStageSurface` が表示用を含む最終深度を共有する。旧 `Radial*` のラッパーエントリーは削除した。観察ビューだけが `ObserverVSMain` を使い、色・深度の PS は共有する。複製した古い Stage グラフは、新しい構造体・ヘルパーを含むグラフへ更新してから成果物を再生成する必要がある。

## 表面と照明の分割(M0129、ADR-0100 決定 2)
主パスの `PSMain` は、`EvaluateSurface`(材質を読んで `SurfaceData` を作る)と `EvaluateSurfaceLighting`(`SurfaceData` と画素の位置から色を作る)を呼ぶだけである。材質(物体と材質の表と、基本色・法線・metallic/roughness・発光の 4 枚のテクスチャ)を読むのは `EvaluateSurface` だけで、`EvaluateSurfaceLighting` は材質を読まない。`SurfaceData` は位置・基本色・metallic・粗さ・法線・頂点法線・発光を持つ(ADR-0085 決定 §2 と同じ形)。この分割ではエントリーも画素も変わらない。照明側の細かい分割(方向光・局所光・間接光・霧)は、描画パスを実装するとき(ADR-0085)に行う。後置ソース(`StagePresentationShaderSource`)の表示用・Lumen 用のエントリーは、`EvaluateSurface` を通らずに同じ表とテクスチャを読む。

## GPU シーン(M0129、ADR-0100 決定 2)
現在の契約は `stage-v18`(`stage-v17` は単位 3 の物体と材質の表まで、`stage-v18` は単位 4 の番号引き)。物体ごとの定数バッファ(ObjectCB)と、材質ごとのテクスチャの束(t0〜t3)は無い。代わりに次の 4 つがある。

| 名前 | レジスター | 中身 | 読むシェーダー |
|---|---|---|---|
| `DrawCB` | b1(ルート定数 3 個) | `DrawInstance`(描く物体の番号)、`DrawPresentationMode`(表示モード)、`DrawFlags`(描画の印) | 頂点、表示用の画素 |
| `Instances` | t20(構造化バッファ、1 件 84 バイト) | `World`、`ConservativeRasterDepth`、`EmissiveScale`、`SelectionWeight`、`Material` | 頂点、画素 |
| `Materials` | t21(構造化バッファ、1 件 128 バイト) | `BaseColorFactor`、`MaterialParams`(x 粗さ、y metallic)、`EmissiveNits`、世界格子の 4 つ、`Textures`(4 枚のテクスチャの番号) | 画素 |
| `MaterialTextures[]` | t0 space1(上限の無い配列) | 材質のテクスチャ。`Textures` の x 基本色、y 法線、z metallic と粗さ、w 発光 | 画素 |

- `VSMain` は `Instances[DrawInstance]` を読み、番号を `VertexOutput.Instance`(補間なし)で画素シェーダーへ渡す。画素シェーダーは `DrawInstance` ではなく `input.Instance` を使う。
- `DrawFlags` の `DrawFlagMeshSpace`(1)が立つと、`VSMain` は `World` の代わりに単位行列を使う(Lumen のカード撮影)。
- 発光は `Materials` の `EmissiveNits` に `Instances` の `EmissiveScale` を掛ける。カード撮影(`LumenCardPSMain`)は倍率を掛けない。
- 世界格子の原点は、`WorldGridAnchor.w` が 1 なら物体の位置(`World` の平行移動の符号を反転)、0 なら `WorldGridAnchor.xyz`。
- 影の頂点シェーダー `VSShadowMain` は `DrawCB` と `Instances` だけを読む。
- テクスチャは `MaterialTextures[NonUniformResourceIndex(...)]` で引く。1 回の描画が 1 つの材質を描く今も `NonUniformResourceIndex` を付けるのは、後の段階の間接描画が 1 回の描画で複数の材質を描くため。基本色と発光の番号は sRGB の見え方を指し、法線と metallic・粗さの番号はそのままの値の見え方を指す。
- 影の画像(t4)はレジスターを変えず、描画器が環境の表(t12〜t19 と同じ束)の末尾に置く。
- 描画器は D3D12 のリソース束縛の段が 2 以上であることを求める。満たさない GPU では、描画器を作るときにアダプターの名前を添えて止める。
- 管理コード側の配置の正本は L4 の `StageInstanceData`・`StageMaterialData`・`StageDrawConstants`(`StageGpuLayouts.cs`)。シェーダー側との一致は、DXC の反射を読む試験(`StageConstantLayoutReflectionTests`)が、エントリーごとに確かめる。
- 再現できる編集は `tools/ShaderGraphMaintenance/edits/gpu_scene_2026_11_17.py` と `bindless_textures_2026_11_17.py`。FrameCB(1248 バイト)、エントリー 14 個、t4〜t19 は `stage-v16` と同じ。

The restored integration workspace combines the native logarithmic Stage contract and virtual/soft shadows as `stage-v16`: fourteen native entries, SceneDistance MRT, FrameCB 1248 bytes and ObjectCB 192 bytes (ObjectCB was replaced by the GPU scene in stage-v17). StageGpuLayouts owns the appended virtual matrices/parameters and Lumen uses the same buffer layout. The clean integration commit before restoring workspace work retains stage-v14; its earlier hashes are historical evidence, not hashes of the combined workspace.
