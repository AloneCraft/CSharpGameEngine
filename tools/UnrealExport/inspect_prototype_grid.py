"""Dumps UE's prototype grid material (MI_PrototypeGrid_Gray -> M_PrototypeGrid -> MF_ProcGrid) to JSON.

The engine reproduces the grid as a world-space procedural pattern instead of the baked glTF texture,
so this script records the instance parameters and every expression (class, properties, inputs) of the
parent material and of each material function it calls. The project is opened read-only; nothing is saved.
Output: .artifacts/UnrealExport/Lvl_ThirdPerson/prototype_grid.json
"""

import json
import os

import unreal

REPO_ROOT = os.environ.get("CSGE_REPO_ROOT", "G:/GitHub/CSharpGameEngine").replace("\\", "/")
OUTPUT_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_ThirdPerson"
INSTANCE_PATHS = [
    "/Game/LevelPrototyping/Materials/MI_PrototypeGrid_Gray",
    "/Game/LevelPrototyping/Materials/MI_PrototypeGrid_Gray_02",
    "/Game/LevelPrototyping/Materials/MI_PrototypeGrid_Gray_Round",
    "/Game/LevelPrototyping/Materials/MI_PrototypeGrid_TopDark",
]
TEXTURE_PATHS = ["/Game/LevelPrototyping/Textures/T_GridChecker_A"]
PARENT_PATH = "/Game/LevelPrototyping/Materials/M_PrototypeGrid"
# Engine functions MF_ProcGrid calls whose expressions the engine shader reproduces.
ENGINE_FUNCTION_PATHS = ["/Engine/Functions/Engine_MaterialFunctions01/ImageAdjustment/CheapContrast"]
FUNCTION_PATH = "/Game/LevelPrototyping/Materials/MF_ProcGrid"
SKIPPED_PROPERTIES = {"material_expression_editor_x", "material_expression_editor_y", "material_expression_guid", "function", "material", "graph_node", "desc"}
MEL = unreal.MaterialEditingLibrary


def text(value) -> str:
    if isinstance(value, unreal.MaterialExpression):
        return f"<{value.get_class().get_name()} {value.get_name()}>"
    return str(value)


PROPERTY_CANDIDATES = (
    "parameter_name", "default_value", "group", "slider_min", "slider_max", "value", "r", "g", "b", "a", "constant",
    "const_a", "const_b", "const_coordinate", "equals_threshold", "bias", "scale", "material_function", "function_name",
    "transform_source_type", "transform_type", "output_type", "texture", "coordinate_index", "input_name", "sort_priority",
    "min_max", "mask_r", "mask_g", "mask_b", "mask_a", "ignore_alpha", "x_axis", "y_axis", "z_axis", "period", "clamp",
    "use_default_value", "shader_frequency", "origin_type", "world_position_shader_offset", "output_index", "name",
    "reroute", "declaration", "variable_guid", "node_color", "texture_object", "sampler_type", "mip_value_mode",
    "const_input", "const_default", "preview_value", "output_name", "description", "variable_name", "named_reroute_declaration",
)


def expression_properties(expression) -> dict:
    result = {}
    for name in PROPERTY_CANDIDATES:
        try:
            value = expression.get_editor_property(name)
        except Exception:  # noqa: BLE001 - each expression class only has some of the candidates
            continue
        result[name] = text(value)
    return result


def dump_expressions(expressions: list, inputs_of) -> list:
    items = []
    for expression in expressions:
        entry = {"name": expression.get_name(), "class": expression.get_class().get_name(), "properties": expression_properties(expression)}
        try:
            entry["inputNames"] = [str(item) for item in MEL.get_material_expression_input_names(expression)]
        except Exception as ex:  # noqa: BLE001
            entry["inputNamesError"] = str(ex)
        try:
            entry["inputs"] = [text(item) for item in inputs_of(expression)]
        except Exception as ex:  # noqa: BLE001
            entry["inputsError"] = str(ex)
        try:
            entry["outputNames"] = [str(item) for item in MEL.get_material_expression_output_names(expression)]
        except Exception as ex:  # noqa: BLE001
            entry["outputNamesError"] = str(ex)
        items.append(entry)
    return items


