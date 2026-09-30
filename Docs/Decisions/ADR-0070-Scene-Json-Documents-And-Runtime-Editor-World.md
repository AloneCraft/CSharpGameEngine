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

1. **Scenes are `*.scene.json` documents.**
   - Each file declares `"$schema"` pointing at `schemas/cge-scene.schema.json`, so the VS JSON editor completes and validates it. Its `schemaVersion` is 1.
   - It holds the scene's namespace, class name, optional startup order and a node tree.
   - Each node has a stable `id`, a `name`, `components` and `children`.
   - A component is its C# type's full name plus member values.
2. **Values follow the member's declared C# type.** There are no tags in the JSON.
   - Numbers, booleans and strings use their JSON forms. Enums are member-name strings.
   - A struct value is a JSON object. The object's keys are either settable members (an object initializer) or the parameter names of a public constructor.
   - Floats are written in their shortest round-trip form. The writer changes only the values that were edited and keeps key order, so one edit is a one-line diff.
3. **C# is generated at build time.**
   - `GameSystemGenerator` reads `*.scene.json` from `AdditionalFiles` and builds the existing `SceneEmitModel`. The emitters are unchanged, and the generated C# is compiled in the same build.
   - With `EmitCompilerGeneratedFiles` the C# is also written to `obj/Generated` for inspection.
   - Type and value errors are compiler diagnostics (`SCENE2xx`) on the JSON file.
   - `Game.props` and the SDK package include `**/*.scene.json` by default.
4. **Undefined types and members are caught while typing and fail the build.** (User choice, 2026-10-01.)
   - **While typing:** the generator builds a per-project JSON Schema from the same Roslyn component rules. It lists every usable component type, and for each type its settable members and constructor parameters with their value shapes; `additionalProperties` is false. The schema is embedded as a generated constant. A post-build step writes it to `.cge/scene.schema.json`, and scene files point `"$schema"` at it. The VS JSON editor then warns about unknown types, unknown members and wrong value kinds as the user types, and completes names.
   - **At build:** the generator reports errors at the JSON line and column. SCENE201 is an unknown type, SCENE202 an unknown member or constructor parameter, SCENE203 a value of the wrong kind, SCENE204 a missing key or malformed JSON, and SCENE205 a duplicate component. A scene that cannot be generated is never built with values silently dropped.
   - **In the Scene Designer:** the same checks show as warnings. Unknown entries are preserved, so saving never loses them.
5. **The editor owns the scene model until Save.**
   - The Scene Designer is a custom editor for `*.scene.json` implementing `IVsPersistDocData`, like the Shader Graph editor.
   - Every gizmo, Inspector and Hierarchy operation is a command on an editor Undo/Redo stack and applies immediately. The document becomes dirty; Save and Save All write the JSON.
   - If the file changes on disk while it is open, the editor asks to reload, as VS does for text.
   - Roslyn is used only for component type metadata, for the Inspector and for validation. It is not on the edit path.
6. **The editor world renders with the game runtime.**
   - The preview host loads the built game assembly and instantiates the world from the editor's in-memory JSON by reflection, with the same schema rules as the generator.
   - It runs the game's Presentation-phase systems, then `EngineHost` and `RenderServer` draw into the Scene view surface. Environment, lights, materials, skinning, exposure and GI are therefore the runtime's own.
   - Edits are sent as document updates. New component types need a build, as Unity scripts do.
7. **Cutover.** A converter turns the existing C# scenes into JSON. The C# `IScene` scene path is then removed; there are no parallel readers after the milestone, as [Editor-VSIX](../Specifications/Editor-VSIX.md) already requires for format changes.

## Consequences

- Editing no longer compiles the project per gesture, and Undo works on scene operations.
- A scene can no longer be written as C# code. Gameplay stays in C#, and scene values are validated at build time.
- The design-time image is the runtime image. New runtime rendering features reach the editor without editor work.
- `SceneSourceTruthArchitectureTests` and the `vsix.source-sync` stage are replaced by tests of the JSON document, the Undo stack and the editor world.
- The UE importer (`tools/CSharpGameEngine.Unreal.SceneImporter`) emits JSON scenes.

## Verification

Tracked in [M0093](../Milestones/M0093-Scene-Json-And-Editor-World.md).
