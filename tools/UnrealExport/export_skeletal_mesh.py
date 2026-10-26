"""Exports one UE skeletal mesh as a skinned glTF (the character mesh of a template), or a static mesh with CGE_SK_STATIC=1.

Run through run_export_skeletal_mesh.cmd. CGE_SK_ASSET is the asset path (for example
/Game/Characters/Mannequins/Meshes/SKM_Manny_Simple) and CGE_SK_OUT the output .glb. Nothing is saved back to Unreal.
"""
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import export_gltf, log, log_error  # noqa: E402 - UE runs this file as a script

try:
    asset = unreal.load_asset(os.environ["CGE_SK_ASSET"])
    if asset is None:
        raise RuntimeError("The skeletal mesh was not found: " + os.environ["CGE_SK_ASSET"])
    out_path = os.environ["CGE_SK_OUT"]
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    # CGE_SK_STATIC=1 は skinning せずに static mesh を export します（sports car の body、glass、wheel は static mesh です）。
    skinned = os.environ.get("CGE_SK_STATIC") != "1"
    export_gltf(asset, out_path, set(), skinned=skinned)
    log(("Exported skeletal mesh: " if skinned else "Exported static mesh: ") + out_path)
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error("FAILED: " + str(ex))
    sys.exit(1)
