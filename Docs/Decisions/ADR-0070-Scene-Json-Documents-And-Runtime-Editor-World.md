# ADR-0070 — Scene JSON documents, editor-owned scene model and a runtime-rendered editor world

- Status: Accepted
- Date: 2026-10-01
- Supersedes: the source-of-truth and Undo parts of [ADR-0054](ADR-0054-Editor-Authoring-Host-And-Source-Synchronization.md) and [M0070](../Milestones/M0070-CSharp-Source-Synchronization-V2.md). The versioned out-of-process host protocol of ADR-0054 stays.

## Context

The Scene Designer writes every gesture into the live C# scene file. Each commit compiles the project twice more and re-parses the buffer, so editing feels slow. Undo is VS text Undo, and the design-time image comes from a separate, simplified path.
- **Rendering gap:** that path draws a fixed light rig, base colours only, no environment, no textures and no skinning. The design-time image is far from the game.

The user asked for an Unreal/Unity-like editor:
- Undo/Redo that works on scene operations.
- Fast edits that stay in memory until Save.
- Scenes stored as JSON with a schema and turned into C# at build time.
- A design-time view that matches the running game.

**Earlier attempts and why they were rolled back:**

| Attempt | Removed in | Reason |
|---|---|---|
| JSON scenes (M0049/M0050) | M0051 | The writer reformatted every value, so one gizmo move rewrote 565 lines. Moving off C# also lost IntelliSense and compiler diagnostics on scene values. |
| In-memory editing (M0047) | M0070 | It was layered on the C# text buffer and VS text Undo. Watermarks, landing states and save-time replay produced repeated regressions. |

## Decision

1. **Scenes are JSON documents under the project's `Scenes` folder: `Scenes\**\*.json`.** (Revised 2026-10-01 at the user's request: the files were `*.scene.json` until then.)
   - A scene file needs no special extension. Which `.json` is a scene is decided by its folder, relative to the project: `SceneFiles.IsScene` in the shared SceneModel sources is the one rule. The build includes exactly `Scenes\**\*.json`; the generator applies the rule with the build's `ProjectDir` (a `CompilerVisibleProperty`), and the Visual Studio extension with the project's file or, given only a path, the nearest folder with a `*.csproj`. JSON elsewhere (`appsettings.json`, assets) is never a scene.
   - A converted C# scene goes to `Scenes/<ClassName>.json`, next to its source when that is already under `Scenes`.
   - Each file declares `"$schema"` pointing at `schemas/cge-scene.schema.json`, so the VS JSON editor completes and validates it. Its `schemaVersion` is 3: every number inside an `[Angle]` member is in turns, under a key without the C# unit word (ADR-0074). Versions 1 (each member's own unit) and 2 (C# names as keys) are still read, and `SceneJson.Cli upgrade` rewrites them.
   - It holds the scene's namespace, class name, optional startup order and a node tree.
   - Each node has a stable `id`, a `name`, `components` and `children`.
   - A component is its C# type's full name plus member values.
2. **Values follow the member's declared C# type.** There are no tags in the JSON.
   - Numbers, booleans and strings use their JSON forms. Enums are member-name strings.
   - A struct value is a JSON object. The object's keys are either settable members (an object initializer) or the parameter names of a public constructor.
   - Floats are written in their shortest round-trip form. The writer changes only the values that were edited and keeps key order, so one edit is a one-line diff.
3. **C# is generated at build time.**
   - `GameSystemGenerator` reads the scene files from `AdditionalFiles` and builds the existing `SceneEmitModel`. The emitters are unchanged, and the generated C# is compiled in the same build.
   - With `EmitCompilerGeneratedFiles` the C# is also written to `obj/Generated` for inspection.
   - Type and value errors are compiler diagnostics (`SCENE2xx`) on the JSON file.
   - `Game.props` and the SDK package include `Scenes\**\*.json` by default.
4. **Undefined types and members are caught while typing and fail the build.** (User choice, 2026-10-01.)
   - **While typing:** the generator builds a per-project JSON Schema from the same Roslyn component rules. It lists every usable component type, and for each type its settable members and constructor parameters with their value shapes; `additionalProperties` is false. The schema is embedded as a generated constant. A post-build step writes it to `.cge/scene.schema.json`, and scene files point `"$schema"` at it. The VS JSON editor then warns about unknown types, unknown members and wrong value kinds as the user types, and completes names.
   - **At build:** the generator reports errors at the JSON line and column. SCENE201 is an unknown type, SCENE202 an unknown member or constructor parameter, SCENE203 a value of the wrong kind or one the runtime or the compiler would reject (a physical light value outside its range, a number beyond its float, double or decimal member, a LogicalScalar beyond Q64.64; added 2026-10-04 after such a scene built and then threw on instantiation), SCENE204 a missing key or malformed JSON, and SCENE205 a duplicate component. A scene that cannot be generated is never built with values silently dropped.
   - **In the Scene Designer:** the same checks are errors, as at build (revised 2026-10-01, M0095: this said "warnings, and unknown entries are preserved", which the Designer never did). A document with an unknown key, member or type is not analyzed and nothing is written to it, so nothing is lost; it is fixed in the JSON view, where the schema shows the same problems.
