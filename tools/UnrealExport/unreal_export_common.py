"""Shared helpers for the UE 5.8 -> C# Scene DSL export scripts.

Both export_lvl_firstperson.py and export_lvl_thirdperson.py open their Unreal project read-only.
Levels are never saved: actors are temporarily placed at identity only while a selected-actor glTF
is exported and are restored in a finally block.
"""

import hashlib
import json
import math
import re

import unreal


def log(message: str) -> None:
    unreal.log(f"[UnrealExport] {message}")


def log_warning(message: str) -> None:
    unreal.log_warning(f"[UnrealExport] {message}")


def log_error(message: str) -> None:
    unreal.log_error(f"[UnrealExport] {message}")


def load_level(level_asset_path: str) -> unreal.World:
    subsystem = unreal.get_editor_subsystem(unreal.LevelEditorSubsystem)
    if not subsystem.load_level(level_asset_path):
        raise RuntimeError(f"Could not load '{level_asset_path}'")
    world = unreal.get_editor_subsystem(unreal.UnrealEditorSubsystem).get_editor_world()
    if world is None:
        raise RuntimeError("get_editor_world returned None")
    return world


def all_level_actors() -> list:
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
    return sorted(actors, key=lambda actor: actor.get_actor_label())


def gltf_options(skinned: bool = False) -> unreal.GLTFExportOptions:
    options = unreal.GLTFExportOptions()
    options.export_uniform_scale = 0.01
    options.bake_material_inputs = unreal.GLTFMaterialBakeMode.USE_MESH_DATA
    options.default_material_bake_size = unreal.GLTFMaterialBakeSize(x=1024, y=1024)
    options.export_lights = True
    if skinned:
        options.export_vertex_skin_weights = True
        options.export_animation_sequences = True
    return options


def export_gltf(target, path: str, selected_actors: set, skinned: bool = False) -> None:
    messages = unreal.GLTFExporter.export_to_gltf(target, path, gltf_options(skinned), selected_actors)
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


PROTOTYPE_GRID_MATERIAL = "/Game/LevelPrototyping/Materials/M_PrototypeGrid"
# MF_ProcGrid は T_GridChecker_A の R channel（512 px の1 tile。4辺には PNG export 後に計測した5 px の
# wire があります）を world 軸に沿って投影します。grid line の両側で wire は tile の5/512を占めます。
# tools/UnrealExport/inspect_prototype_grid.py と ADR-0075 を参照。
PROTOTYPE_GRID_WIRE_HALF_WIDTH = 5.0 / 512.0


def prototype_grid_parameters(material) -> dict:
    """The world-aligned prototype grid parameters of a M_PrototypeGrid material instance, or None.

    Only the configuration the engine reproduces is accepted: the main grid on, world aligned, no
    top-surface colour. Anything else fails loudly instead of being silently dropped.
    """
    base = material
    while isinstance(base, unreal.MaterialInstanceConstant):
        base = base.get_editor_property("parent")
    if base is None or base.get_path_name().split(".")[0] != PROTOTYPE_GRID_MATERIAL:
        return None
    editing = unreal.MaterialEditingLibrary

    def scalar(name: str) -> float:
        return float(editing.get_material_instance_scalar_parameter_value(material, name))

    def color(name: str) -> list:
        value = editing.get_material_instance_vector_parameter_value(material, name)
        return [float(value.r), float(value.g), float(value.b)]

    def switch(name: str) -> bool:
        return bool(editing.get_material_instance_static_switch_parameter_value(material, name))

    name = material.get_path_name()
    if not switch("Grid"):
        raise RuntimeError(f"{name}: the Grid switch is off (the round-grid variant) and has no engine equivalent")
    if switch("ObjectAligned"):
        raise RuntimeError(f"{name}: ObjectAligned grids have no engine equivalent")
    if switch("TopSurfaceColor"):
        raise RuntimeError(f"{name}: TopSurfaceColor has no engine equivalent")
    return {
        "gridSizeCm": scalar("Grid Size"),
        "subGridDivisions": int(round(scalar("Sub Grid Number"))),
        "wireHalfWidth": PROTOTYPE_GRID_WIRE_HALF_WIDTH,
        "roughness": scalar("Roughness"),
        "surfaceColor": color("SurfaceColor"),
        "gridColor": color("GridColor"),
        "subGridColor": color("SubGridColor"),
    }


def prototype_grid_materials(mesh_actors: list) -> dict:
    """Material path -> prototype grid parameters for every distinct grid material the actors use."""
    result = {}
    for actor in mesh_actors:
        for path in render_variant(actor)[1]:
            if path and path not in result:
                grid = prototype_grid_parameters(unreal.load_asset(path.split(".")[0]))
                if grid is not None:
                    result[path] = grid
    return result


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


def export_level_static_meshes(world: unreal.World, mesh_actors: list, mesh_output_dir: str, asset_prefix: str) -> tuple:
    """Exports one identity-placed GLB per distinct (StaticMesh, material overrides) pair."""
    variant_by_key = {}
    actor_entries = []
    for actor in mesh_actors:
        source_mesh_path, material_paths = render_variant(actor)
        key = (source_mesh_path, material_paths)
        if key not in variant_by_key:
            stem = safe_asset_stem(source_mesh_path, material_paths)
            export_variant_at_identity(world, actor, f"{mesh_output_dir}/{stem}.glb")
            variant_by_key[key] = {
                "assetPath": f"{asset_prefix}/{stem}.glb",
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
    return actor_entries, sorted(variant_by_key.values(), key=lambda item: item["assetPath"])
