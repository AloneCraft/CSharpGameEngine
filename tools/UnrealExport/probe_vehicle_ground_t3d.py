"""Writes the T3D text of the Vehicle template's ground materials, landscape layer infos and Landscape actors (M0112).

Run: run_probe_vehicle_landscape.cmd probe_vehicle_ground_t3d.py (CGE_PROBE_OUT = the output directory). Nothing is saved.
Why: M_Track is a Material Attributes material whose graph the property-input walk cannot reach; the text export lists every expression.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import all_level_actors, load_level, log_error  # noqa: E402 - UE runs this file as a script

OUT = os.environ["CGE_PROBE_OUT"]


def export_t3d(obj, path):
    task = unreal.AssetExportTask()
    task.object = obj
    task.filename = path
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    if not unreal.Exporter.run_asset_export_task(task) or not os.path.exists(path):
        exporter_class = unreal.load_class(None, "/Script/UnrealEd.ObjectExporterT3D")
        task.exporter = unreal.new_object(exporter_class)
        if not unreal.Exporter.run_asset_export_task(task) or not os.path.exists(path):
            raise RuntimeError(f"T3D export wrote no file: {path}")


def main() -> None:
    os.makedirs(OUT, exist_ok=True)
    for path in (
        "/Game/VehicleTemplate/Materials/M_Track",
        "/Game/VehicleTemplate/Materials/M_Landscape",
        "/Game/VehicleTemplate/Maps/LandscapeInfo/LI_Ground",
        "/Game/VehicleTemplate/Maps/LandscapeInfo/LI_Road",
        "/Game/VehicleTemplate/Blueprints/Tools/BP_SplineMesh",
        "/Game/VehicleTemplate/Blueprints/Tools/BP_SplineSpawn",
    ):
        asset = unreal.EditorAssetLibrary.load_asset(path)
        try:
            export_t3d(asset, os.path.join(OUT, path.rsplit("/", 1)[-1] + ".t3d"))
        except Exception as ex:  # noqa: BLE001 - keep going so one failure does not hide the others
            with open(os.path.join(OUT, path.rsplit("/", 1)[-1] + ".error.txt"), "w", encoding="utf-8") as handle:
                handle.write(str(ex))
    load_level("/Game/VehicleTemplate/Maps/Lvl_VehicleBasic")
    for actor in all_level_actors():
        if actor.get_class().get_name() in ("LandscapeSplineActor", "Landscape", "RuntimeVirtualTextureVolume", "PostProcessVolume"):
            name = actor.get_class().get_name()
            try:
                export_t3d(actor, os.path.join(OUT, f"actor_{name}.t3d"))
            except Exception as ex:  # noqa: BLE001
                with open(os.path.join(OUT, f"actor_{name}.error.txt"), "w", encoding="utf-8") as handle:
                    handle.write(str(ex))


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    import traceback

    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, "FAILED.txt"), "w", encoding="utf-8") as handle:
        handle.write(traceback.format_exc())
    log_error(f"FAILED: {ex}")
    sys.exit(1)
