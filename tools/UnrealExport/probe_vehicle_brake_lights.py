"""Follows up probe_vehicle_physics.py (M0109): whether the sports car's skeletal VehicleMesh (whose material 0 BrakeLights writes BrakeLightPower to)
is drawn, which materials carry a BrakeLightPower parameter and what drives their emissive input, and the physics asset bodies.

Run: run_probe_vehicle_brake_lights.cmd <OutputFile>. Nothing is saved.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []


def log(message: str) -> None:
    LINES.append(message)


def prop(target, name: str):
    try:
        return target.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001 - not every class has every property
        return f"<{type(ex).__name__}>"


def base_of(material):
    chain = []
    while isinstance(material, (unreal.MaterialInstanceConstant, unreal.MaterialInstanceDynamic)):
        chain.append(material)
        material = material.get_editor_property("parent") if isinstance(material, unreal.MaterialInstanceConstant) else material.get_base_material()
    return material, chain


def tree(material, label: str, property_id) -> None:
    editing = unreal.MaterialEditingLibrary
    root = editing.get_material_property_input_node(material, property_id)
    log(f"  INPUT {label} <- {root.get_class().get_name() if root else 'none'}")
    seen = []
    queue = [(root, 1)] if root else []
    while queue:
        node, depth = queue.pop(0)
        if node in seen or len(seen) > 60:
            continue
        seen.append(node)
        text = node.get_class().get_name()
        for attr in ("parameter_name", "default_value", "constant", "r", "g", "b", "a", "texture", "coordinate_index", "const_a", "const_b", "r_mask", "g_mask", "b_mask", "a_mask"):
            value = prop(node, attr)
            if not (isinstance(value, str) and value.startswith("<")):
                text += f" {attr}={value.get_path_name() if hasattr(value, 'get_path_name') else value}"
        log(f"    {'  ' * depth}NODE {text}")
        queue.extend((n, depth + 1) for n in editing.get_inputs_for_material_expression(material, node) if n)


def describe(label: str, material) -> None:
    if material is None:
        log(f"MATERIAL {label} none")
        return
    base, chain = base_of(material)
    log(f"MATERIAL {label} {material.get_path_name()} base={base.get_path_name() if base else None} chain={[m.get_path_name() for m in chain]}")
    if base is None:
        return
    editing = unreal.MaterialEditingLibrary
    names = [str(n) for n in editing.get_scalar_parameter_names(base)]
    log(f"  SCALARS {[(n, editing.get_material_default_scalar_parameter_value(base, n)) for n in names]}")
    log(f"  TEXTURES {[(str(n), (editing.get_material_default_texture_parameter_value(base, n).get_path_name() if editing.get_material_default_texture_parameter_value(base, n) else None)) for n in editing.get_texture_parameter_names(base)]}")
    log(f"  VECTORS {[(str(n), editing.get_material_default_vector_parameter_value(base, n)) for n in editing.get_vector_parameter_names(base)]}")
    for item in chain:
        if isinstance(item, unreal.MaterialInstanceConstant):
            log(f"  INSTANCE {item.get_path_name()} scalars={[(str(i.get_editor_property('parameter_info').get_editor_property('name')), i.get_editor_property('parameter_value')) for i in item.get_editor_property('scalar_parameter_values')]}")
    if "BrakeLightPower" in names or label.startswith("Chasis"):
        tree(base, "emissive", unreal.MaterialProperty.MP_EMISSIVE_COLOR)


def main() -> None:
    load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    blueprint = unreal.EditorAssetLibrary.load_asset("/Game/VehicleTemplate/Blueprints/SportsCar/BP_VehicleAdvSportsCar")
    actor = unreal.EditorLevelLibrary.spawn_actor_from_object(blueprint, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0))
    try:
        mesh = actor.get_component_by_class(unreal.SkeletalMeshComponent)
        asset = mesh.get_skeletal_mesh_asset()
        log(f"VEHICLEMESH visible={prop(mesh, 'visible')} hidden_in_game={prop(mesh, 'hidden_in_game')} num_materials={mesh.get_num_materials()} cast_shadow={prop(mesh, 'cast_shadow')}")
        try:
            log(f"  asset materials={[(str(s.get_editor_property('material_slot_name')), s.get_editor_property('material_interface').get_path_name() if s.get_editor_property('material_interface') else None) for s in asset.get_editor_property('materials')]}")
        except Exception as ex:  # noqa: BLE001
            log(f"  asset materials failed {ex}")
        try:
            log(f"  bounds={asset.get_bounds()} imported_bounds={asset.get_imported_bounds()}")
        except Exception as ex:  # noqa: BLE001
            log(f"  bounds failed {ex}")
        for name in ("get_num_vertices",):
            try:
                log(f"  {name}={getattr(unreal.SkeletalMeshEditorSubsystem, name)}")
            except Exception:  # noqa: BLE001
                pass
        try:
            subsystem = unreal.get_editor_subsystem(unreal.SkeletalMeshEditorSubsystem)
            log(f"  lod0 vertices={subsystem.get_num_verts(asset, 0)} lods={subsystem.get_lod_count(asset)}")
        except Exception as ex:  # noqa: BLE001
            log(f"  vertices failed {ex}")
        for index in range(mesh.get_num_materials()):
            describe(f"VehicleMesh[{index}]", mesh.get_material(index))
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            if component.static_mesh is None:
                continue
            log(f"STATIC {component.get_name()} mesh={component.static_mesh.get_path_name()} visible={prop(component, 'visible')} attach_socket={component.get_attach_socket_name()} bounds={component.static_mesh.get_bounds()}")
            for index in range(component.get_num_materials()):
                describe(f"{component.get_name()}[{index}]", component.get_material(index))
        physics = asset.get_editor_property("physics_asset")
        setups = physics.get_editor_property("skeletal_body_setups")
        log(f"PHYSICS {physics.get_path_name()} bodies={len(setups)}")
        for setup in setups:
            try:
                geometry = setup.get_editor_property("agg_geom")
                boxes = [(prop(b, "center"), prop(b, "rotation"), prop(b, "x"), prop(b, "y"), prop(b, "z")) for b in geometry.get_editor_property("box_elems")]
                spheres = [(prop(s, "center"), prop(s, "radius")) for s in geometry.get_editor_property("sphere_elems")]
                sphyls = [(prop(s, "center"), prop(s, "radius"), prop(s, "length")) for s in geometry.get_editor_property("sphyl_elems")]
                convex = len(geometry.get_editor_property("convex_elems"))
                log(f"  BODY {setup.get_editor_property('bone_name')} type={setup.get_editor_property('physics_type')} boxes={boxes} spheres={spheres} sphyls={sphyls} convex={convex}")
            except Exception as ex:  # noqa: BLE001
                log(f"  BODY failed {ex} dir={[d for d in dir(setup) if not d.startswith('_')][:40]}")
    finally:
        actor.destroy_actor()


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
