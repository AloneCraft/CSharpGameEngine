# ADR-0047: Generated Editor World and Preview Host

## Decision

Editor Preview does not implement a second spatial index inside the VSIX. The Source Generator emits
GeneratedEditorPreviewBridge and a hidden net10 Preview Host invokes it. Runtime and Editor use the same ECS
World, system order (ADR-0073), World.RefreshGlobalSpatialIndex, EntityLooseOctree and SpatialDebugHub.

Only systems marked with [ExecuteInEditor] run in editor mode. Without Realtime they run after Scene/Project
changes; Realtime=true adds a 15 Hz tick. Preview World changes are temporary and are never saved automatically.

## Switching

Scene View prefers a valid Runtime snapshot. When Runtime is not running, the pipe is unavailable, or a PID
selection is pending, it falls back to the Preview Host snapshot. Both sources use the same Spatial Debug DTO and
the same overlay renderer.

## Failure handling

Compilation, host, dependency, and packet failures never stop Visual Studio or the game. The last known good
snapshot or static Scene rendering remains visible. Dependencies are resolved from the generated assembly output folder.