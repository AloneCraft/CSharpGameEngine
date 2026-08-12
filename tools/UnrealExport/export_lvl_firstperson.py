"""UE 5.8 FirstPerson level -> C# Scene DSL import staging data.

The Unreal project is opened read-only. The source level is never saved: actors are temporarily
placed at identity only while a selected-actor glTF is exported and are restored in a finally
block. A whole-level GLB is written only below .artifacts as a coordinate-conversion oracle for
the .NET generator; it is not a runtime asset.
"""

import hashlib
import json
import os
import re
import sys

import unreal

LEVEL_ASSET_PATH = "/Game/FirstPerson/Lvl_FirstPerson"
REPO_ROOT = "G:/GitHub/CSharpGameEngine"
OUTPUT_DIR = f"{REPO_ROOT}/UserEcsProject/Assets/Stage/Lvl_FirstPerson"
MESH_OUTPUT_DIR = f"{OUTPUT_DIR}/Meshes"
STAGING_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_FirstPerson"
STAGING_GLB_PATH = f"{STAGING_DIR}/Lvl_FirstPerson.staging.glb"
MANIFEST_PATH = f"{STAGING_DIR}/manifest.json"


def log(message: str) -> None:
    unreal.log(f"[UnrealExport] {message}")


def log_warning(message: str) -> None:
    unreal.log_warning(f"[UnrealExport] {message}")


def log_error(message: str) -> None:
    unreal.log_error(f"[UnrealExport] {message}")


def require_force_for_existing_output() -> None:
    if not os.path.exists(MANIFEST_PATH) and not os.path.exists(MESH_OUTPUT_DIR):
        return
    if os.environ.get("CSGE_UNREAL_EXPORT_FORCE") != "1":
        raise RuntimeError(
            "Output already exists. Re-run tools/UnrealExport/run_export.cmd --force "
            "to replace generated import data."
        )


def load_level() -> unreal.World:
    subsystem = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not subsystem.load_level(LEVEL_ASSET_PATH):
        raise RuntimeError(f"Could not load '{LEVEL_ASSET_PATH}'")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    if world is None:
        raise RuntimeError("get_editor_world returned None")
    return world


def all_level_actors() -> list:
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
    return sorted(actors, key=lambda actor: actor.get_actor_label())


def gltf_options() -> unreal.GLTFExportOptions:
    options = unreal.GLTFExportOptions()
    options.export_uniform_scale = 0.01
    options.bake_material_inputs = unreal.GLTFMaterialBakeMode.USE_MESH_DATA
    options.default_material_bake_size = unreal.GLTFMaterialBakeSize(x=1024, y=1024)
    options.export_lights = True
    return options


def export_gltf(world: unreal.World, path: str, selected_actors: set) -> None:
    messages = unreal.GLTFExporter.export_to_gltf(world, path, gltf_options(), selected_actors)
    for suggestion in messages.suggestions:
        log(f"suggestion: {suggestion}")
    for warning in messages.warnings:
        log_warning(str(warning))
    for error in messages.errors:
        log_error(str(error))
    if len(messages.errors) > 0:
        raise RuntimeError(f"GLTFExporter reported {len(messages.errors)} error(s) for '{path}'")


def static_mesh_component(actor: unreal.StaticMeshActor):
    components = actor.get_components_by_class(unreal.StaticMeshComponent)
    if len(components) != 1:
        raise RuntimeError(
            f"StaticMeshActor '{actor.get_actor_label()}' has {len(components)} StaticMeshComponents"
        )
    return components[0]


def render_variant(actor: unreal.StaticMeshActor) -> tuple:
    component = static_mesh_component(actor)
    mesh = component.get_editor_property("static_mesh")
    if mesh is None:
        raise RuntimeError(f"StaticMeshActor '{actor.get_actor_label()}' has no StaticMesh")
    materials = []
    for index in range(component.get_num_materials()):
        material = component.get_material(index)
        materials.append(material.get_path_name() if material is not None else "")
    return mesh.get_path_name(), tuple(materials)


def safe_asset_stem(source_mesh_path: str, material_paths: tuple) -> str:
    mesh_name = source_mesh_path.rsplit("/", 1)[-1].split(".", 1)[0]
    sanitized = re.sub(r"[^A-Za-z0-9_]", "_", mesh_name).strip("_") or "StaticMesh"
    digest_source = json.dumps([source_mesh_path, list(material_paths)], separators=(",", ":"))
    digest = hashlib.sha256(digest_source.encode("utf-8")).hexdigest()[:10]
    return f"{sanitized}_{digest}"


def mobility(actor: unreal.StaticMeshActor) -> str:
    component = static_mesh_component(actor)
    return "Movable" if component.mobility == unreal.ComponentMobility.MOVABLE else "Static"


