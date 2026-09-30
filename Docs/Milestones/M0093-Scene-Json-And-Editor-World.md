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
| C1 | JSON DOM, reader and span-preserving writer in the shared SceneModel sources; `schemas/cge-scene.schema.json`; the generator reads `*.scene.json` from `AdditionalFiles`; `Game.props`/SDK include them; a C#-scene → JSON converter | In progress |
| C1b | Per-project JSON Schema from the generator (embedded constant, written to `.cge/scene.schema.json` after build) for editor warnings and completion; build errors SCENE201-205 at JSON positions | Planned |
| C2 | `*.scene.json` editor: in-memory model, command Undo/Redo, dirty state, Save/Save All, reload on external change; Inspector/Hierarchy/gizmo on the model | Planned |
| C3 | Convert UserEcsProject, the ThirdPerson template, FeatureStages and the UE importer output; remove the C# `IScene` scene path and its guard tests | Planned |
| B1 | Preview host instantiates the editor world from the in-memory JSON inside the game assembly and renders it with the runtime's Presentation systems | Planned |
| B2 | Keep the device and swap chain across edits; exposure/GI history survives edits | Planned |

## Oracles
- **One-line diffs:** one gizmo or Inspector edit changes only the edited value's text.
- **Build equivalence:** for every converted scene, the C# generated from JSON builds the same world as the C# scene (component types and values per node).
- **Undo round trip:** Undo/Redo/Save/reload restores byte-identical JSON.
- **Runtime image in the editor:** the Scene view of the ThirdPerson level matches the runtime frame from the same camera on WARP, within a pixel threshold.
