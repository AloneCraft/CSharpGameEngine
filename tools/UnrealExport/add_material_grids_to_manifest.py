"""Adds the prototype grid parameters (materialGrids) to an existing export manifest.

Used when generated GLBs are kept and only the grid material needs to be (re)derived, so the level is
not exported again. Environment:
- CSGE_GRID_LEVEL: level asset path, e.g. /Game/ThirdPerson/Lvl_ThirdPerson
- CSGE_GRID_MANIFEST: manifest.json to update in place
The Unreal project is opened read-only; nothing is saved.
"""

import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import (  # noqa: E402 - UE runs this file as a script, not a package
    all_level_actors,
    load_level,
    log,
    log_error,
    prototype_grid_materials,
)


def main() -> None:
    manifest_path = os.environ["CSGE_GRID_MANIFEST"].replace("\\", "/")
    with open(manifest_path, encoding="utf-8") as stream:
        manifest = json.load(stream)
    load_level(os.environ["CSGE_GRID_LEVEL"])
    mesh_actors = [actor for actor in all_level_actors() if isinstance(actor, unreal.StaticMeshActor)]
    if len(mesh_actors) != len(manifest["actors"]):
        raise RuntimeError(f"The level has {len(mesh_actors)} StaticMeshActors but the manifest lists {len(manifest['actors'])}")
    manifest["materialGrids"] = prototype_grid_materials(mesh_actors)
    with open(manifest_path, "w", encoding="utf-8", newline="\n") as stream:
        json.dump(manifest, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    log(f"Wrote {len(manifest['materialGrids'])} prototype grid material(s) to {manifest_path}")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
