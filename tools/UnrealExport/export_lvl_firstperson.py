"""
UE5.8 Lvl_FirstPerson -> UserGameProject export script (ADR-0017).

Run via UnrealEditor-Cmd.exe against G:\\UE\\5.8\\WFC\\WFC.uproject (see run_export.cmd).
Not part of the C# solution; lives outside src/ on purpose (a dev-time authoring tool that
requires the Unreal Editor, not something CI or the game itself ever executes).

What this script exports and why:
  1. The entire level as ONE glTF binary (.glb) via UGLTFExporter.ExportToGLTF(World, ...).
     Passing the UWorld object (not individual StaticMesh assets) makes UE itself do:
       - unit/axis conversion (cm/Z-up/left-handed -> m/Y-up/right-handed) per node transform
       - per-actor material *override* resolution and UV-space baking (bBakeMaterialInputs =
         UseMeshData) - this is why the whole level is exported instead of the 3 unique meshes:
         exporting bare StaticMesh assets would use each mesh's *default* material slot and
         miss any per-component override the level actually uses.
       - DirectionalLight -> KHR_lights_punctual (bExportLights = True), with direction baked
         into the light node's transform and Intensity/Color copied through unconverted
         (verified against Engine/Plugins/Enterprise/GLTFExporter source - no lux/candela
         rescale is applied by the exporter itself).
  2. A small supplementary scene_meta.json for exactly the things GLTFExporter cannot see,
     because they aren't StaticMeshActor/Light actors: SkyLight (ambient approximation),
     ExponentialHeightFog, PlayerStart (the one place this script does its own UE->glTF
     coordinate conversion), and a mobility tag per actor label (glTF has no such concept).
"""

import json
import os
import sys

import unreal

LEVEL_ASSET_PATH = "/Game/FirstPerson/Lvl_FirstPerson"
REPO_ROOT = "G:/GitHub/CSharpGameEngine"
OUTPUT_DIR = f"{REPO_ROOT}/UserGameProject/Assets/Stage/Lvl_FirstPerson"
GLB_PATH = f"{OUTPUT_DIR}/Lvl_FirstPerson.glb"
SCENE_META_PATH = f"{OUTPUT_DIR}/scene_meta.json"

# Actors to force into the "movable" archetype for the static/movable demo (Phase 4). The level
# is expected to be 100% Static/Stationary mobility otherwise. Adjust these labels after Phase 1
# acceptance once the real actor labels in the exported glTF node names are confirmed.
MOBILITY_OVERRIDE_LABELS = {
    "SM_Cube16": "movable",
    "SM_Cube17": "movable",
}


def log(message: str) -> None:
    unreal.log(f"[UnrealExport] {message}")


def log_warning(message: str) -> None:
    unreal.log_warning(f"[UnrealExport] {message}")


def log_error(message: str) -> None:
    unreal.log_error(f"[UnrealExport] {message}")


def ue_to_gltf_translation(location: unreal.Vector) -> list:
    # UE centimeters, left-handed Z-up -> glTF meters, right-handed Y-up.
    # Matches Epic's own FGLTFCoreUtilities convention: (x, z, y) * scale, Y negated for handedness.
    scale = 0.01
    return [location.x * scale, location.z * scale, -location.y * scale]


def ue_yaw_to_gltf_yaw_radians(rotation: unreal.Rotator) -> float:
    import math
    # UE yaw is left-handed degrees around +Z; glTF/engine yaw here is right-handed radians
    # around +Y. Negate for handedness flip, matching the translation's Y negation above.
    return -math.radians(rotation.yaw)


def load_level() -> unreal.World:
    level_subsystem = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    ok = level_subsystem.load_level(LEVEL_ASSET_PATH)
    if not ok:
        raise RuntimeError(f"LevelEditorSubsystem.load_level failed for '{LEVEL_ASSET_PATH}'")

    editor_subsystem = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem)
    world = editor_subsystem.get_editor_world()
    if world is None:
        raise RuntimeError("UnrealEditorSubsystem.get_editor_world() returned None after load_level")
    return world


def get_all_actors() -> list:
    actor_subsystem = unreal.get_editor_subsystem(unreal.EditorActorSubsystem)
    return actor_subsystem.get_all_level_actors()


def try_get_component(actor: unreal.Actor, property_name: str):
    try:
        return actor.get_editor_property(property_name)
    except Exception as ex:  # noqa: BLE001 - best-effort metadata extraction, never fatal
        log_warning(f"get_editor_property('{property_name}') failed on {actor.get_actor_label()}: {ex}")
        return None


