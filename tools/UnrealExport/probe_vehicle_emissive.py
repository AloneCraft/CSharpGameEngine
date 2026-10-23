"""Follows the sports car body material's emissive input through its named reroute (M0109): M_SportsCarBase's emissive is
EmissivePower times a named reroute, and this prints what the reroute carries and exports the texture it samples, so the engine's
copy of the body can glow where UE's would.

Run: run_probe_vehicle_emissive.cmd <OutputFile>. Exports textures next to the output file. Nothing is saved in the project.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import log_error  # noqa: E402 - UE runs this file as a script

LINES = []


def log(message: str) -> None:
    LINES.append(message)


def prop(target, name: str):
    try:
        return target.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001
        return f"<{type(ex).__name__}>"


def walk(material, root, depth: int, seen: list) -> None:
    if root is None or root in seen or len(seen) > 80:
        return
    seen.append(root)
    text = root.get_class().get_name()
    for attr in ("parameter_name", "name", "default_value", "constant", "r", "g", "b", "a", "texture", "coordinate_index", "const_a", "const_b", "r_mask", "g_mask", "b_mask", "a_mask", "sampler_type"):
        value = prop(root, attr)
        if not (isinstance(value, str) and value.startswith("<")):
            text += f" {attr}={value.get_path_name() if hasattr(value, 'get_path_name') else value}"
    log(f"{'  ' * depth}NODE {text}")
    if root.get_class().get_name() == "MaterialExpressionNamedRerouteUsage":
        declaration = prop(root, "declaration")
        log(f"{'  ' * depth}  DECLARATION {declaration.get_class().get_name() if hasattr(declaration, 'get_class') else declaration} name={prop(declaration, 'name')}")
        if hasattr(declaration, "get_class"):
            walk(material, declaration, depth + 2, seen)
        return
    for child in unreal.MaterialEditingLibrary.get_inputs_for_material_expression(material, root):
        if child:
            walk(material, child, depth + 1, seen)


def export_texture(path: str, folder: str) -> None:
    texture = unreal.EditorAssetLibrary.load_asset(path)
    if texture is None:
        log(f"TEXTURE {path} missing")
        return
    log(f"TEXTURE {path} size={texture.blueprint_get_size_x()}x{texture.blueprint_get_size_y()} srgb={prop(texture, 'srgb')} compression={prop(texture, 'compression_settings')}")
    task = unreal.AssetExportTask()
    task.object = texture
    task.filename = os.path.join(os.path.abspath(folder), path.split("/")[-1].split(".")[0] + ".png")
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    task.exporter = unreal.TextureExporterPNG()
    log(f"  EXPORT {task.filename} ok={unreal.Exporter.run_asset_export_task(task)}")


def main() -> None:
    folder = os.path.dirname(os.environ.get("CGE_PROBE_OUT", "."))
    material = unreal.EditorAssetLibrary.load_asset("/Game/Vehicles/SportsCar/Materials/M_SportsCarBase")
    editing = unreal.MaterialEditingLibrary
    for label, property_id in (("emissive", unreal.MaterialProperty.MP_EMISSIVE_COLOR), ("base_color", unreal.MaterialProperty.MP_BASE_COLOR)):
        log(f"INPUT {label}")
        walk(material, editing.get_material_property_input_node(material, property_id), 1, [])
    for path in ("/Game/Vehicles/SportsCar/Materials/Textures/T_SportsCar_Lights_M", "/Game/Vehicles/SportsCar/Materials/Textures/T_SportsCar_Chasis_D", "/Game/Vehicles/SportsCar/Materials/Textures/T_SportsCar_Body_D"):
        export_texture(path, folder)
    for path in unreal.EditorAssetLibrary.list_assets("/Game/Vehicles/SportsCar/Materials/Textures", recursive=True, include_folder=False):
        log(f"ASSET {path}")


try:
    main()
except Exception as ex:  # noqa: BLE001
    LINES.append(f"FAILED: {ex}")
    log_error(f"FAILED: {ex}")
    sys.exit(1)
finally:
    output = os.environ.get("CGE_PROBE_OUT")
    if output:
        with open(output, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES))
