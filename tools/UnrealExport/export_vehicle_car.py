"""Exports the Vehicle template's sports car (body, glass, wheel) to glTF with its materials.

Run: run_export_vehicle_car.cmd [output folder]. The default folder is Templates/CSharpGameEngine.Templates.Vehicle/Assets/Vehicles/SportsCar.
The editor must have a render device (no -nullrhi): the glTF exporter reads and bakes materials through it, and without one
it writes empty materials (the exported car was grey). Nothing in the UE project is saved.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import gltf_options, log, log_error, log_warning  # noqa: E402 - UE runs this file as a script

MESHES = (
    "/Game/Vehicles/SportsCar/SM_SportsCar",
    "/Game/Vehicles/SportsCar/SM_SportsCar_Glass",
    "/Game/Vehicles/SportsCar/SM_SportsCar_Wheel",
)


def main() -> None:
    folder = os.environ["CGE_VEHICLE_CAR_OUT_DIR"]
    os.makedirs(folder, exist_ok=True)
    for path in MESHES:
        asset = unreal.EditorAssetLibrary.load_asset(path)
        if asset is None:
            raise RuntimeError(f"Could not load '{path}'")
        target = os.path.join(folder, path.rsplit("/", 1)[1] + ".glb")
        messages = unreal.GLTFExporter.export_to_gltf(asset, target, gltf_options(), set())
        for warning in messages.warnings:
            log_warning(str(warning))
        for error in messages.errors:
            log_error(str(error))
        if len(messages.errors) > 0:
            raise RuntimeError(f"GLTFExporter reported {len(messages.errors)} error(s) for '{path}'")
        log(f"Exported {target}")


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error(f"FAILED: {ex}")
    sys.exit(1)
