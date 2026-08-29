# M0060 — Visual Shader Graph, Shader Artifact, and D3D12 Hot Reload

## Outcome

Stage, Model, and Triangle production shaders now have canonical .cgeshadergraph sources. The compiler CLI deterministically regenerates HLSL, compiles five plus two plus two fixed-ABI entries with DXC, and publishes checksummed .cgeshaderartifact containers. D3D12 Runtime no longer references DXC or HLSL compiler APIs.

Debug renderers monitor artifacts, build replacement PSOs separately, exchange only at frame boundaries, and retain last-known-good PSOs after corrupt or invalid writes. Release renderers load artifacts once.

The existing Visual Studio extension now registers Graph/Subgraph editor factories and provides a WPF node canvas, palette search, typed connections, selection/edit commands, property editing, diagnostics, Undo/Redo routing, generated-HLSL opening, save/build publication, external-change handling, and localized target-selecting item templates.

## Public migration

- Level.ShaderPath became Level.ShaderArtifactPath; no compatibility member remains.
- Runtime renderer arguments and diagnostics use ShaderArtifactPath terminology.
- Runtime shader compilation and DXC package/native deployment moved to CSharpGameEngine.ShaderGraph.Cli.
- MSBuild explicitly compiles registered graph items and atomically publishes artifacts into Shaders.

## Automated evidence

- Canonical round-trip, layout-independent semantic hash, typed edges, graph cycles, subgraph recursion, and structured-loop guards.
- Deterministic HLSL/artifacts, all nine DXC entries, fixed ABI, checksum/version/target rejection, and source-map diagnostics.
- Artifact-only WARP rendering for Triangle, Model, Stage, lights, shadows, HDR, bloom, and ACES with no debug-layer errors.
- Debug hot reload success, corrupt-write last-known-good behavior, recovery, and Release watcher exclusion.
- VSIX editor behavior, native command routing, diagnostics, localization, factory registration, wizard registration, and package payload.

## Remaining conformance delta

The external reusable PBR/shadow/PCSS/ACES .cgesubgraph production split and automated Experimental Instance interaction smoke are not complete. Internal typed subgraphs, recursion validation, and localized Subgraph templates exist, but the production graphs currently retain complete typed syntax trees. These differences remain Partial in Conformance.

## Next milestone

Extract reusable production declarations into dependency-tracked external subgraphs, add first-class region editing/navigation, and automate Visual Studio Experimental Instance open/edit/save/reload evidence.
## Follow-up closure

M0061/ADR-0059 completes the external PBR/Shadow/PCSS/ACES split, recursive dependency hashing, artifact cache validation, Stage-template dependency copying, and dedicated Subgraph navigation. Structured high-level region authoring and Experimental Instance automation remain future work.