# ADR-0058 — Visual Shader Graph and artifact-only D3D12 runtime

- Status: Accepted
- Date: 2026-08-29

## Context

The D3D12 renderers compiled production HLSL through DXC at runtime. This made startup depend on source files and compiler-native binaries, prevented a stable shader ABI boundary, and gave the existing VSIX no shader-specific visual authoring format.

## Decision

.cgeshadergraph is the canonical UTF-8 JSON source. Stable GUIDs identify documents, subgraphs, nodes, edges, and regions. Editor-only layout is excluded from the semantic hash. Stage, Model, and Triangle use fixed target ABIs and nine total entry points.

CSharpGameEngine.ShaderGraph.Cli owns validation, deterministic HLSL generation, source mapping, DXC compilation, and artifact publication. CSharpGameEngine.ShaderArtifacts owns the versioned CGESHDR1 container, checksum, target/ABI verification, DXIL entries, reflection, compiler provenance, dependency hash, and PSO key.

D3D12 Runtime accepts .cgeshaderartifact only. Debug builds watch artifacts and exchange successfully-created PSOs at a frame boundary while preserving last-known-good state on failure. Release builds contain no watcher and load once at startup. Level.ShaderArtifactPath is the immediate replacement for Level.ShaderPath.

The existing VSIX WPF and IVsEditorFactory infrastructure owns the visual editor. Shader Graph and Shader Subgraph item templates are packaged for 1033 and 1041.

## Consequences

- Production HLSL and runtime DXC are removed.
- Generated HLSL, source maps, DXIL, reflection, and artifacts remain generated files.
- Fixed ABI mismatch, corruption, and reflection mismatch fail before GPU submission.
- Layout edits do not invalidate DXIL.
- Custom HLSL and user-DLL nodes are not v1 extension points.
- External production subgraph extraction remains a follow-up; current production graphs retain complete typed syntax-node trees.

## Verification

Schema, compiler, artifact, runtime boundary, WARP rendering, hot reload, VSIX model/package, and Debug/Release stages provide automated evidence. Experimental Instance behavior remains supplemental evidence.

## Related features

F-L7-022, F-L2-005, F-L7-049, F-L6-004, F-L8-031.