# ADR-0045: Shared and private spatial indexes

- Status: Accepted
- Date: 2026-08-23

## Context

Gameplay, visibility, and selection systems need spatial queries at fixed-point world coordinates. A separate index per system can reduce each query's working set but duplicates memory and update work.

## Decision

The ECS `World` owns one automatically synchronized, read-only `EntityLooseOctree` for `Transform + SpatialBounds` Entities. Its synchronization point is after generated structural playback, giving all Systems one complete previous-frame snapshot. Systems may own additional mutable `EntityLooseOctree` instances and explicitly manage their membership and update timing.

The first implementation is entity-only, fixed-point, single-writer, and loose-octree-only. The public interfaces separate read-only querying from mutation so future Grid/BVH implementations can conform without making the global index mutable.

## Consequences

The global path minimizes duplicated storage but has one broad policy and one-frame snapshot latency. Private indexes can be tightly tuned but incur their own memory, synchronization, and lifecycle costs. Explicit refresh is allowed only as a single-writer operation.

## Verification

`SpatialPartitioningTests` covers query, layer, root growth, removal, and compaction behavior. `SpatialWorldGeneratorEmissionTests` proves generated synchronization and frame-end refresh emission.

## Related features

- F-L3-015
- F-L3-016
