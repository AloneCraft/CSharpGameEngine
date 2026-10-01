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
## One process per Scene view (M0096, 2026-10-01)

At the user's request the editor world no longer has a process of its own. The runtime view's render server
(ADR-0070 B1) loads the game once and holds two worlds through the same generated bridge: the view world, which
runs only Presentation systems into the Scene view's surface, and the editor world described above
(`Initialize`, the 15 Hz `TickRealtime`, `Shutdown`). The worlds stay separate. The ticks run under the session's
lock, so they never overlap a request.

- The Spatial and Physics debug overlays ask the render server's process. Both worlds publish spatial snapshots
  there; a request without a world id gets the editor world, which the bridge marks as preferred, so the overlay
  shows the same world as before.
- The preview host's `--assembly` mode and the extension's separate editor-world process are removed. One copy of
  the game build serves both worlds.
- Costs the user accepted: a crash in either world ends the whole Scene view process, so the image and the debug
  overlays stop together; there is no editor world without a runtime view, so a scene the build does not contain has
  no editor-world overlays until it is built.
