"""Prints the materials of the Vehicle template's level meshes and sports car: slot, instance chain, base material, textures.

Run: run_probe_vehicle_materials.cmd (UnrealEditor-Cmd on G:\\UE\\5.8\\Vehicle\\Vehicle.uproject). Writes the result to the file named by CGE_PROBE_OUT. Nothing is saved.
Why: the exported glb files of the Vehicle template carry empty materials; this tells which materials the meshes use.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []


def log(message: str) -> None:
    LINES.append(message)


def describe_material(material) -> str:
    chain = []
    base = material
    while isinstance(base, unreal.MaterialInstanceConstant):
        chain.append(base.get_path_name())
        base = base.get_editor_property("parent")
    chain.append(base.get_path_name() if base is not None else "none")
    text = " -> ".join(chain)
    if isinstance(material, unreal.MaterialInstanceConstant):
        textures = []
        for item in material.get_editor_property("texture_parameter_values"):
            info = item.get_editor_property("parameter_info")
            value = item.get_editor_property("parameter_value")
            textures.append(f"{info.get_editor_property('name')}={value.get_path_name() if value else 'none'}")
        scalars = [f"{i.get_editor_property('parameter_info').get_editor_property('name')}={i.get_editor_property('parameter_value'):.3f}" for i in material.get_editor_property("scalar_parameter_values")]
        vectors = []
        for i in material.get_editor_property("vector_parameter_values"):
            c = i.get_editor_property("parameter_value")
            vectors.append(f"{i.get_editor_property('parameter_info').get_editor_property('name')}=({c.r:.3f},{c.g:.3f},{c.b:.3f},{c.a:.3f})")
        text += f" | textures[{'; '.join(textures)}] scalars[{'; '.join(scalars)}] vectors[{'; '.join(vectors)}]"
    return text


def describe_mesh(label: str, mesh) -> None:
    log(f"MESH {label} {mesh.get_path_name()}")
    for index, slot in enumerate(mesh.get_editor_property("static_materials")):
        material = slot.get_editor_property("material_interface")
        log(f"  SLOT {index} {slot.get_editor_property('material_slot_name')}: {describe_material(material) if material else 'none'}")


def dump_expressions(path: str) -> None:
    """The expression nodes of a plain Material: class, key properties; enough to read a simple colour material."""
    material = unreal.EditorAssetLibrary.load_asset(path)
    log(f"MATERIAL {path} blend={material.get_editor_property('blend_mode')} two_sided={material.get_editor_property('two_sided')} shading={material.get_editor_property('shading_model')}")
    editing = unreal.MaterialEditingLibrary
    log(f"  textures used: {[t.get_path_name() for t in editing.get_used_textures(material)] if hasattr(editing, 'get_used_textures') else 'n/a'}")
    props = {"base_color": unreal.MaterialProperty.MP_BASE_COLOR, "metallic": unreal.MaterialProperty.MP_METALLIC, "roughness": unreal.MaterialProperty.MP_ROUGHNESS, "specular": unreal.MaterialProperty.MP_SPECULAR, "emissive": unreal.MaterialProperty.MP_EMISSIVE_COLOR}
    for name, prop in props.items():
        root = editing.get_material_property_input_node(material, prop)
        log(f"  INPUT {name} <- {root.get_class().get_name() if root else 'none'}")
        seen = []
        queue = [root] if root else []
        while queue:
            node = queue.pop(0)
            if node in seen:
                continue
            seen.append(node)
            text = node.get_class().get_name()
            for attr in ("parameter_name", "default_value", "constant", "r", "g", "b", "texture", "value"):
                try:
                    text += f" {attr}={node.get_editor_property(attr)}"
                except Exception:  # noqa: BLE001 - not every node has every property
                    continue
            log(f"    NODE {text}")
            queue.extend(n for n in editing.get_inputs_for_material_expression(material, node) if n)


def main() -> None:
    dump_expressions("/Game/VehicleTemplate/Materials/M_TintCol")
    for path in unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if isinstance(asset, unreal.StaticMesh) and asset.get_path_name() not in ("",) and "Car" in path:
            describe_mesh("car", asset)
    load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    seen = set()
    for actor in all_level_actors():
        if not isinstance(actor, unreal.StaticMeshActor):
            continue
        component = actor.get_component_by_class(unreal.StaticMeshComponent)
        if component is None or component.static_mesh is None:
            continue
        overrides = [component.get_material(i) for i in range(component.get_num_materials())]
        log(f"ACTOR {actor.get_actor_label()} mesh={component.static_mesh.get_path_name()} materials={[m.get_path_name() if m else None for m in overrides]}")
        if component.static_mesh.get_path_name() not in seen:
            seen.add(component.static_mesh.get_path_name())
            describe_mesh(actor.get_actor_label(), component.static_mesh)
    for path in unreal.EditorAssetLibrary.list_assets("/Game/VehicleTemplate/Meshes", recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if isinstance(asset, unreal.StaticMesh) and asset.get_path_name() not in seen:
            seen.add(asset.get_path_name())
            describe_mesh("library", asset)
    for path in unreal.EditorAssetLibrary.list_assets("/Game/VehicleTemplate/Blueprints", recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if isinstance(asset, unreal.SkeletalMesh):
            log(f"SKELETAL {asset.get_path_name()}")
            for index, slot in enumerate(asset.get_editor_property("materials")):
                material = slot.get_editor_property("material_interface")
                log(f"  SLOT {index}: {describe_material(material) if material else 'none'}")


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    LINES.append(f"FAILED: {ex}")
    log_error(f"FAILED: {ex}")
    sys.exit(1)
finally:
    # The Python log does not reach stdout in this editor configuration, so the result goes to a file.
    output = os.environ.get("CGE_PROBE_OUT")
    if output:
        with open(output, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES))
