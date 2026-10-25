# Shader Graph specification

- Status: Implemented with partial conformance
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0058](../Decisions/ADR-0058-Visual-Shader-Graph-And-Artifact-Runtime.md), [ADR-0059](../Decisions/ADR-0059-External-Shader-Subgraph-Composition.md), [ADR-0060](../Decisions/ADR-0060-Structured-Shader-Regions.md)
- Target layers: L2, L6, L7, L8

## Canonical assets

.cgeshadergraph is a root graph and .cgesubgraph is a reusable visual subgraph. Both are canonical UTF-8 JSON. Schema v1 stores stable GUID identity, target ABI, entry metadata, subgraphs, typed ports, nodes, edges, regions, external dependency IDs/paths, and editor layout.

Canonical serialization orders identities and argument names; it leaves out nulls, so a canonical file never holds one. A JSON null where the graph needs a value (the target, a list, a list item, a node type, an argument name or value, an edge port, a dependency path) is malformed JSON: loading throws a `JsonException` naming its path, as for any other malformed JSON. Semantic hashing removes node positions and region layout. Connection types must match exactly; conversions are explicit nodes. Unknown JSON node data survives load/save and validator diagnostics identify unsupported node types. A subgraph or node identity that appears twice (a node copied by hand in the JSON, a merged file) is a validation error (`DuplicateIdentity`), so generation reports it instead of failing.

The fixed root target is Stage, Model, or Triangle. The Stage graph defines VSMain, PSMain, VSShadowMain, FullscreenVS, and PostProcessPS; the engine-owned `StagePresentationShaderSource` suffix adds PreviewPSMain, PreviewCleanPSMain, OverlayVSMain, OverlayPSMain and the radial depth entries RadialPSMain, PreviewRadialPSMain, PreviewCleanRadialPSMain, RadialObserverVSMain and RadialVSMain (M0099, ADR-0076) and the screen layer entries ScreenVSMain and ScreenPSMain (M0104, ADR-0078) and the Lumen entries LumenSurfacePSMain and LumenCardPSMain (M0111), 18 entries in the current `stage-v14` migration contract (FrameCB ends with the depth-mapping packet and `LumenParams`; ObjectCB ends with `RadialRasterDepth`). `stage-v12`(M0110)は `stage-v11` と項目・定数の配置が同じで、材質のサンプラー s0 が異方性 8・wrap になり、材質テクスチャが全ミップを持つことだけが違う(`s0-anisotropic-wrap`)。Stage グラフと `StagePresentationShaderSource` は変えていないが、ABI のハッシュが変わるので成果物は作り直しになる。`stage-v13`(M0111)は FrameCB の末尾に `LumenParams`(x = Lumen の拡散、y = Lumen の反射)、t16 `LumenDiffuseTexture`・t17 `LumenReflectionTexture` と、接尾辞の `LumenSurfacePSMain`(表面の前処理: 法線と粗さ、カメラからの距離)・`LumenCardPSMain`(カードの撮影: 基本色 ×(1 − 金属)、法線、発光)を足した。Stage グラフの PSMain は、Lumen の拡散があればプローブボリュームと空の光の拡散を、反射があれば反射の照度を置き換える。`LumenParams` が 0 なら式は v12 と同じで、標準フレームのハッシュは変わらない。 Model and Triangle each have VSMain and PSMain.

Dependencies resolve relative to their owning document and may be recursive. Resolution validates document identity and rejects missing files, recursion, node collisions, and top-level declaration-slot collisions. Production Stage owns PBR, Shadow, PCSS, and ACES as external typed-syntax Subgraphs whose original declaration slots are preserved during composition.

## Validation and lowering

The built-in catalog contains typed constant, arithmetic, vector, utility, output, and Subgraph Call nodes. The production migration additionally represents every parser AST node and typed child relation as visual nodes; no raw HLSL source, code, or token payload is stored.

Validation rejects missing required ports, type mismatch, unknown ports, graph cycles, missing subgraphs, subgraph recursion, invalid region ownership/nesting, missing target entries, and unbounded ForRange. If owns explicit Then and Else members. ForRange owns Body members and requires static non-negative bounds plus a maximum in 1..4096 that covers the range. Return must match a named Subgraph output type, and LoopIndex is valid only under a ForRange ancestor. Values may enter a branch from an outer scope but cannot escape or cross sibling branches. Output nodes are not legal inside structured regions.

The deterministic backend emits structured if/else, bounded for, and typed early-return HLSL. Control, value, LoopIndex, and Return nodes retain node-to-line source-map entries.

Custom HLSL and user DLL node providers are outside v1.

## Compiler CLI

