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
| C3b | Remove the user-authored C# `IScene` path: the generator reads scenes only from JSON, a C# scene class is SCENE206, and JSON `startupOrder` conflicts are SCENE118. The VSIX discovers, opens and writes only `*.scene.json`, and the C# scene writer is removed. Tests keep writing scenes in the C# DSL; their helpers convert them the way the CLI does. Model predictions are checked by writing JSON and reading it back | Done (457f7f0) |
| B1 | The Scene view renders the built game's world with the runtime, and the editor's in-memory JSON edits its values (steps below) | Done: steps 1 (f38e9f4), 2 (a6a1e55), 3a (aaa81f2), 4a (81e8a4e), 4b (5e4afe7), selection and gizmos (72140e0) and 3b (this commit); a manual VS check is pending |
| B2 | Keep the device and swap chain across edits; exposure/GI history survives edits | Done for the runtime view (by construction, below). The scene preview, shown only while the game is not built, still recreates its device on a structural edit |

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
   - **3a result:** preview protocol 6.
     - An Open request may carry a `RuntimeView` (game assembly, content directory, scene), and cameras carry their rotation and projection.
     - `RenderPreviewRuntimeSession` loads the game into a collectible load context and drives the bridge.
     - The runtime's content paths now resolve against `ContentRoot`, which the view sets to the game build's directory.
     - `RenderPreviewRuntimeSessionTests`: over the protocol the session renders exactly the bridge's pixels, and it explains a scene missing from the build and a build without a runtime view.
   - **Selection and gizmos (72140e0):** the frame's selected node paths highlight the renderer objects their entities became, and the designer's gizmos are drawn over the runtime image in camera-relative space. The Scene view's look-at camera is sent as a rotation, field of view and planes; `EditorCameraConventionTests` pins that conversion to the engine's camera basis.
   - **3b result:** the Scene view opens the runtime view for a JSON scene whose game is built, and falls back to the scene preview otherwise.
     - The game build (the Debug output found the way the editor preview host finds it) is copied to a temporary folder, so a rebuild can overwrite the output while the view runs. Content still resolves against the build's own folder.
     - Open and every ReplaceScene send the scene's live JSON, unsaved edits included (`SceneWorkspaceService.TryGetLiveText`).
     - A structural edit shows "build to see this change" in the Scene view's status (`SceneRuntimeViewBuildRequired`). A failed open falls back to the scene preview with the reason (`SceneRuntimeViewUnavailable`) and is not retried until the build changes.
     - Once a second the view compares the build's write time with the one shown. A new build, held for one check so a build in progress is not copied, reopens the view without another edit; the first build of an unbuilt game switches from the scene preview.
     - A runtime-view request may take up to 120 seconds, since opening loads the game's level.
     - `PackagedRuntimeViewTests`: the VSIX's packaged host, driven by the VSIX client, opens UserEcsProject's runtime view, shows a moved box, flags an added node as needing a build, and restores the exact pixels.
4. **Edits.** Value edits (transforms, members) go to the running world. A structural edit that needs an archetype the build does not have is reported as "build to see this change", never drawn wrong.
   - **4a result:** the generated `World.TrySetComponent` replaces a value in place and marks it changed. `World.GetSceneEntities` lists a scene instance's entities in node pre-order. The bridge's `TrySetRuntimeViewComponent` applies a value to a node of the edited scene.
   - **4b result:**
     - Open and ReplaceScene carry the edited scene's JSON (`SceneJson`).
     - `SceneJsonRuntimeValues` turns each component's members into the game's own type by reflection with the generator's rules. For every component of three UserEcsProject scenes it equals what the generated code created.
     - The session applies the values. A node count or component type the build does not have sets `BuildRequired` in the response, and an unreadable document changes nothing.
     - `SceneDocument_EditsValuesLiveAndFlagsStructureForABuild`: moving the box changes the image, and restoring the document restores the exact pixels.
     - **Known gap:** a component removed in the document stays in the running world until the next build, and it does not set `BuildRequired`.
     - **Known gap:** during a gizmo drag the runtime image updates only when the gesture is committed.

## B2 result (2026-10-01)
- The runtime view's session keeps one `EngineHost` and `RenderServer` from Open to Close. ReplaceScene only applies component values to the running world, so the device, swap chain, auto-exposure and GI history are never rebuilt by an edit. Only a new build reopens the view.
- The scene preview keeps its device-recreating ReplaceScene. It is the fallback before the game is first built; making it keep its device would duplicate what the runtime view now does.

## Oracles
- **One-line diffs:** one gizmo or Inspector edit changes only the edited value's text.
- **Build equivalence:** for every converted scene, the C# generated from JSON builds the same world as the C# scene (component types and values per node).
  - **C3a result:** all 29 scenes in 12 projects matched. The generated code was compared before and after conversion with archetypes named by their component lists; number spelling, digit separators and inlined asset constants were normalized.
  - The whole-project archetype sets are identical. Only their registration order changed, with scene file order; see ADR-0070.
- **Undo round trip:** Undo/Redo/Save/reload restores byte-identical JSON.
- **Runtime image in the editor:** the Scene view of the ThirdPerson level matches the runtime frame from the same camera on WARP, within a pixel threshold.
