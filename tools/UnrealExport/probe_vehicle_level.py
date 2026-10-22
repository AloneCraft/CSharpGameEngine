"""Prints what the Vehicle template's level holds that a plain static-mesh export would lose: the Landscape's bounds and ground height.

Run: UnrealEditor-Cmd G:\\UE\\5.8\\Vehicle\\Vehicle.uproject -ExecutePythonScript=probe_vehicle_level.py -nullrhi. Nothing is saved.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log, log_error  # noqa: E402 - UE runs this file as a script


def main() -> None:
    load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    actors = all_level_actors()
    by_class = {}
    for actor in actors:
        by_class.setdefault(actor.get_class().get_name(), []).append(actor)
    log("CLASSES " + ", ".join(f"{name}={len(items)}" for name, items in sorted(by_class.items())))
    for landscape in by_class.get("Landscape", []):
        origin, extent = landscape.get_actor_bounds(False)
        log(f"LANDSCAPE origin_cm=({origin.x:.1f},{origin.y:.1f},{origin.z:.1f}) extent_cm=({extent.x:.1f},{extent.y:.1f},{extent.z:.1f}) location={landscape.get_actor_location()} scale={landscape.get_actor_scale3d()}")
    for actor in by_class.get("StaticMeshActor", []):
        location = actor.get_actor_location()
        rotation = actor.get_actor_rotation()
        scale = actor.get_actor_scale3d()
        component = actor.get_component_by_class(unreal.StaticMeshComponent)
        mesh = component.static_mesh.get_path_name() if component and component.static_mesh else "none"
        log(f"MESH {actor.get_actor_label()} {mesh} loc=({location.x:.0f},{location.y:.0f},{location.z:.0f}) rot=({rotation.pitch:.1f},{rotation.yaw:.1f},{rotation.roll:.1f}) scale=({scale.x:.2f},{scale.y:.2f},{scale.z:.2f})")
    for name in ("PlayerStart", "DirectionalLight", "PostProcessVolume"):
        for actor in by_class.get(name, []):
            location = actor.get_actor_location()
            rotation = actor.get_actor_rotation()
            log(f"{name.upper()} loc=({location.x:.0f},{location.y:.0f},{location.z:.0f}) rot=({rotation.pitch:.1f},{rotation.yaw:.1f},{rotation.roll:.1f})")


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error(f"FAILED: {ex}")
    sys.exit(1)