CSharpGameEngine.ShaderGraph.Cli provides validate, build, dump-hlsl, and publish. Validate/build/dump recursively compose external Subgraphs before graph validation and lowering. Build regenerates deterministic HLSL, compiles the fixed ABI with DXC, validates reflection, and writes one artifact. Generated HLSL and source maps are intermediate output only. Publish replaces the destination atomically. A failure is written in MSBuild's canonical form at the graph (`<graph>: error <Code>: <message>`, a warning as `warning`, with `[node <id>]` when one node is at fault; DXC diagnostics as `<hlsl>(<line>,<column>): Error: <message>`), so a build lists its cause and not only MSB3073; an unreadable or missing file is one such line without a stack trace.

MSBuild compiles explicit CgeShaderGraph items and their CgeShaderSubgraph inputs into obj/.../ShaderGraph and publishes into the output Shaders directory. The root SemanticHash and recursive DependencyHash are distinct. Matching hashes, target/ABI, fixed entries, and compiler/DXC provenance reuse an existing valid artifact without invoking DXC.

## Artifact format

.cgeshaderartifact is a versioned little-endian CGESHDR1 container. It records target, ABI hash, semantic/dependency hash, compiler and DXC provenance, entry-specific DXIL, reflection, PSO key, and SHA-256 checksum. Readers reject unsupported versions, checksum failure, target mismatch, ABI mismatch, missing or duplicate entries, invalid profiles, and empty bytecode/reflection.

## Runtime

D3D12 renderers consume artifacts only and validate them before creating a command list. Debug uses FileSystemWatcher; a pending artifact is loaded and replacement PSOs are built independently. Successful PSOs exchange at a frame boundary and old PSOs are released after the GPU fence. Failure preserves last-known-good bytecode/PSOs and exposes diagnostics. Release compiles out the watcher and performs one startup load.

## Visual Studio editor

The VSIX registers editor factories for both extensions. The WPF editor supplies canvas pan/zoom, a searchable built-in/prototype palette, typed connection rejection, multiple selection, copy/paste, duplicate/delete, comments/frames, structured If/ForRange/Return creation, entry tabs, properties, Error List mapping, save/build, external-change handling, external Subgraph listing/navigation, and Visual Studio Save/Undo/Redo commands. Region kind/name labels and editable control arguments make structured behavior visible without raw HLSL.

Localized 1033/1041 templates create Shader Graph or Shader Subgraph assets. The root wizard selects Stage, Model, or Triangle. A Stage template copies and coherently remaps the complete external dependency bundle using graph-prefixed filenames. Open Generated HLSL opens the semantic/dependency-hash-matched generated source.

## Stage oracles

| Feature | Stage | Evidence |
|---|---|---|
| F-L2-005 | shadergraph.schema | canonical/hash/type/cycle/region/subgraph tests |
| F-L7-049 | shadergraph.compiler | deterministic HLSL and nine-entry DXC tests |
| F-L6-004 | rendering.shader-artifacts | container/runtime/hot-reload tests |
| F-L8-031 | vsix.shader-graph | editor/factory/template/package tests |

## Known delta

External production Subgraphs and first-class structured If/ForRange/Return authoring/lowering are implemented. F-L2-005 and F-L7-049 Match this specification. Debug/Release 1041 CgeAutomation evidence now proves the Editor Factory, stable AutomationIds, layout/Undo/Redo/save/reload/diagnostic lifecycle, package identity, and normal shutdown; F-L8-031 Matches this specification.

## 対数ネイティブへの移行(M0119、Implementing)
`stage-v14` への移行中。Lumen の表面前パスは `LumenSurfacePSMain` で既存の法線・粗さ・物理距離に加え `SV_Depth` を書く。対数カメラは共有の `EncodeRadialDepth` を使い、頂点は保守的な深度と明示的な近遠クリップ距離を使う。これにより、far 1e18 m・手前の面 1e7 m の場合でも、主画面と同じ面を Lumen が読む。標準カメラは現在のラスタ深度を明示的に返す。現在のエントリー数は 18、CB 配置は v13 と同じ。ADR-0084 で決定した標準経路・ラッパーの削除と、14 エントリーへの切り替えは後続段階。古いハッシュの成果物は再生成する。

M0119 の次段階で、Stage グラフ本体の `VSMain`/`PSMain` が深度を持つようになった。ピクセル出力は `StagePixelOutput`(色と `SV_DepthGreaterEqual`)、単一の `VertexOutput` が VS と PS の契約。`CompleteStageSurface` が表示用を含む最終深度を共有する。現在の旧 `Radial*` エントリーは直接委譲する別名で、追加の深度処理は無い。複製した古い Stage グラフは、新しい構造体・ヘルパーを含むグラフへ更新してから成果物を再生成する必要がある。
