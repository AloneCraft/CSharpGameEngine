"""Reads what M0107 (characters and animation) needs from a UE template project: the world's KillZ and PlayerStarts, and the
FirstPerson control rig's controls (CtrlRig_FPWarp: BodyOffset, Ctrl_Head, Ctrl_Shoulder_L / R).

Run through run_probe_character_animation.cmd. The result goes to the file named by CGE_PROBE_OUT. Nothing is saved.
"""
import json
import os

import unreal

out_path = os.environ["CGE_PROBE_OUT"]
result = {"engine": unreal.SystemLibrary.get_engine_version(), "ok": False}


def vec(value):
    return [float(value.x), float(value.y), float(value.z)]


def transform(value):
    rotation = value.rotation
    return {
        "translation": vec(value.translation),
        "rotationQuat": [float(rotation.x), float(rotation.y), float(rotation.z), float(rotation.w)],
        "rotationEuler": [float(value.rotation.rotator().roll), float(value.rotation.rotator().pitch), float(value.rotation.rotator().yaw)],
        "scale": vec(value.scale3d),
    }


try:
    startup = unreal.get_default_object(unreal.GameMapsSettings).get_editor_property("editor_startup_map")
    map_path = str(startup.export_text()) if hasattr(startup, "export_text") else str(startup)
    map_path = map_path.strip('"')
    result["map"] = os.environ.get("CGE_PROBE_MAP", map_path)
    unreal.EditorLoadingAndSavingUtils.load_map(result["map"])
    world = unreal.EditorLevelLibrary.get_editor_world()
    settings = world.get_world_settings()
    result["killZ"] = float(settings.get_editor_property("kill_z"))
    result["enableWorldBoundsChecks"] = bool(settings.get_editor_property("enable_world_bounds_checks"))
    starts = []
    for actor in unreal.get_editor_subsystem(unreal.EditorActorSubsystem).get_all_level_actors():
        if actor.get_class().get_name() == "PlayerStart":
            location = actor.get_actor_location()
            rotation = actor.get_actor_rotation()
            starts.append({"name": actor.get_actor_label(), "location": vec(location), "yaw": float(rotation.yaw)})
    result["playerStarts"] = starts
    rig_path = os.environ.get("CGE_PROBE_RIG")
    if rig_path:
        rig = unreal.load_asset(rig_path)
        hierarchy = rig.get_hierarchy()
        controls = {}
        for name in ["BodyOffset", "Ctrl_Head", "Ctrl_Shoulder_L", "Ctrl_Shoulder_R"]:
            key = unreal.RigElementKey(type=unreal.RigElementType.CONTROL, name=name)
            entry = {}
            for label, getter in (
                ("globalInitial", lambda: hierarchy.get_global_transform(key, True)),
                ("globalCurrent", lambda: hierarchy.get_global_transform(key, False)),
                ("localInitial", lambda: hierarchy.get_local_transform(key, True)),
            ):
                try:
                    entry[label] = transform(getter())
                except Exception as exception:  # accessor が1つ見つからなくても、ほかの accessor を隠してはいけません。
                    entry[label + "Error"] = str(exception)
            try:
                parents = hierarchy.get_parents(key)
                entry["parents"] = [str(parent.name) for parent in parents]
            except Exception as exception:
                entry["parentsError"] = str(exception)
            controls[name] = entry
        result["controls"] = controls
        bones = {}
        for name in ["root", "pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "neck_01", "head", "clavicle_l", "upperarm_l", "clavicle_r", "upperarm_r", "thigh_l", "calf_l", "foot_l", "ik_foot_l", "ik_foot_root"]:
            key = unreal.RigElementKey(type=unreal.RigElementType.BONE, name=name)
            try:
                bones[name] = {"global": transform(hierarchy.get_global_transform(key, True))}
            except Exception as exception:
                bones[name] = {"error": str(exception)}
        result["bones"] = bones
    result["ok"] = True
except Exception as exception:  # probe は editor の実行を失敗させず、結果を報告します。
    result["error"] = str(exception)

with open(out_path, "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=2)
unreal.log("CGE-PROBE " + json.dumps({"map": result.get("map"), "ok": result["ok"], "killZ": result.get("killZ")}))
