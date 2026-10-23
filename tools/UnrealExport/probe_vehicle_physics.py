"""Prints the Chaos vehicle values the logic export does not carry (M0109): physical materials and the Landscape layers that use them,
the sports car's wheel bones and centre of mass, its physics asset bodies, the spring arms' lag values and the brake light material.

Run: run_probe_vehicle_physics.cmd (UnrealEditor-Cmd on G:\\UE\\5.8\\Vehicle\\Vehicle.uproject). Writes the result to the file named by
CGE_PROBE_OUT. Nothing is saved (the spawned car is destroyed and the level is not saved).
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []


def log(message: str) -> None:
    LINES.append(message)


def prop(target, name: str):
    try:
        return target.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001 - not every class has every property
        return f"<{type(ex).__name__}>"


def physical_material(path: str) -> None:
    material = unreal.EditorAssetLibrary.load_asset(path)
    if material is None:
        log(f"PHYSMAT {path} missing")
        return
    names = ("friction", "static_friction", "restitution", "friction_combine_mode", "restitution_combine_mode", "override_friction_combine_mode", "density", "surface_type")
    log(f"PHYSMAT {path} " + " ".join(f"{name}={prop(material, name)}" for name in names))


def expression_tree(material, label: str, root) -> None:
    editing = unreal.MaterialEditingLibrary
    log(f"  INPUT {label} <- {root.get_class().get_name() if root else 'none'}")
    seen = []
    queue = [(root, 1)] if root else []
    while queue:
        node, depth = queue.pop(0)
        if node in seen:
            continue
        seen.append(node)
        text = node.get_class().get_name()
        for attr in ("parameter_name", "default_value", "constant", "r", "g", "b", "a", "texture", "value", "coordinate_index", "u_tiling", "v_tiling", "const_a", "const_b", "mask_r", "mask_g", "mask_b", "mask_a", "group"):
            value = prop(node, attr)
            if not (isinstance(value, str) and value.startswith("<")):
                text += f" {attr}={value.get_path_name() if hasattr(value, 'get_path_name') else value}"
        log(f"    {'  ' * depth}NODE {text}")
        queue.extend((n, depth + 1) for n in editing.get_inputs_for_material_expression(material, node) if n)


def material_graph(material) -> None:
    base = material
    chain = []
    while isinstance(base, unreal.MaterialInstanceConstant):
        chain.append(base.get_path_name())
        scalars = [f"{i.get_editor_property('parameter_info').get_editor_property('name')}={i.get_editor_property('parameter_value'):.3f}" for i in base.get_editor_property("scalar_parameter_values")]
        log(f"  INSTANCE {base.get_path_name()} scalars[{'; '.join(scalars)}]")
        base = base.get_editor_property("parent")
    if base is None:
        log("  BASE none")
        return
    log(f"  BASE {base.get_path_name()} blend={prop(base, 'blend_mode')} shading={prop(base, 'shading_model')}")
    editing = unreal.MaterialEditingLibrary
    for name, property_id in (("emissive", unreal.MaterialProperty.MP_EMISSIVE_COLOR), ("base_color", unreal.MaterialProperty.MP_BASE_COLOR)):
        expression_tree(base, name, editing.get_material_property_input_node(base, property_id))
    try:
        log(f"  SCALAR_NAMES {editing.get_scalar_parameter_names(base)}")
        for name in editing.get_scalar_parameter_names(base):
            log(f"  SCALAR {name}={editing.get_material_default_scalar_parameter_value(base, name)}")
        log(f"  TEXTURE_NAMES {editing.get_texture_parameter_names(base)}")
        for name in editing.get_texture_parameter_names(base):
            texture = editing.get_material_default_texture_parameter_value(base, name)
            log(f"  TEXTURE {name}={texture.get_path_name() if texture else None}")
    except Exception as ex:  # noqa: BLE001 - optional API
        log(f"  PARAMETERS failed {ex}")


def landscape(actor) -> None:
    log(f"LANDSCAPE {actor.get_actor_label()} class={actor.get_class().get_name()} default_phys_material={prop(actor, 'default_phys_material')} material={prop(actor, 'landscape_material')}")
    for name in ("editor_layer_settings", "target_layers", "layers"):
        value = prop(actor, name)
        log(f"  {name}={value}")
    material = prop(actor, "landscape_material")
    if hasattr(material, "get_path_name"):
        base = material
        while isinstance(base, unreal.MaterialInstanceConstant):
            base = base.get_editor_property("parent")
        try:
            expressions = unreal.MaterialEditingLibrary.get_material_expressions(base) if hasattr(unreal.MaterialEditingLibrary, "get_material_expressions") else []
        except Exception:  # noqa: BLE001
            expressions = []
        for expression in expressions:
            if "Physical" in expression.get_class().get_name() or "LayerSample" in expression.get_class().get_name():
                log(f"  EXPRESSION {expression.get_class().get_name()} inputs={prop(expression, 'inputs')} parameter={prop(expression, 'parameter_name')}")
    for path in unreal.EditorAssetLibrary.list_assets("/Game", recursive=True, include_folder=False):
        asset = unreal.EditorAssetLibrary.find_asset_data(path).get_asset()
        if isinstance(asset, unreal.LandscapeLayerInfoObject):
            log(f"  LAYERINFO {path} layer={prop(asset, 'layer_name')} phys_material={prop(asset, 'phys_material')}")


def car(world) -> None:
    blueprint = unreal.EditorAssetLibrary.load_asset("/Game/VehicleTemplate/Blueprints/SportsCar/BP_VehicleAdvSportsCar")
    actor = unreal.EditorLevelLibrary.spawn_actor_from_object(blueprint, unreal.Vector(0, 0, 0), unreal.Rotator(0, 0, 0))
    try:
        mesh = actor.get_component_by_class(unreal.SkeletalMeshComponent)
        log(f"CAR actor={actor.get_name()} mesh_component={mesh.get_name()} asset={mesh.get_skeletal_mesh_asset().get_path_name() if hasattr(mesh, 'get_skeletal_mesh_asset') else prop(mesh, 'skeletal_mesh')}")
        for bone in ("Root", "root", "Phys_Wheel_FL", "Phys_Wheel_FR", "Phys_Wheel_BL", "Phys_Wheel_BR", "Phys_Body", "Body"):
            try:
                location = mesh.get_socket_location(bone)
                log(f"  BONE {bone} exists={mesh.does_socket_exist(bone)} location=({location.x:.4f},{location.y:.4f},{location.z:.4f})")
            except Exception as ex:  # noqa: BLE001
                log(f"  BONE {bone} failed {ex}")
        try:
            names = mesh.get_all_socket_names()
            log(f"  SOCKETS {[str(n) for n in names]}")
        except Exception as ex:  # noqa: BLE001
            log(f"  SOCKETS failed {ex}")
        for call in ("get_center_of_mass",):
            try:
                value = getattr(mesh, call)()
                log(f"  {call}=({value.x:.4f},{value.y:.4f},{value.z:.4f})")
            except Exception as ex:  # noqa: BLE001
                log(f"  {call} failed {ex}")
        try:
            log(f"  mass={mesh.get_mass()} body_instance={prop(mesh, 'body_instance')}")
        except Exception as ex:  # noqa: BLE001
            log(f"  mass failed {ex}")
        asset = mesh.get_skeletal_mesh_asset() if hasattr(mesh, "get_skeletal_mesh_asset") else prop(mesh, "skeletal_mesh")
        physics = prop(asset, "physics_asset")
        log(f"  PHYSICS_ASSET {physics.get_path_name() if hasattr(physics, 'get_path_name') else physics}")
        if hasattr(physics, "get_path_name"):
            for setup in prop(physics, "skeletal_body_setups"):
                geometry = prop(setup, "agg_geom")
                log(f"    BODY bone={prop(setup, 'bone_name')} physics_type={prop(setup, 'physics_type')} collision_trace={prop(setup, 'collision_trace_flag')}")
                for kind in ("box_elems", "sphyl_elems", "sphere_elems", "convex_elems"):
                    elements = prop(geometry, kind)
                    if isinstance(elements, str):
                        continue
                    for element in elements:
                        fields = []
                        for name in ("center", "rotation", "x", "y", "z", "radius", "length", "elem_box"):
                            value = prop(element, name)
                            if not (isinstance(value, str) and value.startswith("<")):
                                fields.append(f"{name}={value}")
                        if kind == "convex_elems":
                            vertices = prop(element, "vertex_data")
                            if not isinstance(vertices, str):
                                xs = [v.x for v in vertices]
                                ys = [v.y for v in vertices]
                                zs = [v.z for v in vertices]
                                if xs:
                                    fields.append(f"bounds=({min(xs):.2f},{min(ys):.2f},{min(zs):.2f})-({max(xs):.2f},{max(ys):.2f},{max(zs):.2f}) count={len(xs)}")
                        log(f"      {kind} {' '.join(fields)}")
        for component in actor.get_components_by_class(unreal.SpringArmComponent):
            names = ("target_arm_length", "socket_offset", "target_offset", "relative_location", "relative_rotation", "enable_camera_lag", "enable_camera_rotation_lag", "camera_lag_speed", "camera_rotation_lag_speed", "camera_lag_max_distance", "camera_lag_max_time_step", "use_pawn_control_rotation", "inherit_pitch", "inherit_yaw", "inherit_roll", "do_collision_test")
            log(f"  ARM {component.get_name()} attach={component.get_attach_parent().get_name() if component.get_attach_parent() else None} " + " ".join(f"{name}={prop(component, name)}" for name in names))
        for component in actor.get_components_by_class(unreal.CameraComponent):
            log(f"  CAMERA {component.get_name()} attach={component.get_attach_parent().get_name() if component.get_attach_parent() else None} fov={prop(component, 'field_of_view')} aspect={prop(component, 'aspect_ratio')} auto_activate={prop(component, 'auto_activate')} constrain={prop(component, 'constrain_aspect_ratio')} relative_location={prop(component, 'relative_location')}")
        for component in actor.get_components_by_class(unreal.SpotLightComponent):
            names = ("intensity", "intensity_units", "light_color", "inner_cone_angle", "outer_cone_angle", "attenuation_radius", "source_radius", "relative_location", "relative_rotation", "visible", "cast_shadows", "use_temperature", "temperature")
            log(f"  SPOT {component.get_name()} attach={component.get_attach_parent().get_name() if component.get_attach_parent() else None} " + " ".join(f"{name}={prop(component, name)}" for name in names))
        for component in actor.get_components_by_class(unreal.StaticMeshComponent):
            log(f"  STATIC {component.get_name()} attach={component.get_attach_parent().get_name() if component.get_attach_parent() else None} socket={prop(component, 'attach_socket_name')} relative_location={prop(component, 'relative_location')} relative_rotation={prop(component, 'relative_rotation')} scale={prop(component, 'relative_scale3d')}")
        log("BRAKE_MATERIAL slot 0")
        material = mesh.get_material(0)
        log(f"  SLOT0 {material.get_path_name() if material else None}")
        if material:
            material_graph(material)
        for index in range(mesh.get_num_materials()):
            other = mesh.get_material(index)
            log(f"  SLOT {index} {other.get_path_name() if other else None}")
    finally:
        actor.destroy_actor()


def main() -> None:
    physical_material("/Game/Vehicles/PhysicsMaterials/Slippery")
    physical_material("/Game/Vehicles/PhysicsMaterials/NonSlippery")
    physical_material("/Engine/EngineMaterials/DefaultPhysicalMaterial")
    settings = unreal.get_default_object(unreal.PhysicsSettings) if hasattr(unreal, "PhysicsSettings") else None
    if settings is not None:
        log(f"PHYSICS_SETTINGS default_gravity_z={prop(settings, 'default_gravity_z')} friction_combine_mode={prop(settings, 'friction_combine_mode')} restitution_combine_mode={prop(settings, 'restitution_combine_mode')}")
    world = load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    for actor in all_level_actors():
        name = actor.get_class().get_name()
        if "Landscape" in name and "Spline" not in name and "Gizmo" not in name:
            landscape(actor)
        elif isinstance(actor, unreal.StaticMeshActor):
            component = actor.get_component_by_class(unreal.StaticMeshComponent)
            if component is not None and component.static_mesh is not None:
                body = prop(component, "body_instance")
                override = prop(body, "phys_material_override") if not isinstance(body, str) else body
                mats = [component.get_material(i) for i in range(component.get_num_materials())]
                physmats = []
                for m in mats:
                    try:
                        physmats.append(m.get_physical_material().get_path_name() if m and m.get_physical_material() else None)
                    except Exception:  # noqa: BLE001
                        physmats.append("?")
                log(f"MESHACTOR {actor.get_actor_label()} mesh={component.static_mesh.get_path_name()} override={override.get_path_name() if hasattr(override, 'get_path_name') else override} physmats={physmats}")
    car(world)


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