5. **Edits stay in memory until Save, in the JSON document's VS text buffer.** (Revised 2026-10-01 during C2.)
   - The Scene Designer reads a JSON scene through its equivalent C# class (`SceneJsonDesignerSource`), analyzed in the project's Roslyn compilation with the JSON path. The Hierarchy, Inspector, gizmo and scene catalog therefore work on JSON and C# scenes with the same code.
   - Each completed gizmo, Inspector or Hierarchy gesture is applied to the scene model and written back with `SceneJsonConverter` (`SceneJsonCommitPlanner`). Node ids are kept, the result is re-read to verify it, and only the changed lines go into the buffer. The write is one VS Undo unit.
   - Nothing is written to disk per gesture. The buffer is the in-memory document: the tab shows `*`, and Ctrl+S, Save All and the Scene Designer's Save write the file. Undo/Redo steps one gesture at a time, from the Scene view or the JSON editor. A change on disk while the file is open uses VS's own reload prompt.
   - No project compile is on the gesture path. A gesture parses one generated class, and the full re-analysis runs in the background afterwards.
   - The Scene Designer's document window is the Scene Editor (ADR-0071), whose document data is this buffer; its JSON view is the JSON editor over it.
   - **Why not a custom `IVsPersistDocData` editor with its own Undo stack** (the first plan): a second in-memory copy next to the text buffer brings back M0047's two-owner problems (watermarks, landing states, replay on save). With the buffer as the only copy, the JSON editor and the Scene Designer always show the same document, and Save, dirty state, reload and Undo come from VS.
6. **The editor world renders with the game runtime.**
   - The preview host loads the built game assembly and instantiates its world with the generated code: the startup scenes and the edited scene, as the game would.
   - It runs the game's Presentation-phase systems, then `EngineHost` and `RenderServer` draw into the Scene view surface. Environment, lights, materials, skinning, exposure and GI are therefore the runtime's own.
   - Edits are sent as the editor's in-memory JSON. Its component values are applied to the running world by reflection, with the same schema rules as the generator. A gizmo drag writes nothing until it ends, so each frame carries the dragged roots' parent-relative transforms (`RenderPreviewFrame.TransformOverrides`, M0096), applied to their `Transform` the same way; a cancel resends the document. A structural change (a node, or a component type the node was not built with) needs a build, as Unity scripts do, and the Scene view says so; it is never drawn wrong. A new build reopens the view.
   - Until the game is first built, or if the runtime view cannot open, the Scene view shows the simplified scene preview.
7. **Cutover.** A converter (`CSharpGameEngine.SceneJson.Cli convert`) turns the existing C# scenes into JSON. The C# `IScene` scene path is then removed; there are no parallel readers after the milestone, as [Editor-VSIX](../Specifications/Editor-VSIX.md) already requires for format changes.
   - The generator builds scenes only from scene JSON.
   - A C# scene class, or a leftover `[StartupScene]`, is error SCENE206, whose message names the converter command. An unconverted scene therefore cannot silently disappear from the game.
   - Two JSON startup scenes with the same `startupOrder` are SCENE118, reported at the later document's value.
   - The Scene Designer opens and writes only scene JSON. The C# scene writer (`SceneSourceEditor`) and its save verification are removed.
   - The C# scene DSL stays as the designer's internal reading of a JSON scene (its equivalent class, `SceneJsonDesignerSource`), which never enters the build.

## Consequences

- Editing no longer compiles the project per gesture, and Undo works on scene operations.
- A scene can no longer be written as C# code. Gameplay stays in C#, and scene values are validated at build time.
- The design-time image is the runtime image. New runtime rendering features reach the editor without editor work.
- `SceneSourceTruthArchitectureTests` stays: its invariants (one authoring copy, no mirrored Undo ledger, one native Undo unit, canonical re-read) now guard the JSON buffer. The `vsix.source-sync` stage runs the JSON commit and workspace tests in place of the C# writer's.
- The UE importer (`tools/CSharpGameEngine.Unreal.SceneImporter`) still writes C# scene classes as an intermediate. Its export scripts convert them to JSON with `CSharpGameEngine.SceneJson.Cli convert` in the same run.
- Each scene is its own file, and scene archetypes are registered in scene file order. Archetype numbers and component indices can therefore differ from the old C# layout, where several scenes shared one file. Nodes, components and values are unchanged; this was checked for every converted scene (M0093 C3a).
- The Scene palette and schema list an engine struct only when it carries `[SceneComponentOrder]` or `[SceneComponentIcon]`. Engine types that scenes author directly must be marked; `PhysicsRotation`, `PhysicsVelocity` and `PhysicsCachePlayback` gained the marker in C3a.

## Verification

Tracked in [M0093](../Milestones/M0093-Scene-Json-And-Editor-World.md).
