# M0061 - External Shader Subgraph composition and incremental artifacts

## Outcome

The production Stage graph now references four canonical external assets: `Pbr.cgesubgraph`, `Shadow.cgesubgraph`, `Pcss.cgesubgraph`, and `Aces.cgesubgraph`. They contain typed visual syntax nodes only. Recursive CLI composition restores the original top-level declaration slots and reproduces the M0060 Stage HLSL SHA-256 `67C5652958550327DFB90C2059E1FE5778A0D0B59B0787D21919314A9D67DF81` byte for byte.

## Compiler and build behavior

- Root JSON carries stable dependency document IDs and normalized relative paths.
- Resolution rejects missing dependencies, ID mismatch, recursion, node ID collision, and declaration-slot collision.
- `SemanticHash` identifies the root source. `DependencyHash` includes the complete recursive dependency closure.
- MSBuild tracks the four explicit `CgeShaderSubgraph` inputs for Stage consumers.
- A valid artifact with matching hashes, target/ABI entries, and compiler/DXC provenance is reused without DXC or artifact rewrite.
- HLSL/source-map generation and artifact publication remain atomic.

## Visual Studio behavior

The Shader Graph editor lists external Subgraphs and opens the selected dependency on double-click. The Stage item template includes the four production Subgraphs, assigns a coherent new stable-ID set across the bundle, writes unique graph-prefixed dependency names, and adds the generated Subgraph files to the destination project.

## Automated evidence

- Canonical dependency ordering, path normalization, semantic hash participation, and round-trip.
- Exact pre/post-split Stage HLSL golden hash and fixed five-entry Stage DXC build.
- Dependency semantic change affects `DependencyHash` but not root `SemanticHash`.
- Missing dependency, identity mismatch, and external recursion rejection.
- Read-only artifact cache-hit predicate and real UserEcsProject MSBuild publication.
- VSIX package contains all four Subgraphs; template bundle copy and ID remapping are verified.

Full verification passed all five affected Feature Stages in Debug and Release, 1,628 Debug solution tests, 1,627 Release solution tests, and UserEcsProject startup/normal-shutdown smoke in both configurations.

## Remaining delta

External production Subgraph composition and dedicated navigation are complete. F-L2-005 and F-L7-049 remain Partial only for first-class high-level If/For/Return region authoring and related visual editing. F-L8-031 remains Partial only for automated Experimental Instance interaction evidence.

## Next milestone

Implement first-class structured region authoring/lowering and record automated Visual Studio Experimental Instance open/edit/save/reload evidence.