def dump_instance(path: str) -> dict:
    instance = unreal.load_asset(path)
    chain = []
    parent = instance
    while isinstance(parent, unreal.MaterialInstanceConstant):
        parent = parent.get_editor_property("parent")
        chain.append(parent.get_path_name() if parent is not None else None)
    entry = {"path": path, "parentChain": chain, "scalars": {}, "vectors": {}, "textures": {}, "switches": {}}
    for name in MEL.get_scalar_parameter_names(parent):
        entry["scalars"][str(name)] = {
            "instance": MEL.get_material_instance_scalar_parameter_value(instance, name),
            "default": MEL.get_material_default_scalar_parameter_value(parent, name),
            "overridden": MEL.is_material_instance_parameter_overridden(instance, name) if hasattr(MEL, "is_material_instance_parameter_overridden") else None,
        }
    for name in MEL.get_vector_parameter_names(parent):
        value = MEL.get_material_instance_vector_parameter_value(instance, name)
        default = MEL.get_material_default_vector_parameter_value(parent, name)
        entry["vectors"][str(name)] = {"instance": [value.r, value.g, value.b, value.a], "default": [default.r, default.g, default.b, default.a]}
    for name in MEL.get_texture_parameter_names(parent):
        value = MEL.get_material_instance_texture_parameter_value(instance, name)
        entry["textures"][str(name)] = value.get_path_name() if value is not None else None
    for name in MEL.get_static_switch_parameter_names(parent):
        entry["switches"][str(name)] = MEL.get_material_instance_static_switch_parameter_value(instance, name)
    return entry


def dump_texture(path: str) -> dict:
    texture = unreal.load_asset(path)
    entry = {"path": path, "width": texture.blueprint_get_size_x(), "height": texture.blueprint_get_size_y()}
    for name in ("srgb", "compression_settings", "filter", "address_x", "address_y", "mip_gen_settings", "lod_group", "never_stream", "source_color_space"):
        try:
            entry[name] = text(texture.get_editor_property(name))
        except Exception as ex:  # noqa: BLE001
            entry[name] = f"error: {ex}"
    task = unreal.AssetExportTask()
    task.object = texture
    task.filename = f"{OUTPUT_DIR}/{path.rsplit('/', 1)[-1]}.png"
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    entry["exported"] = bool(unreal.Exporter.run_asset_export_task(task))
    entry["exportedFile"] = task.filename
    return entry


def main() -> None:
    parent = unreal.load_asset(PARENT_PATH)
    function = unreal.load_asset(FUNCTION_PATH)
    result = {
        "instances": [dump_instance(path) for path in INSTANCE_PATHS],
        "parent": {
            "path": PARENT_PATH,
            "shadingModel": text(parent.get_editor_property("shading_model")),
            "blendMode": text(parent.get_editor_property("blend_mode")),
            "usedTextures": [texture.get_path_name() for texture in MEL.get_used_textures(parent)] if hasattr(MEL, "get_used_textures") else [],
            "expressions": dump_expressions(MEL.get_material_expressions(parent), lambda expression: MEL.get_inputs_for_material_expression(parent, expression)),
            "properties": {},
        },
        "function": {
            "path": FUNCTION_PATH,
            "expressions": dump_expressions(MEL.get_material_function_expressions(function), lambda expression: MEL.get_inputs_for_material_function_expression(function, expression)),
        },
    }
    for prop in ("base_color", "metallic", "specular", "roughness", "normal", "emissive_color", "ambient_occlusion", "opacity", "opacity_mask", "world_position_offset"):
        try:
            node = MEL.get_material_property_input_node(parent, getattr(unreal.MaterialProperty, "MP_" + prop.upper()))
            result["parent"]["properties"][prop] = text(node) if node is not None else None
        except Exception as ex:  # noqa: BLE001
            result["parent"]["properties"][prop] = f"error: {ex}"
    result["textures"] = [dump_texture(path) for path in TEXTURE_PATHS]
    result["engineFunctions"] = {}
    for path in ENGINE_FUNCTION_PATHS:
        engine_function = unreal.load_asset(path)
        result["engineFunctions"][path] = dump_expressions(MEL.get_material_function_expressions(engine_function), lambda expression, f=engine_function: MEL.get_inputs_for_material_function_expression(f, expression))
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    with open(f"{OUTPUT_DIR}/prototype_grid.json", "w", encoding="utf-8") as stream:
        json.dump(result, stream, indent=1, ensure_ascii=False)
    unreal.log(f"[UnrealExport] Wrote {OUTPUT_DIR}/prototype_grid.json")


main()
