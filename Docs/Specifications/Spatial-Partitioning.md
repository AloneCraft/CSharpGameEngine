# Spatial partitioning

## Intended behavior

`SpatialBounds` opts an ECS `Entity` into the World-owned global spatial snapshot. Its local AABB uses the application-selected `Logical` fixed-point coordinate format and is transformed through the complete `Transform`/`Parent` hierarchy before insertion.

The global snapshot is read-only through `World.GlobalSpatialIndex`. It is synchronized after structural-command playback at the end of every generated frame, so Systems query a complete snapshot from the preceding frame. The generated entry point also synchronizes once before its first frame. `World.RefreshGlobalSpatialIndex()` is the explicit single-writer escape hatch for an immediate update.

`EntityLooseOctree` is the mutable implementation for system-owned indexes. Each index stores one AABB proxy per `Entity`; systems own registration, removal, settings, and update timing. It is not safe to query while another caller mutates it.

## Queries and precision

- Fixed-point `SpatialAabb`, `SpatialSphere`, and normalized `SpatialRay` provide inclusive AABB, point, sphere, and ray queries.
- `SpatialLayerMask` filters the 64 layers represented by `SpatialLayer`.
- Query methods append to caller-owned lists, leave them unchanged on validation or arithmetic failure, and do not allocate result buffers.
- Local AABB corners are transformed entirely in fixed point. Invalid bounds, invalid parent resolution, and overflow exclude the Entity and are exposed through `GlobalSpatialSyncReport` and `TryGetGlobalSpatialFailure`.

## Octree behavior

- Items are stored in the deepest loose child that fully contains their AABB; straddling items remain in an ancestor and never duplicate.
- Nodes split above capacity and merge after removals when their complete subtree fits the capacity.
- The root doubles toward out-of-range bounds and does not shrink automatically. `Compact()` rebuilds a smallest current root.
- Defaults are capacity 16, maximum depth 16, loose factor 1.5, and minimum root half extent 1.

## Exclusions

Grid/BVH implementations, multiple proxies per Entity per index, persistence, concurrent access, and automatic ownership of private indexes are outside this version.

## Scene View デバッグ公開

`EntityLooseOctree.CollectDebugNodes` はRootと非空ノードを重複なしで列挙する。`World.RefreshGlobalSpatialIndex` 成功後にエンジン所有の `SpatialDebugHub` へ最新索引を登録し、Named Pipe要求時のみ固定精度座標をdoubleへ変換した最大8,192ノードのスナップショットを返す。送信は現在プロセスIDとCurrentUserOnlyで分離し、Scene Viewは最大512ノード・24線頂点/ノードで描画する。