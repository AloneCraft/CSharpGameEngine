"""Reads the Vehicle template Landscape's height field (collision line traces on a 1 m grid) and the SM_Track_10M spline mesh parameters (M0112).

Run: run_probe_vehicle_landscape.cmd probe_vehicle_landscape_heights.py (CGE_PROBE_OUT = the result JSON file). Nothing is saved.
Output JSON (centimetres, Unreal axes): { "grid": {"min": [x, y], "step": cm, "nx": n, "ny": n, "heights": [...], "physical": [...]},
"splineMeshes": [ {name, mesh, location, rotation, scale, startPos, startTangent, ...} ] }.
"""
import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

HALF_EXTENT_CM = 6300.0
STEP_CM = float(os.environ.get("CGE_PROBE_STEP_CM", "100"))


def vector(value) -> list:
    return [float(value.x), float(value.y), float(value.z)]


def vector2(value) -> list:
    return [float(value.x), float(value.y)]


def main() -> None:
    world = load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = all_level_actors()
    landscapes = [actor for actor in actors if isinstance(actor, unreal.LandscapeProxy)]
    ignore = [actor for actor in actors if not isinstance(actor, unreal.LandscapeProxy)]
    count = int(round(2.0 * HALF_EXTENT_CM / STEP_CM)) + 1
    heights = []
    physical = []
    misses = 0
    for iy in range(count):
        y = -HALF_EXTENT_CM + iy * STEP_CM
        for ix in range(count):
            x = -HALF_EXTENT_CM + ix * STEP_CM
            start = unreal.Vector(x, y, 5000.0)
            end = unreal.Vector(x, y, -5000.0)
            hit = unreal.SystemLibrary.line_trace_single(
                world, start, end, unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, ignore, unreal.DrawDebugTrace.NONE, True
            )
            blocking = bool(hit.to_tuple()[0])
            if not blocking:
                misses += 1
                heights.append(None)
                physical.append(None)
                continue
            location = hit.to_tuple()[4]
            heights.append(round(float(location.z), 3))
            phys = hit.to_tuple()[8]
            physical.append(phys.get_path_name() if phys else None)
    meshes = []
    for actor in actors:
        for component in actor.get_components_by_class(unreal.SplineMeshComponent):
            mesh = component.get_editor_property("static_mesh")
            params = component.get_editor_property("spline_params")

            def field(name):
                return params.get_editor_property(name)

            meshes.append(
                {
                    "name": component.get_name(),
                    "mesh": mesh.get_path_name() if mesh else None,
                    "location": vector(component.get_world_location()),
                    "rotation": [component.get_world_rotation().pitch, component.get_world_rotation().yaw, component.get_world_rotation().roll],
                    "scale": vector(component.get_world_scale()),
                    "forwardAxis": str(component.get_editor_property("forward_axis")),
                    "splineUpDir": vector(component.get_editor_property("spline_up_dir")),
                    "smoothInterpRollScale": bool(component.get_editor_property("smooth_interp_roll_scale")),
                    "startPos": vector(field("start_pos")),
                    "startTangent": vector(field("start_tangent")),
                    "startScale": vector2(field("start_scale")),
                    "startRoll": float(field("start_roll")),
                    "startOffset": vector2(field("start_offset")),
                    "endPos": vector(field("end_pos")),
                    "endTangent": vector(field("end_tangent")),
                    "endScale": vector2(field("end_scale")),
                    "endRoll": float(field("end_roll")),
                    "endOffset": vector2(field("end_offset")),
                    "materials": [component.get_material(i).get_path_name() if component.get_material(i) else None for i in range(component.get_num_materials())],
                    "collision": str(component.get_collision_enabled()),
                }
            )
    result = {
        "landscapes": [landscape.get_name() for landscape in landscapes],
        "grid": {"min": [-HALF_EXTENT_CM, -HALF_EXTENT_CM], "stepCm": STEP_CM, "nx": count, "ny": count, "heights": heights, "physical": physical, "misses": misses},
        "splineMeshes": meshes,
    }
    with open(os.environ["CGE_PROBE_OUT"], "w", encoding="utf-8") as handle:
        json.dump(result, handle)


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    import traceback

    with open(os.environ["CGE_PROBE_OUT"] + ".error.txt", "w", encoding="utf-8") as handle:
        handle.write(traceback.format_exc())
    log_error(f"FAILED: {ex}")
    sys.exit(1)
