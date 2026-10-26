"""Reads the Vehicle level's physical materials, the surface under the track ring, and Landscape heights at off-grid points (M0112).

Run: run_probe_vehicle_landscape.cmd probe_vehicle_surfaces.py (CGE_PROBE_OUT = the result JSON file). Nothing is saved.
Why: the engine's terrain mesh splits each 1 m quad along one diagonal; the off-grid heights tell which diagonal UE's collision uses, and the
physical materials (Slippery / NonSlippery, the track's) give the friction and restitution the colliders carry.
"""
import json
import os
import random
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script


def describe_material(path):
    asset = unreal.EditorAssetLibrary.load_asset(path)
    result = {"path": path}
    for name in ("friction", "static_friction", "restitution", "density", "friction_combine_mode", "restitution_combine_mode", "override_friction_combine_mode", "override_restitution_combine_mode"):
        try:
            result[name] = str(asset.get_editor_property(name))
        except Exception as ex:  # noqa: BLE001 - not every property exists in every version
            result[name] = f"<{str(ex)[:40]}>"
    return result


def trace(world, x, y, ignore):
    hit = unreal.SystemLibrary.line_trace_single(world, unreal.Vector(x, y, 5000.0), unreal.Vector(x, y, -5000.0), unreal.TraceTypeQuery.TRACE_TYPE_QUERY1, False, ignore, unreal.DrawDebugTrace.NONE, True)
    values = hit.to_tuple()
    if not values[0]:
        return None
    phys = values[8]
    actor = values[9]
    return {"z": float(values[4].z), "physical": phys.get_path_name() if phys else None, "actor": actor.get_name() if actor else None, "component": values[10].get_name() if values[10] else None}


def main() -> None:
    world = load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = all_level_actors()
    result = {"materials": [describe_material("/Game/Vehicles/PhysicsMaterials/Slippery"), describe_material("/Game/Vehicles/PhysicsMaterials/NonSlippery")]}
    non_landscape = [actor for actor in actors if not isinstance(actor, unreal.LandscapeProxy)]
    landscape_only_ignore = [actor for actor in actors if not isinstance(actor, unreal.LandscapeProxy)]
    nothing_ignored = [actor for actor in actors if isinstance(actor, (unreal.StaticMeshActor,))]
    # spline mesh を配置した ring（半径45 m）、続いてその下の Landscape。
    ring = []
    for degrees in (0, 20, 45, 90, 135, 180, 225, 270, 315):
        import math

        x = 4500.0 * math.cos(math.radians(degrees))
        y = 4500.0 * math.sin(math.radians(degrees))
        ring.append({"deg": degrees, "withTrack": trace(world, x, y, nothing_ignored), "landscapeOnly": trace(world, x, y, landscape_only_ignore)})
    result["ring"] = ring
    # 道路の横と縁石の上。
    across = []
    for radius in (3500.0, 3700.0, 3800.0, 3900.0, 4000.0, 4200.0, 4500.0, 4800.0, 5000.0, 5100.0, 5200.0, 5300.0, 5500.0, 5800.0, 6200.0):
        across.append({"r": radius, "withTrack": trace(world, radius, 0.0, nothing_ignored), "landscapeOnly": trace(world, radius, 0.0, landscape_only_ignore)})
    result["across"] = across
    rng = random.Random(20261025)
    samples = []
    for _ in range(400):
        x = rng.uniform(-3500.0, 3500.0)
        y = rng.uniform(-3500.0, 3500.0)
        hit = trace(world, x, y, landscape_only_ignore)
        if hit is not None:
            samples.append([x, y, hit["z"]])
    result["offGrid"] = samples
    with open(os.environ["CGE_PROBE_OUT"], "w", encoding="utf-8") as handle:
        json.dump(result, handle, indent=1)


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    import traceback

    with open(os.environ["CGE_PROBE_OUT"] + ".error.txt", "w", encoding="utf-8") as handle:
        handle.write(traceback.format_exc())
    log_error(f"FAILED: {ex}")
    sys.exit(1)
