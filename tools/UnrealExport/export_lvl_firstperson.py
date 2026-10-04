"""UE 5.8 FirstPerson level -> C# Scene DSL import staging data.

The Unreal project is opened read-only. The source level is never saved: actors are temporarily
placed at identity only while a selected-actor glTF is exported and are restored in a finally
block. A whole-level GLB is written only below .artifacts as a coordinate-conversion oracle for
the .NET generator; it is not a runtime asset.
"""

import json
import os
import sys

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import (  # noqa: E402 - UE runs this file as a script, not a package
    build_environment,
    export_gltf,
    export_level_static_meshes,
    log,
    log_error,
    all_level_actors,
    prototype_grid_materials,
    load_level as load_level_at,
)

LEVEL_ASSET_PATH = "/Game/FirstPerson/Lvl_FirstPerson"
REPO_ROOT = "G:/GitHub/CSharpGameEngine"
OUTPUT_DIR = f"{REPO_ROOT}/UserEcsProject/Assets/Stage/Lvl_FirstPerson"
MESH_OUTPUT_DIR = f"{OUTPUT_DIR}/Meshes"
STAGING_DIR = f"{REPO_ROOT}/.artifacts/UnrealExport/Lvl_FirstPerson"
STAGING_GLB_PATH = f"{STAGING_DIR}/Lvl_FirstPerson.staging.glb"
MANIFEST_PATH = f"{STAGING_DIR}/manifest.json"


def require_force_for_existing_output() -> None:
    if not os.path.exists(MANIFEST_PATH) and not os.path.exists(MESH_OUTPUT_DIR):
        return
    if os.environ.get("CSGE_UNREAL_EXPORT_FORCE") != "1":
        raise RuntimeError(
            "Output already exists. Re-run tools/UnrealExport/run_export.cmd --force "
            "to replace generated import data."
        )


def load_level():
    return load_level_at(LEVEL_ASSET_PATH)


def main() -> None:
    require_force_for_existing_output()
    os.makedirs(MESH_OUTPUT_DIR, exist_ok=True)
    os.makedirs(STAGING_DIR, exist_ok=True)

    world = load_level()
    actors = all_level_actors()
    mesh_actors = [actor for actor in actors if isinstance(actor, unreal.StaticMeshActor)]
    labels = [actor.get_actor_label() for actor in mesh_actors]
    if len(labels) != len(set(labels)):
        raise RuntimeError("StaticMeshActor labels must be unique")
    if len(mesh_actors) != 55:
        raise RuntimeError(f"Expected 55 StaticMeshActors, found {len(mesh_actors)}")

    export_gltf(world, STAGING_GLB_PATH, set())
    log(f"Exported coordinate oracle: {STAGING_GLB_PATH}")

    actor_entries, render_variants = export_level_static_meshes(
        world, mesh_actors, MESH_OUTPUT_DIR, "Assets/Stage/Lvl_FirstPerson/Meshes"
    )

    manifest = {
        "version": 2,
        "levelAssetPath": LEVEL_ASSET_PATH,
        "stagingGlb": STAGING_GLB_PATH,
        "actors": actor_entries,
        "renderVariants": render_variants,
        "materialGrids": prototype_grid_materials(mesh_actors),
        "environment": build_environment(actors),
    }
    with open(MANIFEST_PATH, "w", encoding="utf-8", newline="\n") as output:
        json.dump(manifest, output, ensure_ascii=False, indent=2)
        output.write("\n")

    log(f"Wrote manifest: {MANIFEST_PATH}")
    log(f"Exported {len(mesh_actors)} actors using {len(render_variants)} render variants")


if __name__ == "__main__":
    try:
        main()
    except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
        log_error(f"FAILED: {ex}")
        import traceback

        log_error(traceback.format_exc())
        sys.exit(1)
