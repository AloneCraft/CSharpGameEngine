"""Dumps the default values of an Unreal Blueprint class and its components to JSON (reference data for porting a template).

Run through run_dump_blueprint.cmd. CGE_BP_CLASS is the generated class path (for example
/Game/FirstPerson/Blueprints/BP_FirstPersonCharacter.BP_FirstPersonCharacter_C) and CGE_BP_OUT the output file. Every readable
property of the class default object and of each component (inherited components included) is written; values the dump cannot
express become their text. Nothing is saved.
"""
import json
import os

import unreal


def convert(value, depth=0):
    if value is None or isinstance(value, (bool, int, float, str)):
        return value
    if depth > 2:
        return str(value)
    if isinstance(value, (unreal.Vector, unreal.Vector2D)):
        return {"x": value.x, "y": value.y, **({"z": value.z} if hasattr(value, "z") else {})}
    if isinstance(value, unreal.Rotator):
        return {"pitch": value.pitch, "yaw": value.yaw, "roll": value.roll}
    if isinstance(value, unreal.LinearColor):
        return {"r": value.r, "g": value.g, "b": value.b, "a": value.a}
    if isinstance(value, unreal.Object):
        return value.get_path_name()
    if isinstance(value, (list, tuple, unreal.Array)):
        return [convert(item, depth + 1) for item in list(value)[:32]]
    return str(value)


def dump_object(obj) -> dict:
    result = {"class": obj.get_class().get_path_name()}
    properties = {}
    for name in dir(obj):
        if name.startswith("_") or name.startswith("get_") or name.startswith("set_") or callable(getattr(type(obj), name, None)):
            continue
        try:
            properties[name] = convert(obj.get_editor_property(name))
        except Exception:  # noqa: BLE001 - not every attribute is an editor property
            continue
    result["properties"] = properties
    return result


class_path = os.environ["CGE_BP_CLASS"]
out_path = os.environ["CGE_BP_OUT"]
generated = unreal.load_object(None, class_path)
default = unreal.get_default_object(generated)
result = {"class": class_path, "default": dump_object(default), "components": {}}

subsystem = unreal.get_engine_subsystem(unreal.SubobjectDataSubsystem)
blueprint_path = class_path.split(".")[0]
blueprint = unreal.load_asset(blueprint_path)
if blueprint is not None:
    for handle in subsystem.k2_gather_subobject_data_for_blueprint(blueprint):
        data = unreal.SubobjectDataBlueprintFunctionLibrary.get_data(handle)
        component = unreal.SubobjectDataBlueprintFunctionLibrary.get_associated_object(data)
        if component is not None:
            entry = dump_object(component)
            result["components"][component.get_name()] = entry

with open(out_path, "w", encoding="utf-8") as stream:
    json.dump(result, stream, indent=1, ensure_ascii=False)
unreal.log("CGE-BP-DUMP " + out_path + " components=" + str(len(result["components"])))
