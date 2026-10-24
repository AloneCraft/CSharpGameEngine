"""Prints what a template level's sky is made of (M0110): the VolumetricCloud component and its cloud material (the instance's parameter values,
the parent material's node graph and its volumetric advanced output), the sky sphere actor (SM_SkySphere: transform, mesh bounds and the
M_SimpleSkyDome graph), and the SkyAtmosphere, SkyLight and fog components, so the engine's clouds and sky sphere can be fed UE's values.

Run: run_probe_sky_and_clouds.cmd <Project.uproject> <LevelAssetPath> <OutputFile>. Nothing is saved in the project.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []
EXPRESSION_ATTRIBUTES = ("parameter_name", "group", "default_value", "constant", "r", "g", "b", "a", "texture", "coordinate_index", "const_a", "const_b",
                         "r_mask", "g_mask", "b_mask", "a_mask", "sampler_type", "function", "input_name", "description", "conservative_density",
                         "per_sample_phase_evaluation", "multi_scattering_approximation_octave_count", "ground_contribution", "gray_scale_material",
                         "rayleigh_only", "clamp_multi_scattering_contribution", "material_function", "period", "speed", "speed_x", "speed_y", "fraction")


def log(message: str) -> None:
    LINES.append(message)


def prop(target, name: str):
    try:
        return target.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001
        return f"<{type(ex).__name__}>"


def text_of(value) -> str:
    if hasattr(value, "get_path_name"):
        return value.get_path_name()
    return str(value)


def dump_properties(prefix: str, target) -> None:
    names = sorted(name for name in dir(target) if not name.startswith("_"))
    for name in names:
        value = prop(target, name)
        if isinstance(value, str) and value.startswith("<"):
            continue
        if callable(value):
            continue
        log(f"{prefix}{name}={text_of(value)}")


def describe_expression(expression) -> str:
    text = expression.get_class().get_name()
    for attribute in EXPRESSION_ATTRIBUTES:
        value = prop(expression, attribute)
        if not (isinstance(value, str) and value.startswith("<")):
            text += f" {attribute}={text_of(value)}"
    return text


def walk(material, root, depth: int, seen: list) -> None:
    if root is None or len(seen) > 400:
        return
    if root in seen:
        log(f"{'  ' * depth}(again) {root.get_class().get_name()} {prop(root, 'parameter_name')}")
        return
    seen.append(root)
    log(f"{'  ' * depth}NODE {describe_expression(root)}")
    if root.get_class().get_name() == "MaterialExpressionNamedRerouteUsage":
        declaration = prop(root, "declaration")
        if hasattr(declaration, "get_class"):
            walk(material, declaration, depth + 1, seen)
        return
    try:
        inputs = unreal.MaterialEditingLibrary.get_inputs_for_material_expression(material, root)
    except Exception as ex:  # noqa: BLE001
        log(f"{'  ' * depth}  <inputs failed {ex}>")
        return
    for index, child in enumerate(inputs):
        if child:
            log(f"{'  ' * depth}  input[{index}]:")
            walk(material, child, depth + 2, seen)


def dump_material(label: str, material) -> None:
    if material is None:
        log(f"MATERIAL {label} none")
        return
    log(f"MATERIAL {label} {material.get_path_name()} class={material.get_class().get_name()}")
    base = material
    if isinstance(material, unreal.MaterialInstance):
        editing = unreal.MaterialEditingLibrary
        for name in editing.get_scalar_parameter_names(material):
            log(f"  SCALAR {name}={editing.get_material_instance_scalar_parameter_value(material, name)}")
        for name in editing.get_vector_parameter_names(material):
            log(f"  VECTOR {name}={editing.get_material_instance_vector_parameter_value(material, name)}")
        for name in editing.get_texture_parameter_names(material):
            log(f"  TEXTURE {name}={text_of(editing.get_material_instance_texture_parameter_value(material, name))}")
        base = material.get_base_material()
        log(f"  BASE {base.get_path_name() if base else 'none'}")
    if base is None:
        return
    for attribute in ("material_domain", "blend_mode", "shading_model", "is_sky", "two_sided", "used_with_static_lighting"):
        log(f"  {attribute}={prop(base, attribute)}")
    editing = unreal.MaterialEditingLibrary
    for name in editing.get_scalar_parameter_names(base):
        log(f"  BASE_SCALAR {name}={editing.get_material_default_scalar_parameter_value(base, name)}")
    for name in editing.get_vector_parameter_names(base):
        log(f"  BASE_VECTOR {name}={editing.get_material_default_vector_parameter_value(base, name)}")
    for name in editing.get_texture_parameter_names(base):
        log(f"  BASE_TEXTURE {name}={text_of(editing.get_material_default_texture_parameter_value(base, name))}")
    for property_label in ("MP_BASE_COLOR", "MP_EMISSIVE_COLOR", "MP_SUBSURFACE_COLOR", "MP_AMBIENT_OCCLUSION", "MP_OPACITY", "MP_WORLD_POSITION_OFFSET"):
        property_id = getattr(unreal.MaterialProperty, property_label, None)
        if property_id is None:
            continue
        node = editing.get_material_property_input_node(base, property_id)
        if node is None:
            continue
        log(f"  INPUT {property_label}")
        walk(base, node, 2, [])
    expressions = prop(base, "expression_collection")
    items = prop(expressions, "expressions") if not isinstance(expressions, str) else expressions
    if isinstance(items, str):
        log(f"  EXPRESSIONS unavailable {items}")
        return
    for expression in items:
        name = expression.get_class().get_name()
        if "Output" in name or "Custom" in name:
            log(f"  OUTPUT_NODE {describe_expression(expression)}")
            walk(base, expression, 2, [])


def main() -> None:
    level = os.environ["CGE_PROBE_MAP"]
    load_level(level)
    log(f"LEVEL {level}")
    for actor in all_level_actors():
        class_name = actor.get_class().get_name()
        label = actor.get_actor_label()
        keyword = any(word in (class_name + label).lower() for word in ("cloud", "sky", "atmosphere", "fog", "directionallight"))
        if not keyword:
            continue
        location = actor.get_actor_location()
        rotation = actor.get_actor_rotation()
        scale = actor.get_actor_scale3d()
        log(f"ACTOR {label} class={class_name} loc_cm=({location.x:.2f},{location.y:.2f},{location.z:.2f}) rot=({rotation.pitch:.3f},{rotation.yaw:.3f},{rotation.roll:.3f}) scale=({scale.x:.4f},{scale.y:.4f},{scale.z:.4f})")
        for component in actor.get_components_by_class(unreal.ActorComponent):
            log(f"  COMPONENT {component.get_name()} class={component.get_class().get_name()}")
            dump_properties("    ", component)
            if isinstance(component, unreal.VolumetricCloudComponent):
                dump_material("cloud", prop(component, "material"))
            if isinstance(component, unreal.StaticMeshComponent):
                mesh = component.static_mesh
                if mesh is not None:
                    bounds = mesh.get_bounds()
                    log(f"    MESH {mesh.get_path_name()} bounds_origin={bounds.origin} extent={bounds.box_extent} radius={bounds.sphere_radius}")
                for index in range(component.get_num_materials()):
                    dump_material(f"mesh[{index}]", component.get_material(index))


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    LINES.append(f"FAILED: {ex}")
    log_error(f"FAILED: {ex}")
    sys.exit(1)
finally:
    output = os.environ.get("CGE_PROBE_OUT")
    if output:
        with open(output, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES))
