"""Writes the navigation data of a template level: NavMeshBoundsVolume (bounds) and RecastNavMesh (agent and cell values).

Run through run_probe_nav.cmd. The result goes to the file named by CGE_NAV_OUT. Nothing is saved.
M0106 takes the agent size, the cell size and the walkable limits of the TopDown navigation mesh from this output.
"""
import json
import os
import unreal

out_path = os.environ["CGE_NAV_OUT"]
result = {"engine": unreal.SystemLibrary.get_engine_version(), "actors": [], "ok": False}


def describe(value):
    try:
        if hasattr(value, "export_text"):
            return str(value.export_text())
    except Exception:
        pass
    return str(value)


try:
    startup = unreal.get_default_object(unreal.GameMapsSettings).get_editor_property("editor_startup_map")
    map_path = str(startup.export_text()) if hasattr(startup, "export_text") else str(startup)
    map_path = map_path.strip('"')
    unreal.EditorLoadingAndSavingUtils.load_map(os.environ.get("CGE_PROBE_MAP", map_path))
    result["map"] = map_path
    for actor in unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors():
        name = actor.get_class().get_name()
        if name not in ("NavMeshBoundsVolume", "RecastNavMesh", "NavigationSystemV1"):
            continue
        entry = {"class": name, "label": actor.get_actor_label(), "properties": {}}
        origin, extent = actor.get_actor_bounds(False)
        entry["location"] = [actor.get_actor_location().x, actor.get_actor_location().y, actor.get_actor_location().z]
        entry["scale"] = [actor.get_actor_scale3d().x, actor.get_actor_scale3d().y, actor.get_actor_scale3d().z]
        entry["boundsOrigin"] = [origin.x, origin.y, origin.z]
        entry["boundsExtent"] = [extent.x, extent.y, extent.z]
        for prop in dir(actor):
            if prop.startswith("_"):
                continue
            try:
                value = actor.get_editor_property(prop)
            except Exception:
                continue
            if callable(value):
                continue
            entry["properties"][prop] = describe(value)
        if name == "RecastNavMesh":
            # The generation settings are properties of the class, not listed by dir(): ask for them by name.
            names = ["agent_radius", "agent_height", "agent_max_height", "agent_max_slope", "agent_max_step_height", "cell_size", "cell_height",
                     "tile_size_uu", "min_region_area", "merge_region_size", "max_simplification_error", "max_simplification_error",
                     "tile_number_hard_limit", "poly_ref_salt_bits", "navmesh_origin_offset", "region_partitioning", "layer_chunk_splits",
                     "draw_distance", "default_draw_distance", "agent_max_climb", "use_virtual_filters", "heuristic_scale", "fixed_tile_pool_size",
                     "use_better_pathfinding", "merge_region_size", "max_sim_error", "rasterize_triangles_using_sort", "default_query_extent", "fixed_tile_pool"]
            entry["generation"] = {}
            for prop in names:
                try:
                    entry["generation"][prop] = describe(actor.get_editor_property(prop))
                except Exception as exception:
                    entry["generation"][prop] = "ERR " + str(exception)[:80]
        result["actors"].append(entry)
    nav_settings = unreal.get_default_object(unreal.NavigationSystemV1) if hasattr(unreal, "NavigationSystemV1") else None
    if nav_settings is not None:
        settings = {}
        for prop in dir(nav_settings):
            if prop.startswith("_"):
                continue
            try:
                value = nav_settings.get_editor_property(prop)
            except Exception:
                continue
            if not callable(value):
                settings[prop] = describe(value)
        result["navigationSystemDefaults"] = settings
    result["ok"] = True
except Exception as exception:
    result["error"] = str(exception)

with open(out_path, "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=2)
unreal.log("CGE-NAV " + json.dumps({"ok": result["ok"], "actors": len(result["actors"])}))
