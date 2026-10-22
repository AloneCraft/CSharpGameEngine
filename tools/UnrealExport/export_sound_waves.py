"""Exports UE SoundWave assets as .wav files (the footstep, jump and landing sounds of the game templates).

Run through import_template_sounds.ps1, which stages the source .uasset files under /Game/CgeImportedSounds of a reference project.
CGE_SOUND_EXPORTS is a JSON list of {"asset": "/Game/CgeImportedSounds/Name", "out": "<absolute .wav path>"}. Nothing is saved back to Unreal.
"""
import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import log, log_error  # noqa: E402 - UE runs this file as a script


def export_wave(asset_path: str, out_path: str) -> None:
    asset = unreal.load_asset(asset_path)
    if asset is None or not isinstance(asset, unreal.SoundWave):
        raise RuntimeError("Not a SoundWave: " + asset_path)
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    task = unreal.AssetExportTask()
    task.object = asset
    task.filename = out_path
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    if not unreal.Exporter.run_asset_export_task(task) or not os.path.exists(out_path):
        raise RuntimeError("The WAV export wrote no file: " + out_path)
    log(f"Exported sound: {asset_path} -> {out_path} ({os.path.getsize(out_path)} bytes)")


try:
    for entry in json.loads(os.environ["CGE_SOUND_EXPORTS"]):
        export_wave(entry["asset"], entry["out"])
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error("FAILED: " + str(ex))
    sys.exit(1)