def get_mobility_string(actor: unreal.Actor) -> str:
    try:
        root = actor.root_component
        if root is None:
            return "static"
        mobility = root.mobility
        if mobility == unreal.ComponentMobility.MOVABLE:
            return "movable"
        return "static"
    except Exception as ex:  # noqa: BLE001
        log_warning(f"Could not read mobility for {actor.get_actor_label()}: {ex}")
        return "static"


def export_level_glb(world: unreal.World) -> None:
    options = unreal.GLTFExportOptions()
    options.export_uniform_scale = 0.01
    options.bake_material_inputs = unreal.GLTFMaterialBakeMode.USE_MESH_DATA
    options.default_material_bake_size = unreal.GLTFMaterialBakeSize(x=1024, y=1024)
    options.export_lights = True

    # The Blueprint signature has 5 params (Object, FilePath, Options, SelectedActors, OutMessages&)
    # and a bool return, but OutMessages is a non-const reference output param: UE's Python binding
    # generator drops it from the call *and* replaces the bool return with the out-param itself
    # (confirmed empirically - a 5th positional arg raises "takes at most 4 arguments", and
    # unpacking the single-object return as a tuple raises "cannot unpack non-iterable
    # GLTFExportMessages object"). Success is therefore judged solely from messages.errors below.
    messages = unreal.GLTFExporter.export_to_gltf(world, GLB_PATH, options, set())

    for suggestion in messages.suggestions:
        log(f"suggestion: {suggestion}")
    for warning in messages.warnings:
        log_warning(str(warning))
    for error in messages.errors:
        log_error(str(error))

    if len(messages.errors) > 0:
        raise RuntimeError(f"GLTFExporter reported {len(messages.errors)} error(s); see log above")

    log(f"Exported level GLB: {GLB_PATH}")


def build_scene_meta(actors: list) -> dict:
    static_mesh_actor_count = 0
    directional_light_count = 0
    ambient = None
    fog = None
    player_start = None
    mobility_by_label = {}

    for actor in actors:
        label = actor.get_actor_label()

        if isinstance(actor, unreal.StaticMeshActor):
            static_mesh_actor_count += 1
            mobility_by_label[label] = get_mobility_string(actor)

        elif isinstance(actor, unreal.DirectionalLight):
            directional_light_count += 1

        elif isinstance(actor, unreal.SkyLight):
            component = try_get_component(actor, "light_component")
            if component is not None:
                color = component.light_color
                ambient = {
                    "colorRgb": [color.r / 255.0, color.g / 255.0, color.b / 255.0],
                    "intensity": component.intensity,
                }

        elif isinstance(actor, unreal.ExponentialHeightFog):
            component = try_get_component(actor, "component")
            if component is not None:
                fog = {
                    "density": component.fog_density,
                    "heightFalloff": component.fog_height_falloff,
                    # Property name to confirm at run time - FogInscatteringColor is deprecated
                    # in 5.8; fall back to a neutral gray if the active property can't be read.
                    "colorRgb": [0.6, 0.65, 0.7],
                }

        elif isinstance(actor, unreal.PlayerStart):
            transform = actor.get_actor_transform()
            player_start = {
                "translation": ue_to_gltf_translation(transform.translation),
                "yawRadians": ue_yaw_to_gltf_yaw_radians(transform.rotation.rotator()),
            }

    for label, mobility in MOBILITY_OVERRIDE_LABELS.items():
        if label in mobility_by_label:
            mobility_by_label[label] = mobility
        else:
            log_warning(f"MOBILITY_OVERRIDE_LABELS references unknown actor label '{label}'")

    log(f"StaticMeshActor count: {static_mesh_actor_count}")
    log(f"DirectionalLight count: {directional_light_count}")

    return {
        "version": 1,
        "conventions": {"units": "meters", "handedness": "right", "up": "+Y"},
        "ambient": ambient or {"colorRgb": [0.15, 0.15, 0.18], "intensity": 1.0},
        "fog": fog,
        "playerStart": player_start,
        "mobilityOverrides": mobility_by_label,
        "clearColorRgb": [0.35, 0.55, 0.85],
        "actorCounts": {
            "staticMeshActors": static_mesh_actor_count,
            "directionalLights": directional_light_count,
        },
    }


def main() -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    world = load_level()
    actors = get_all_actors()
    log(f"get_all_level_actors() returned {len(actors)} actor(s)")

    export_level_glb(world)

    scene_meta = build_scene_meta(actors)
    with open(SCENE_META_PATH, "w", encoding="utf-8") as f:
        json.dump(scene_meta, f, indent=2)
    log(f"Wrote scene metadata: {SCENE_META_PATH}")

    log("Export complete.")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top-level: make failures visible in the commandlet log
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
