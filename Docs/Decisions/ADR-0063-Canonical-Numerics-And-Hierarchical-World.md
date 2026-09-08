# ADR-0063: Canonical numerics and hierarchical world runtime

- Status: Accepted
- Date: 2026-08-31

## Context

The engine must support planetary and space-scale C# worlds while preserving deterministic Bake/runtime behavior. The current wide scalar profiles allocate `BigInteger` during arithmetic, authoritative state mixes several numeric meanings, Entity slots never recycle, and one global World/spatial index couples work to total world size. Renderer and editor hosts also distribute ownership of native resources and duplicate runtime state.

## Decision

Adopt a partitioned modular runtime based on one signed Q64.64 `LogicalScalar`, domain-relative `float`, generational runtime Entities, persistent world identities, a bounded hierarchical Reference Frame tree, and sparse dependency-aware World Cells. One `EngineHost` owns runtime services and native resource lifetimes. C# source and original assets remain authoring truth; completed fixed-tick ECS/frame/active-cell state is runtime truth; all presentation and cache state is derived.

Kepler motion is a frame pose provider. It is not a rigid-body solver mode. F-L5-023 and F-L5-027 are therefore promoted from Deferred to required WorldScale Features. F-L3-020 is promoted because the required stress contract includes 128 Component types.

## Alternatives

- Continue the current structure: preserves local tests but retains multiple numeric truths, non-recycled Entities, global traversal, and distributed lifecycle ownership.
- One global `LogicalScalar` coordinate space: conceptually small, but forces universe-scale arithmetic into every entity hot path and does not establish streaming ownership.
- Flat cells with floating origin: suitable for a ground world, but turns planets, orbits, vehicles, and multiple physics domains into special cases.
- Rewrite every subsystem: removes constraints but discards verified C# authoring, Stable ID, Shader Graph/artifact, input, and Feature Runner contracts without benefit.

The selected hierarchical frame plus partitioned runtime has the fewest long-term concepts that satisfy all scale and lifecycle invariants.

## Invariants

- Engine-owned real state is `LogicalScalar` or relative `float`; allowlisted adapters and test oracles do not retain `double` or `BigInteger`.
- Persistent identity never aliases a runtime Entity generation.
- A frame has at most one parent and the frame graph is acyclic with depth at most 32.
- Frame and cell state changes are visible only at fixed-tick structural barriers.
- Failed or cancelled cell work cannot mutate the last-known-good active set.
- Each active cell exclusively owns the runtime Entities created from that cell.
- Native resources are owned by the host service tree, not ECS Components or static bindings.

## Consequences

Numerics, ECS identity, World, Content, rendering/physics/audio extraction, host lifecycle, VSIX hosting, snapshots, and release evidence require staged migration. Verified external contracts remain compatible; internal profiles, readers, adapters, and duplicated renderers are removed after callers migrate. The architecture enables active-set costs and explicit fault recovery at the price of a frame/cell model that every world-facing subsystem must honor.