def export_variant_at_identity(world: unreal.World, actor: unreal.StaticMeshActor, output_path: str) -> None:
    original = actor.get_actor_transform()
    identity = unreal.Transform(
        location=unreal.Vector(0.0, 0.0, 0.0),
        rotation=unreal.Rotator(0.0, 0.0, 0.0),
        scale=unreal.Vector(1.0, 1.0, 1.0),
    )
    try:
        if not actor.set_actor_transform(identity, False, True):
            raise RuntimeError(f"Could not temporarily reset '{actor.get_actor_label()}'")
        export_gltf(world, output_path, {actor})
    finally:
        actor.set_actor_transform(original, False, True)


def try_property(value, name: str):
    try:
        return value.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001 - UE versions rename some editor properties
        log_warning(f"Could not read {value.get_name()}.{name}: {ex}")
        return None


def ue_to_engine_translation(location: unreal.Vector) -> list:
    return [location.x * 0.01, location.z * 0.01, -location.y * 0.01]


def build_environment(actors: list) -> dict:
    import math

    ambient = None
    fog = None
    player_start = None
    directional_light_count = 0

    for actor in actors:
        if isinstance(actor, unreal.DirectionalLight):
            directional_light_count += 1
        elif isinstance(actor, unreal.SkyLight):
            component = try_property(actor, "light_component")
            if component is not None:
                color = component.light_color
                ambient = {
                    "name": actor.get_actor_label(),
                    "colorRgb": [color.r / 255.0, color.g / 255.0, color.b / 255.0],
                    "intensity": component.intensity,
                }
        elif isinstance(actor, unreal.ExponentialHeightFog):
            component = try_property(actor, "component")
            if component is not None:
                fog = {
                    "name": actor.get_actor_label(),
                    "density": component.fog_density,
                    "heightFalloff": component.fog_height_falloff,
                    "colorRgb": [0.6, 0.65, 0.7],
                }
        elif isinstance(actor, unreal.PlayerStart):
            transform = actor.get_actor_transform()
            rotation = transform.rotation.rotator()
            player_start = {
                "name": actor.get_actor_label(),
                "translation": ue_to_engine_translation(transform.translation),
                "yawRadians": -math.radians(rotation.yaw),
                "pitchRadians": math.radians(rotation.pitch),
            }

    if directional_light_count != 1:
        raise RuntimeError(f"Expected one DirectionalLight, found {directional_light_count}")
    if ambient is None or fog is None or player_start is None:
        raise RuntimeError("SkyLight, ExponentialHeightFog, or PlayerStart was not found")
    return {
        "ambient": ambient,
        "fog": fog,
        "playerStart": player_start,
        "directionalLightCount": directional_light_count,
    }


def main() -> None:
    require_force_for_existing_output()
    os.makedirs(MESH_OUTPUT_DIR, exist_ok=True)
    os.makedirs(STAGING_DIR, exist_ok=True)

    world = load_level()
    actors = all_level_actors()
    mesh_actors = [actor for actor in actors if isinstance(actor, unreal.StaticMeshActor)]
    labels = [actor.get_actor_label() for actor in mesh_actors]
    if len(labels) != len(set(labels)):
        raise RuntimeError("StaticMeshActor labels must be unique")
    if len(mesh_actors) != 55:
        raise RuntimeError(f"Expected 55 StaticMeshActors, found {len(mesh_actors)}")

    export_gltf(world, STAGING_GLB_PATH, set())
    log(f"Exported coordinate oracle: {STAGING_GLB_PATH}")

    variant_by_key = {}
    actor_entries = []
    for actor in mesh_actors:
        source_mesh_path, material_paths = render_variant(actor)
        key = (source_mesh_path, material_paths)
        if key not in variant_by_key:
            stem = safe_asset_stem(source_mesh_path, material_paths)
            absolute_path = f"{MESH_OUTPUT_DIR}/{stem}.glb"
            export_variant_at_identity(world, actor, absolute_path)
            variant_by_key[key] = {
                "assetPath": f"Assets/Stage/Lvl_FirstPerson/Meshes/{stem}.glb",
                "sourceMesh": source_mesh_path,
                "materials": list(material_paths),
            }
            log(f"Exported render variant: {stem}.glb")

        actor_entries.append({
            "label": actor.get_actor_label(),
            "assetPath": variant_by_key[key]["assetPath"],
            "sourceMesh": source_mesh_path,
            "materials": list(material_paths),
            "mobility": mobility(actor),
        })

    manifest = {
        "version": 2,
        "levelAssetPath": LEVEL_ASSET_PATH,
        "stagingGlb": STAGING_GLB_PATH,
        "actors": actor_entries,
        "renderVariants": sorted(variant_by_key.values(), key=lambda item: item["assetPath"]),
        "environment": build_environment(actors),
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as output:
        json.dump(manifest, output, ensure_ascii=False, indent=2)
        output.write("\n")

    log(f"Wrote manifest: {MANIFEST_PATH}")
    log(f"Exported {len(mesh_actors)} actors using {len(variant_by_key)} render variants")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
