# UnrealExport

Dev-time tool (ADR-0017): exports `G:\UE\5.8\WFC\Content\FirstPerson\Lvl_FirstPerson.umap` into
`UserGameProject/Assets/Stage/Lvl_FirstPerson/` as a single textured glTF binary plus a small
JSON manifest for the handful of things the GLTFExporter plugin cannot see on its own.

Not part of the C# solution - requires a local Unreal Engine 5.8 editor install and is never run
by the game or by CI. Re-run it only when the source UE level changes.

## Usage

```bash
tools\UnrealExport\run_export.cmd
```

Requires:
- Unreal Engine 5.8 editor installed at `C:\Program Files\Epic Games\UE_5.8`
- `G:\UE\5.8\WFC\WFC.uproject` present (the source UE project)

## Output

- `UserGameProject/Assets/Stage/Lvl_FirstPerson/Lvl_FirstPerson.glb` - the whole level as one
  glTF binary: one node per StaticMeshActor (baked material honoring per-actor overrides, UE
  cm/Z-up converted to glTF m/Y-up by the exporter itself) plus a `KHR_lights_punctual`
  DirectionalLight node.
- `UserGameProject/Assets/Stage/Lvl_FirstPerson/scene_meta.json` - SkyLight ambient
  approximation, ExponentialHeightFog params, PlayerStart transform (the one manually-converted
  value - PlayerStart isn't an exportable glTF object type), and a mobility tag per actor label.

## Acceptance check

The script logs `StaticMeshActor count` and `DirectionalLight count` from
`get_all_level_actors()`. Expected: 52 StaticMeshActors (20 SM_Cube + 20 SM_QuarterCylinder + 12
SM_Ramp) and 1 DirectionalLight. If World Partition only loaded a subset, the count will be
short - see ADR-0017 for the WorldPartitionEditorSubsystem cell-forcing fallback.
