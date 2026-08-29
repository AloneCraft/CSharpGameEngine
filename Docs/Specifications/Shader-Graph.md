# Shader Graph specification

- Status: Implemented with partial conformance
- Parent index: [Roadmap](../Roadmap.md), [Implementation Status](../Implementation-Status.md)
- Decisions: [ADR-0058](../Decisions/ADR-0058-Visual-Shader-Graph-And-Artifact-Runtime.md), [ADR-0059](../Decisions/ADR-0059-External-Shader-Subgraph-Composition.md)
- Target layers: L2, L6, L7, L8

## Canonical assets

.cgeshadergraph is a root graph and .cgesubgraph is a reusable visual subgraph. Both are canonical UTF-8 JSON. Schema v1 stores stable GUID identity, target ABI, entry metadata, subgraphs, typed ports, nodes, edges, regions, external dependency IDs/paths, and editor layout.

Canonical serialization orders identities and argument names. Semantic hashing removes node positions and region layout. Connection types must match exactly; conversions are explicit nodes. Unknown JSON node data survives load/save and validator diagnostics identify unsupported node types.

The fixed root target is Stage, Model, or Triangle. Stage has VSMain, PSMain, VSShadowMain, FullscreenVS, and PostProcessPS; Model and Triangle each have VSMain and PSMain.

Dependencies resolve relative to their owning document and may be recursive. Resolution validates document identity and rejects missing files, recursion, node collisions, and top-level declaration-slot collisions. Production Stage owns PBR, Shadow, PCSS, and ACES as external typed-syntax Subgraphs whose original declaration slots are preserved during composition.

## Validation and lowering

The built-in catalog contains typed constant, arithmetic, vector, utility, output, and Subgraph Call nodes. The production migration additionally represents every parser AST node and typed child relation as visual nodes; no raw HLSL source, code, or token payload is stored.

Validation rejects missing required ports, type mismatch, unknown ports, graph cycles, missing subgraphs, subgraph recursion, invalid region members, missing target entries, and unbounded ForRange. A static maximum is required for loop-region nodes. The deterministic backend emits HLSL plus node-to-line source maps.

Custom HLSL and user DLL node providers are outside v1.

## Compiler CLI

CSharpGameEngine.ShaderGraph.Cli provides validate, build, dump-hlsl, and publish. Validate/build/dump recursively compose external Subgraphs before graph validation and lowering. Build regenerates deterministic HLSL, compiles the fixed ABI with DXC, validates reflection, and writes one artifact. Generated HLSL and source maps are intermediate output only. Publish replaces the destination atomically.

MSBuild compiles explicit CgeShaderGraph items and their CgeShaderSubgraph inputs into obj/.../ShaderGraph and publishes into the output Shaders directory. The root SemanticHash and recursive DependencyHash are distinct. Matching hashes, target/ABI, fixed entries, and compiler/DXC provenance reuse an existing valid artifact without invoking DXC.

## Artifact format

.cgeshaderartifact is a versioned little-endian CGESHDR1 container. It records target, ABI hash, semantic/dependency hash, compiler and DXC provenance, entry-specific DXIL, reflection, PSO key, and SHA-256 checksum. Readers reject unsupported versions, checksum failure, target mismatch, ABI mismatch, missing or duplicate entries, invalid profiles, and empty bytecode/reflection.

## Runtime

D3D12 renderers consume artifacts only and validate them before creating a command list. Debug uses FileSystemWatcher; a pending artifact is loaded and replacement PSOs are built independently. Successful PSOs exchange at a frame boundary and old PSOs are released after the GPU fence. Failure preserves last-known-good bytecode/PSOs and exposes diagnostics. Release compiles out the watcher and performs one startup load.

## Visual Studio editor

The VSIX registers editor factories for both extensions. The WPF editor supplies canvas pan/zoom, searchable palette, typed connection rejection, multiple selection, copy/paste, duplicate/delete, comments/frames, entry tabs, properties, Error List mapping, save/build, external-change handling, external Subgraph listing/navigation, and Visual Studio Save/Undo/Redo commands.

Localized 1033/1041 templates create Shader Graph or Shader Subgraph assets. The root wizard selects Stage, Model, or Triangle. A Stage template copies and coherently remaps the complete external dependency bundle using graph-prefixed filenames. Open Generated HLSL opens the semantic/dependency-hash-matched generated source.

## Stage oracles

| Feature | Stage | Evidence |
|---|---|---|
| F-L2-005 | shadergraph.schema | canonical/hash/type/cycle/region/subgraph tests |
| F-L7-049 | shadergraph.compiler | deterministic HLSL and nine-entry DXC tests |
| F-L6-004 | rendering.shader-artifacts | container/runtime/hot-reload tests |
| F-L8-031 | vsix.shader-graph | editor/factory/template/package tests |

## Known delta

External production Subgraph extraction, recursive dependency hashing, incremental artifact reuse, template copying, and dedicated navigation are implemented. Remaining partial conformance is limited to first-class high-level If/For/Return region authoring/editing and automated Experimental Instance interaction evidence.