# ADR-0052 — Render Graph and Preview Boundary

- Status: Accepted
- Date: 2026-08-25

## Context

The current Windows renderer and VSIX preview establish useful D3D12/WARP and Scene Designer behavior, but pass lifetime, shader provenance, device recovery, and preview ownership are not one explicit contract. A VSIX-owned renderer would duplicate native lifetime and visual validation paths.

## Decision

Place an engine-owned Render Graph in L4, keep D3D12/DXGI objects private to L0, compile HLSL into L6 artifacts in L7, and make an out-of-process Preview Host a versioned Render Server client. Rendering consumes immutable stable-ID snapshots rather than direct ECS traversal.

## Consequences

- Pass/resource handles, hazards, aliases, queues, and fences are engine concepts.
- Runtime shader compilation and a VSIX-specific renderer are removed as their replacements land.
- Existing local/area/emissive shadow capability remains baseline; quality expansion adds cascades, IBL, exposure, AA, and profiles.
- WARP remains required visual evidence and hardware is additive.

## Migration and verification

Each feature cuts over immediately, removes old API/artifact readers, and regenerates artifacts. `rendering.render-graph`, `rendering.shader-artifacts`, recovery, preview, and WARP Stage Oracles in [Rendering](../Specifications/Rendering.md) provide acceptance evidence.

## Related features

F-L7-022, F-L4-004, F-L0-008, F-L6-004 through F-L7-024.
