# M0093 — Scene JSON documents, editor Undo/Save and the runtime editor world
- Date: 2026-10-01
- Decision: [ADR-0070](../Decisions/ADR-0070-Scene-Json-Documents-And-Runtime-Editor-World.md)
- Implementation: In progress

## Goal
Make the Scene Designer behave like Unreal's and Unity's editors, as the user asked on 2026-10-01:
- Scenes are schema-checked JSON documents, turned into C# at build time.
- Edits stay in memory with Undo/Redo until Save.
- The Scene view shows the image the game renders.

## Steps
The steps are ordered so the Scene Designer keeps working at every commit. Repository scenes move to JSON only after the editor can open JSON.

| Step | Content | Status |
|---|---|---|
| C1 | JSON DOM, reader and span-preserving writer in the shared SceneModel sources; `schemas/cge-scene.schema.json`; the generator reads `*.scene.json` from `AdditionalFiles`; `Game.props`/SDK include them; a C#-scene → JSON converter | Done (d004b06, f38ec04, d9464bd) |
| C1b | Per-project JSON Schema from the generator (embedded constant, written to `.cge/scene.schema.json` after build) for editor warnings and completion; build errors SCENE201-205 at JSON positions | Done (ee9b235) |
| C2 | The Scene Designer opens, edits and saves `*.scene.json` (ADR-0070 decision 5): the designer view of the live JSON (`SceneJsonDesignerSource`, `SceneJsonWorkspaceSource`), gestures committed as minimal JSON changes in one VS Undo unit (`SceneJsonCommitPlanner`), the scene catalog, the code-window and Solution Explorer menus, and source markers on the JSON node | Done (fb75f59, eb35738); a manual VS check is pending |
| C3a | Convert UserEcsProject (16 scenes), the ThirdPerson template (3) and the FeatureStages (10) with `CSharpGameEngine.SceneJson.Cli convert --write --delete-source`. The export scripts convert the UE importer's output, the project generator and the VSIX carry `*.scene.json`, and "Add New Item > Scene" creates a `*.scene.json` whose schema path a wizard fills in | Done (a186050) |
| C3b | Remove the user-authored C# `IScene` path: the generator reads scenes only from JSON, a C# scene class is SCENE206, and JSON `startupOrder` conflicts are SCENE118. The VSIX discovers, opens and writes only `*.scene.json`, and the C# scene writer is removed. Tests keep writing scenes in the C# DSL; their helpers convert them the way the CLI does. Model predictions are checked by writing JSON and reading it back | Done (this commit) |
| B1 | Preview host instantiates the editor world from the in-memory JSON inside the game assembly and renders it with the runtime's Presentation systems (steps below) | In progress: steps 1 (f38e9f4) and 2 (this commit) done |
| B2 | Keep the device and swap chain across edits; exposure/GI history survives edits | Planned |

## B1 design (2026-10-01)
Facts that shape it:
- The ECS has closed-set archetypes: a component combination that was not built does not exist at runtime.
- The runtime renders through `LevelLoad`, which creates the `RenderServer` on `EngineHost.Window` and drives the camera from the scene's active `Camera` entity.
- The editor preview host already loads the built game assembly (`--assembly`) through the generated `GeneratedEditorPreviewBridge`. It runs only `[ExecuteInEditor]` systems and does not render.

Steps, each verified before the next:
1. **Runtime: external surface and editor camera.** `EngineHost` renders into a given HWND instead of creating a window, and an editor camera (a world pose plus projection) replaces the scene camera at the point where `LevelLoad` applies it. Camera-relative transforms subtract only the camera's fixed-point translation, so both cameras share one path.
   - **Oracle:** on WARP, the editor camera renders the same pixels as a scene camera placed at the same pose.
   - **Result:** `EngineHost.UseExternalSurface` and `SetEditorCamera`. `EditorRuntimeViewTests` passes, and fails with the override disabled.
2. **Generated presentation frame.** The generated loop gains a presentation-only frame: scene instantiation and Presentation-phase systems, but no input, simulation or physics. The bridge exposes a runtime view: initialize with a surface, render with a camera, resize, shut down.
   - **Result:** `GeneratedGameLoop.RunPresentationFrame` and `GeneratedEditorPreviewBridge.InitializeRuntimeView`, `RenderRuntimeView`, `ResizeRuntimeView`, `CaptureRuntimeView` and `ShutdownRuntimeView`, emitted for games that use `EngineHost`.
   - The view's world is every startup scene in order plus the edited scene.
   - `EditorRuntimeViewBridgeTests` renders UserEcsProject's world on WARP with the runtime sky from two designer cameras, and after a resize.
3. **Preview host runtime session.** It implements the existing session contract (camera, overlay, selection and resize per frame) on the runtime's `RenderServer`. The VSIX opens it for the pinned scene's project when the game assembly is built.
4. **Edits.** Value edits (transforms, members) go to the running world. A structural edit that needs an archetype the build does not have is reported as "build to see this change", never drawn wrong.

## Oracles
- **One-line diffs:** one gizmo or Inspector edit changes only the edited value's text.
- **Build equivalence:** for every converted scene, the C# generated from JSON builds the same world as the C# scene (component types and values per node).
  - **C3a result:** all 29 scenes in 12 projects matched. The generated code was compared before and after conversion with archetypes named by their component lists; number spelling, digit separators and inlined asset constants were normalized.
  - The whole-project archetype sets are identical. Only their registration order changed, with scene file order; see ADR-0070.
- **Undo round trip:** Undo/Redo/Save/reload restores byte-identical JSON.
- **Runtime image in the editor:** the Scene view of the ThirdPerson level matches the runtime frame from the same camera on WARP, within a pixel threshold.
