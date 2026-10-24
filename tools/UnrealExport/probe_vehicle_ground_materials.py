"""Dumps the whole expression graph of the Vehicle template's ground materials (M0112): M_Landscape, M_Landscape_A, M_Track and their instances.

Run: run_probe_vehicle_landscape.cmd probe_vehicle_ground_materials.py (CGE_PROBE_OUT = the result text file). Nothing is saved.
Why: the Landscape and the SM_Track_10M spline meshes carry the checkered pavement the engine's flat slab lacked; this reads where the colours come from.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import log_error  # noqa: E402 - UE runs this file as a script

LINES = []
ROOT_PROPERTIES = [
    "MP_BASE_COLOR", "MP_METALLIC", "MP_SPECULAR", "MP_ROUGHNESS", "MP_NORMAL", "MP_EMISSIVE_COLOR", "MP_OPACITY",
    "MP_OPACITY_MASK", "MP_WORLD_POSITION_OFFSET", "MP_AMBIENT_OCCLUSION", "MP_ANISOTROPY",
]
SKIP = {"get_name", "get_path_name", "get_class"}


def log(message: str) -> None:
    LINES.append(message)


def short(value) -> str:
    text = str(value)
    return text if len(text) < 400 else text[:400] + "..."


def node_properties(node) -> str:
    parts = []
    for name in dir(node):
        if name.startswith("_") or name in SKIP:
            continue
        try:
            value = node.get_editor_property(name)
        except Exception:  # noqa: BLE001 - not a property
            continue
        if callable(value):
            continue
        if name in ("material_expression_editor_x", "material_expression_editor_y", "material_expression_guid", "desc", "b_collapsed", "b_realtime_preview", "b_comment_bubble_visible", "b_show_outputs", "b_show_mask_colors_on_pin", "outputs", "last_error_text", "b_is_parameter_expression", "b_hide_preview_window", "b_need_to_update_preview", "b_collapsed", "editor_property_name", "function_parameter_info", "material_expression_editor_x"):
            continue
        parts.append(f"{name}={short(value)}")
    return " ".join(parts)


def dump_graph(path: str) -> None:
    material = unreal.EditorAssetLibrary.load_asset(path)
    log(f"=== {path} class={material.get_class().get_name()}")
    if isinstance(material, unreal.MaterialInstanceConstant):
        parent = material.get_editor_property("parent")
        log(f"  parent={parent.get_path_name() if parent else None}")
        for item in material.get_editor_property("texture_parameter_values"):
            info = item.get_editor_property("parameter_info")
            value = item.get_editor_property("parameter_value")
            log(f"  TEX {info.get_editor_property('name')}={value.get_path_name() if value else None}")
        for item in material.get_editor_property("scalar_parameter_values"):
            log(f"  SCALAR {item.get_editor_property('parameter_info').get_editor_property('name')}={item.get_editor_property('parameter_value')}")
        for item in material.get_editor_property("vector_parameter_values"):
            color = item.get_editor_property("parameter_value")
            log(f"  VECTOR {item.get_editor_property('parameter_info').get_editor_property('name')}=({color.r:.4f},{color.g:.4f},{color.b:.4f},{color.a:.4f})")
        for item in material.get_editor_property("static_parameters") if hasattr(material, "get_editor_property") and False else []:
            pass
        return
    log(f"  blend={material.get_editor_property('blend_mode')} shading={material.get_editor_property('shading_model')} two_sided={material.get_editor_property('two_sided')} domain={material.get_editor_property('material_domain')}")
    editing = unreal.MaterialEditingLibrary
    ids = {}
    queue = []
    for name in ROOT_PROPERTIES:
        prop = getattr(unreal.MaterialProperty, name, None)
        if prop is None:
            continue
        root = editing.get_material_property_input_node(material, prop)
        if root is not None:
            log(f"  ROOT {name} <- #{ids.setdefault(root, len(ids))} {root.get_class().get_name()}")
            queue.append(root)
    done = set()
    while queue:
        node = queue.pop(0)
        if node in done:
            continue
        done.add(node)
        inputs = editing.get_inputs_for_material_expression(material, node)
        log(f"  NODE #{ids.setdefault(node, len(ids))} {node.get_class().get_name()} {node_properties(node)}")
        for index, source in enumerate(inputs):
            if source is None:
                continue
            log(f"    IN{index} <- #{ids.setdefault(source, len(ids))} {source.get_class().get_name()}")
            queue.append(source)


def dump_textures(path: str) -> None:
    for asset_path in unreal.EditorAssetLibrary.list_assets(path, recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.load_asset(asset_path)
        if isinstance(asset, unreal.Texture):
            log(f"TEXTURE {asset_path} class={asset.get_class().get_name()} size=({asset.blueprint_get_size_x()},{asset.blueprint_get_size_y()})")


def main() -> None:
    for path in (
        "/Game/VehicleTemplate/Materials/M_Landscape",
        "/Game/VehicleTemplate/Materials/M_Landscape_A",
        "/Game/VehicleTemplate/Materials/M_Track",
        "/Game/VehicleTemplate/Materials/MI_TrackEdge",
        "/Game/VehicleTemplate/Materials/MI_TrackEdge_Inst",
    ):
        dump_graph(path)
    dump_textures("/Game/VehicleTemplate")
    dump_textures("/Game/Vehicles")
    for asset_path in unreal.EditorAssetLibrary.list_assets("/Game/VehicleTemplate", recursive=True, include_folder=False):
        log(f"ASSET {asset_path}")
    mesh = unreal.EditorAssetLibrary.load_asset("/Game/VehicleTemplate/Meshes/SM_Track_10M")
    box = mesh.get_bounds()
    log(f"SM_Track_10M bounds origin={box.origin} extent={box.box_extent}")
    for index, slot in enumerate(mesh.get_editor_property("static_materials")):
        log(f"  SLOT {index} {slot.get_editor_property('material_slot_name')} {slot.get_editor_property('material_interface').get_path_name()}")


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    import traceback

    LINES.append("FAILED: " + traceback.format_exc())
    log_error(f"FAILED: {ex}")
    sys.exit(1)
finally:
    with open(os.environ["CGE_PROBE_OUT"], "w", encoding="utf-8") as handle:
        handle.write("\n".join(LINES) + "\n")
