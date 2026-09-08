# World-scale runtime specification

- Status: Specified
- Parent index: [Roadmap](../Roadmap.md), [Feature Stages](../Feature-Stages.md)
- Decision: [ADR-0063](../Decisions/ADR-0063-Canonical-Numerics-And-Hierarchical-World.md)
- Target layers: L1, L2, L4, L5, L6

## Product outcome

C# `IScene` and `IWorldDefinition` source plus original assets are the only authoring source. Bake produces immutable content-addressed cell artifacts. Runtime work is proportional to the active cell set and domain-local frame contents, never to the full authored universe.

## Identity and source of truth

`StableId`, `AssetId`, `ReferenceFrameId`, `WorldCellId`, and `PersistentEntityId` are persistent identities. Runtime `Entity(Index,Generation)` is a recyclable process-local handle and is never serialized. The completed fixed-tick ECS World, immutable Reference Frame snapshot, and active cell set are runtime truth. Render, physics, audio, debug, preview, visibility, and cache representations are derived.

## Canonical numeric model

Engine-owned real state uses only signed Q64.64 `LogicalScalar` and camera/domain-relative `float`. `LogicalScalar` exposes checked arithmetic, ties-to-even conversion, overflow rejection, division-by-zero rejection, square root, and canonical serialization. Arithmetic uses `Int128` storage and explicit multi-limb 256-bit intermediates without allocation. `double` and `BigInteger` are limited to independent test oracles and framework or SDK adapter signatures; an adapter validates and converts within the same method and never stores either type in engine-owned state.

`ExternalNumericAdapter` is a member-level escape hatch, not a type-level numeric mode. Class/struct/field annotations and framework-base-class inheritance do not suppress the canonical numeric rule. A framework `double` result must be projected directly to `float` or consumed inside the marked adapter member; assigning it to inferred engine state is an error.

`ExternalNumericAdapter` is a member-level escape hatch, not a type-level numeric mode. Class/struct/field annotations and framework-base-class inheritance do not suppress the canonical numeric rule. A framework `double` result must be projected directly to `float` or consumed inside the marked adapter member; assigning it to inferred engine state is an error.

Authoritative position, rotation, scale, velocity, simulation values, frame poses, and time scale use `LogicalScalar`. Fixed time uses integer ticks and platform counter values. Rendering, audio spatialization, and UI projection receive only already-relative `float` values.

## Reference Frame Graph

The graph is a single-parent tree with maximum depth 32. Frame mutations enter a command buffer and become visible only at a fixed-tick structural barrier. A completed tick publishes one immutable snapshot. Creation or reparenting rejects cycles, missing parents, depth overflow, more than one parent assignment in the same tick, and non-representable transforms. Cross-frame conversion composes through the least common ancestor and never creates a global absolute `float` coordinate.

Frames model the universe root, star systems, planets, moons, vehicles, and local physics domains through the same contract. Physics, rendering, and audio consume a frame snapshot and a selected domain frame.

## Kepler frame provider

Kepler propagation is a deterministic frame pose provider. It reads immutable orbital elements and integer/domain time, then emits a frame pose command for the next tick boundary. It does not inject special entities or forces into the local rigid-body solver. One-period closure and long-run repeatability are verified against an independent oracle with an explicit Q64.64 error budget.

## World cell artifacts

A World Cell is a sparse Bake unit identified by `WorldCellId`; it is not constrained to one global fixed grid. Its canonical manifest contains frame-local bounds, dependency cell IDs, LOD, entity payload identity, and complete artifact closure. Dependencies form a DAG. Activation owns every runtime Entity created from the cell, and unload destroys that ownership set at one structural barrier.

## Cell state model

The only legal lifecycle is `Unloaded -> Loading -> Loaded -> Activating -> Active -> Deactivating -> Unloading -> Unloaded`, with failure entering `Faulted`. A fault does not retry automatically. Cancellation, corrupt artifacts, missing dependencies, or budget exhaustion do not mutate the last-known-good active set. Loading may run asynchronously; activation and unload are atomic fixed-tick commands.

Loading resolves an immutable `AssetArtifactReference -> canonical envelope address` index against the filesystem CAS. The full envelope address, not the payload hash alone, owns CAS identity. Dependencies load in topological waves; cells within a wave use an explicit concurrency bound, and a failed dependency prevents dependent reads from starting. A successful wave remains unpublished until the complete desired set can activate.

## Active-set selection

Each reference frame owns an immutable sparse AABB index built from canonical cell bounds. A selection query contains one frame, a frame-local `LogicalScalar` position, an axis-aligned Chebyshev radius, an inclusive LOD range, and a caller-provided result budget. It never composes a global coordinate and never converts authoritative state to `float`. Results are ordered deterministically by distance, LOD, and stable cell identity. Query work visits intersecting spatial nodes rather than scanning the authored universe, and a warmed query performs no managed allocation.

The Scene source generator assigns every C# `IScene` one canonical entity-payload `AssetKey` and emits cell construction beside the normal Scene constructor. Node persistent IDs derive from cell identity plus Scene/node path. The generated activation sink validates the payload artifact before mutation, creates only cell-owned Entities, rolls back partial construction, and unloads the ownership set as one unit. WorldCellRuntime transition mutation is internal to the streaming service, so external code cannot interleave a second state transition between ECS application and active-set publication.

## Ownership and lifecycle

One `EngineHost` owns window, clock, input, World, streaming, rendering, physics, and audio services. ECS Components contain portable values only and never own native handles, renderer objects, windows, voices, or host leases. Services may depend on Contracts, Numerics, Core, Entities, World snapshots, and Content handles; Rendering, Physics, and Audio do not depend on one another.

## Required verification

Numeric property tests cover arithmetic, rounding, overflow, square root, serialization, and float boundaries. Frame tests cover cycles, depth 32, reparenting, frame removal, cross-frame round trip, time scale, and long-running Kepler propagation. Cell tests cover partial load, cancellation, missing dependencies, corruption, rapid churn, unload/reload, persistent references, atomic active-set preservation, frame isolation, LOD/budget ordering, spatial pruning, bound overflow, and warmed-query allocation. Architecture validation rejects forbidden numeric types and native resource ownership outside adapters and platform backends.
