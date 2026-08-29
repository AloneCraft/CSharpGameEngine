# ADR-0059 - External Shader Subgraph composition

- Status: Accepted
- Date: 2026-08-29

## Context

M0060 made Stage, Model, and Triangle canonical visual graphs, but the production Stage graph still owned one complete typed syntax tree. PBR, shadow filtering, PCSS, and ACES could not be reviewed or reused as independent `.cgesubgraph` assets, and the artifact dependency hash did not represent external source changes.

## Decision

A root `.cgeshadergraph` stores an ordered canonical `dependencies` list of stable document IDs and relative paths. Dependencies may reference further dependencies. The CLI resolves them recursively, rejects missing files, identity mismatch, recursion, node collisions, and top-level declaration-slot collisions, then composes typed syntax declarations into the root translation unit before validation and lowering.

External production Subgraphs preserve their original `Declarations[n]` slots. Composition therefore regenerates the same HLSL and source-map node identities as the monolithic graph regardless of dependency file order.

The root semantic hash covers the root source, including dependency references. The dependency hash additionally covers every resolved dependency's semantic/dependency hash. Editor layout remains excluded. An existing artifact is reusable only when target, ABI, both hashes, compiler/DXC provenance, and every fixed ABI entry validate.

Stage extracts PBR, Shadow, PCSS, and ACES into four external typed-syntax Subgraphs. MSBuild declares them as `CgeShaderSubgraph` inputs. The VSIX displays root dependencies, opens them from the editor, packages the production template dependencies, and copies/remaps the complete dependency bundle when creating a Stage graph.

## Consequences

- Production Stage HLSL remains byte-identical to the M0060 WARP baseline.
- Layout-only or timestamp-only dependency changes can trigger MSBuild evaluation without invoking DXC or rewriting a valid artifact.
- A semantic dependency change invalidates the artifact while leaving the root semantic hash stable.
- Root and dependency source stay canonical JSON; generated HLSL, source maps, DXIL, and artifacts remain build outputs.
- High-level If/For/Return region authoring and automated Visual Studio Experimental Instance interaction remain separate follow-up work.

## Verification

Schema tests cover dependency canonicalization. CLI tests cover recursive resolution, golden HLSL, dependency hash invalidation, missing files, identity mismatch, recursion, deterministic DXC output, and artifact reuse. VSIX tests cover package payload and template bundle remapping. Debug/Release Feature Stages remain the release-facing oracle.

## Related

ADR-0058; F-L2-005; F-L7-049; F-L6-004; F-L8-031.