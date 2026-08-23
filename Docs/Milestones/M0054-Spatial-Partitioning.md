# M0054: Fixed-point spatial partitioning

## Goal

Provide a shared ECS spatial snapshot plus independently-owned loose octrees for systems that need a specialized index.

## Implemented

- Fixed-point AABB, sphere, ray, layer-mask, query-result, and index contracts.
- Dynamic `EntityLooseOctree` with split, merge, root expansion, compact rebuild, and one proxy per Entity.
- Generated `Transform + SpatialBounds` scan after structural playback and before the first generated frame.
- World diagnostics for excluded spatial Entities.

## Deliberately excluded

Grid/BVH, parallel readers or writers, multiple proxies per index, persistence, and automatic private-index management.

## Feature status

- F-L3-015: Implemented / TestVerified
- F-L3-016: Implemented / TestVerified

## Verification

- `dotnet test tests/CSharpGameEngine.Windows.Tests/CSharpGameEngine.Windows.Tests.csproj -c Debug --no-restore`
- `dotnet test tests/CSharpGameEngine.CodeAnalysis.Tests/CSharpGameEngine.CodeAnalysis.Tests.csproj -c Debug --no-restore`

## Observed behavior and conformance

The final source and tests show single-proxy loose-octree placement, caller-owned query results, frame-end generated refresh, and a read-only World API. This matches the intended shared-versus-private ownership decision. Runtime integration with a production `SpatialBounds` scene remains the next end-to-end scenario.
