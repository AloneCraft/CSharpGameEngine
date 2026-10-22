"""Exports the Mannequin locomotion animations of a UE template project as skinned glTF files (ADR-0078/M0107).

Run through run_export_mannequin_animations.cmd, then compact the files with
`CSharpGameEngine.Unreal.SceneImporter compact-animations <directory>` (each file holds the preview mesh until then; the compact file has the joints and the clip only,
as the first six clips of the templates do). CGE_ANIM_OUT is the output directory; every animation under
/Game/Characters/Mannequins/Anims/Unarmed/Walk and /Jog (the eight directions of BS_Idle_Walk_Run) is written there as <name>.glb. The
rest of the graph's clips (Idle, Jump, Fall Loop, Land) are written too when CGE_ANIM_ALL=1. Nothing is saved back to Unreal.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import export_gltf, log, log_error  # noqa: E402 - UE runs this file as a script

ROOT = "/Game/Characters/Mannequins/Anims/Unarmed"
EXTRA = ["MM_Idle", "Jump/MM_Jump", "Jump/MM_Fall_Loop", "Jump/MM_Land"]

try:
    out_dir = os.environ["CGE_ANIM_OUT"]
    os.makedirs(out_dir, exist_ok=True)
    paths = []
    for folder in ("Walk", "Jog"):
        for asset in unreal.EditorAssetLibrary.list_assets(f"{ROOT}/{folder}", recursive=False, include_folder=False):
            paths.append(asset.split(".")[0])
    if os.environ.get("CGE_ANIM_ALL") == "1":
        paths.extend(f"{ROOT}/{name}" for name in EXTRA)
    for path in sorted(paths):
        animation = unreal.load_asset(path)
        if animation is None:
            raise RuntimeError("The animation was not found: " + path)
        target = os.path.join(out_dir, animation.get_name() + ".glb")
        export_gltf(animation, target, set(), skinned=True)
        log(f"Exported animation: {target} ({float(animation.get_play_length()):.4f} s)")
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error("FAILED: " + str(ex))
    sys.exit(1)
