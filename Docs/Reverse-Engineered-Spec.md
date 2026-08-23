# Reverse-engineered specification

## RE-L3-015/016 - Spatial partitioning

- Confidence: High
- Evidence: `SpatialPartitioning.cs`, `SpatialWorld.cs`, `SpatialWorldEmitter.cs`, and their tests.
- Observed behavior: the octree uses deepest-containing loose-node placement, expands its root without automatic shrink, and queries caller-owned result lists. Generated systems refresh a `Transform + SpatialBounds` World snapshot after structural playback.
- Result: Match.
