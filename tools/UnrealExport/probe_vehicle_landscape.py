"""Explores what the Vehicle template's Landscape holds (M0112): components, material, splines, spline meshes, collision heights.

Run: run_probe_vehicle_landscape.cmd (UnrealEditor-Cmd on G:\\UE\\5.8\\Vehicle\\Vehicle.uproject). Writes the result to the file named by CGE_PROBE_OUT.
Nothing is saved. Python's log does not reach stdout in this environment, so everything goes to the file.
"""
import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

LINES = []


def log(message: str) -> None:
    LINES.append(message)


def prop(obj, name):
    try:
        return obj.get_editor_property(name)
    except Exception as ex:  # noqa: BLE001 - probing which properties are readable
        return f"<unreadable: {str(ex)[:60]}>"


def main() -> None:
    load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = all_level_actors()
    for actor in actors:
        log(f"ACTOR {actor.get_class().get_name()} label={actor.get_actor_label()} loc={actor.get_actor_location()} rot={actor.get_actor_rotation()} scale={actor.get_actor_scale3d()}")
    landscapes = [a for a in actors if isinstance(a, unreal.LandscapeProxy)]
    for landscape in landscapes:
        origin, extent = landscape.get_actor_bounds(False)
        log(f"LANDSCAPE {landscape.get_name()} bounds origin={origin} extent={extent}")
        for name in ("landscape_material", "component_size_quads", "subsection_size_quads", "num_subsections", "landscape_guid", "collision_mip_level", "physical_material", "static_lighting_lod", "cast_shadow", "runtime_virtual_textures", "virtual_texture_render_pass_type", "virtual_texture_num_lods", "landscape_materials_override", "enable_nanite", "nanite_enabled"):
            log(f"  PROP {name} = {prop(landscape, name)}")
        try:
            log("  EXPORT_TEXT " + landscape.get_editor_property("landscape_material").get_path_name())
        except Exception as ex:  # noqa: BLE001
            log(f"  no material: {ex}")
        components = landscape.get_components_by_class(unreal.SceneComponent)
        counts = {}
        for component in components:
            counts[component.get_class().get_name()] = counts.get(component.get_class().get_name(), 0) + 1
        log(f"  COMPONENT CLASSES {counts}")
        for component in components:
            name = component.get_class().get_name()
            if name in ("LandscapeComponent",):
                continue
            log(f"  COMP {name} {component.get_name()} world_loc={component.get_world_location() if hasattr(component, 'get_world_location') else '?'}")
        lcs = [c for c in components if c.get_class().get_name() == "LandscapeComponent"]
        log(f"  LANDSCAPE COMPONENTS {len(lcs)}")
        for c in lcs[:3]:
            log(f"    LC {c.get_name()} base=({prop(c, 'section_base_x')},{prop(c, 'section_base_y')}) loc={c.get_world_location()}")
    for name in ("landscape_info",):
        pass
    meshes = []
    for actor in actors:
        for component in actor.get_components_by_class(unreal.SplineMeshComponent):
            meshes.append((actor, component))
    log(f"SPLINE MESH COMPONENTS {len(meshes)}")
    for actor, component in meshes:
        mesh = component.get_editor_property("static_mesh")
        log(f"  SMC owner={actor.get_name()} {component.get_name()} mesh={mesh.get_path_name() if mesh else None} loc={component.get_world_location()} rot={component.get_world_rotation()} scale={component.get_world_scale()}")
        log(f"    params {prop(component, 'spline_params')}")
        for name in ("forward_axis", "spline_up_dir", "smooth_interp_roll_scale", "spline_boundary_min", "spline_boundary_max", "virtual_texture_render_pass_type"):
            log(f"    {name} = {prop(component, name)}")
    log(f"REGISTERED LANDSCAPE PROXIES {len(landscapes)}")
    out = os.environ.get("CGE_PROBE_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES) + "\n")


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    import traceback

    LINES.append("FAILED: " + traceback.format_exc())
    out = os.environ.get("CGE_PROBE_OUT")
    if out:
        with open(out, "w", encoding="utf-8") as handle:
            handle.write("\n".join(LINES) + "\n")
    log_error(f"FAILED: {ex}")
    sys.exit(1)
