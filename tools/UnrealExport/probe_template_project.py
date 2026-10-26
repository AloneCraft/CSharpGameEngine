"""Opens a UE template project's startup map in the editor and writes what it holds (actor classes, mesh and sound assets).

Run through run_probe.cmd (UnrealEditor-Cmd with this script). The result goes to the file named by CGE_PROBE_OUT, so that the
project can be inspected from outside the editor without opening it by hand. Nothing is saved.
"""
import json
import os
import unreal

out_path = os.environ["CGE_PROBE_OUT"]
startup = unreal.get_default_object(unreal.GameMapsSettings).get_editor_property("editor_startup_map")
map_path = str(startup.export_text()) if hasattr(startup, "export_text") else str(startup)
map_path = map_path.strip('"')
if map_path.startswith("("):
    map_path = os.environ.get("CGE_PROBE_MAP", map_path)

result = {"engine": unreal.SystemLibrary.get_engine_version(), "map": map_path, "ok": False}
try:
    unreal.EditorLoadingAndSavingUtils.load_map(os.environ.get("CGE_PROBE_MAP", map_path))
    actors = unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors()
    classes = {}
    meshes = set()
    for actor in actors:
        name = actor.get_class().get_name()
        classes[name] = classes.get(name, 0) + 1
        if isinstance(actor, unreal.StaticMeshActor):
            component = actor.get_component_by_class(unreal.StaticMeshComponent)
            mesh = component.get_editor_property("static_mesh") if component else None
            if mesh:
                meshes.add(mesh.get_path_name())
    registry = unreal.AssetRegistryHelpers.get_asset_registry()
    assets = registry.get_assets_by_path("/Game", recursive=True)
    kinds = {}
    for asset in assets:
        kind = str(asset.asset_class_path.asset_name)
        kinds[kind] = kinds.get(kind, 0) + 1
    result.update({
        "ok": True,
        "actorCount": len(actors),
        "actorClasses": dict(sorted(classes.items())),
        "staticMeshesInLevel": sorted(meshes),
        "assetKinds": dict(sorted(kinds.items())),
    })
except Exception as exception:  # probe は editor の実行を失敗させず、結果を報告します。
    result["error"] = str(exception)

with open(out_path, "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=2)
unreal.log("CGE-PROBE " + json.dumps({"map": result["map"], "ok": result["ok"], "actors": result.get("actorCount")}))
