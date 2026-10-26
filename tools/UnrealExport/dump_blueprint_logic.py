"""Dumps the logic of Unreal assets as text, so a template's behaviour can be read at code level (ADR-0078/M0102).

Run through run_dump_blueprint_logic.cmd. CGE_LOGIC_ASSETS is a JSON list of object paths (/Game/...) and CGE_LOGIC_OUT the output
directory. For every asset it writes:
- <Name>.t3d: the editor's text export of the whole asset (graphs with nodes, pins and links; anim state machines; widget trees;
  input mappings; curves). This is the complete source.
- <Name>.graphs.json (Blueprints only): every graph's nodes with titles, categories, positions and pins (type, default value,
  connected pins), read through BlueprintGraphEditor / BlueprintGraphPin.
- <Name>.dsl.txt (Blueprints only, when the EditorToolset plugin is enabled): Epic's Blueprint decompiler output (S-expressions).
- _summary.json: what was written and every error.
The decompiler adds and removes nodes in memory while it resolves types; nothing is ever saved.
"""
import glob
import json
import os
import re
import sys
import traceback

import unreal

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
from unreal_export_common import log, log_error  # noqa: E402 - UE runs this file as a script

OUT = os.environ["CGE_LOGIC_OUT"]
ASSETS = json.loads(os.environ["CGE_LOGIC_ASSETS"])


def export_t3d(asset, path):
    task = unreal.AssetExportTask()
    task.object = asset
    task.filename = path
    task.automated = True
    task.prompt = False
    task.replace_identical = True
    if not unreal.Exporter.run_asset_export_task(task) or not os.path.exists(path):
        # 拡張子から exporter を選択できないアセットでは、汎用の text exporter を強制します。
        exporter_class = unreal.load_class(None, "/Script/UnrealEd.ObjectExporterT3D")
        task.exporter = unreal.new_object(exporter_class)
        if not unreal.Exporter.run_asset_export_task(task) or not os.path.exists(path):
            raise RuntimeError("T3D export wrote no file")


def pin_record(pin):
    connections = []
    for other in pin.list_connected_pins():
        node = other.get_owning_node()
        connections.append({"node": node.get_name(), "pin": str(other.get_pin_name())})
    return {
        "name": str(pin.get_pin_name()),
        "direction": "in" if pin.get_pin_direction() == unreal.EdGraphPinDirection.EGPD_INPUT else "out",
        "type": str(pin.get_pin_type_display_string()),
        "value": str(pin.get_pin_value()),
        "links": connections,
    }


def graph_records(blueprint):
    graphs = []
    for graph in unreal.BlueprintEditorLibrary.list_graphs(blueprint):
        record = {"name": graph.get_name(), "class": graph.get_class().get_name(), "nodes": []}
        try:
            editor = unreal.BlueprintGraphEditor.get_graph_editor(graph)
            for node in editor.list_all_nodes():
                position = node.get_node_pos()
                pins = []
                for pin in node.list_all_pins():
                    try:
                        pins.append(pin_record(pin))
                    except Exception as ex:  # noqa: BLE001 - keep the rest of the node
                        pins.append({"error": str(ex)})
                record["nodes"].append({
                    "id": node.get_name(),
                    "class": node.get_class().get_name(),
                    "title": str(node.get_node_title()),
                    "category": str(node.get_node_category()),
                    "position": [position.x, position.y],
                    "pins": pins,
                })
        except Exception as ex:  # noqa: BLE001 - anim and state-machine graphs are not K2 graphs
            record["error"] = str(ex)
        graphs.append(record)
    return graphs


def value_text(value):
    """A property value as Unreal's own text (structs through export_text), so nested settings such as EngineSetup survive."""
    if hasattr(value, "export_text"):
        try:
            return value.export_text()
        except Exception:  # noqa: BLE001 - fall back to the Python text
            pass
    if isinstance(value, unreal.Object):
        return value.get_path_name()
    if isinstance(value, (list, tuple, unreal.Array)):
        return "[" + ", ".join(value_text(item) for item in value) + "]"
    return str(value)


ENGINE_ROOT = "C:/Program Files/Epic Games/UE_5.8/Engine"
_HEADER_CACHE = {}
_UPROPERTY = re.compile(r"UPROPERTY\s*\((?:[^()]|\([^()]*\))*\)\s*(?:[A-Za-z_][\w:<>, *&]*?)\s+([A-Za-z_]\w*)\s*(?:=[^;]*)?(?:\[[^\]]*\])?\s*;", re.S)


def header_text(class_name):
    """The engine header that declares U<class_name> (searched once under Engine/Source and Engine/Plugins by file name)."""
    if class_name in _HEADER_CACHE:
        return _HEADER_CACHE[class_name]
    text = ""
    for root in ("Source", "Plugins"):
        for path in glob.glob(f"{ENGINE_ROOT}/{root}/**/{class_name}.h", recursive=True):
            with open(path, encoding="utf-8", errors="replace") as stream:
                content = stream.read()
            if re.search(rf"class\s+(?:\w+\s+)?[UA]{class_name}\b", content):
                text += content
    _HEADER_CACHE[class_name] = text
    return text


def native_class_path(cls):
    """The first native (/Script/...) class of a class: a Blueprint generated class is followed through its Blueprint's ParentClass."""
    for _ in range(16):
        if cls is None:
            return ""
        path = cls.get_path_name()
        if path.startswith("/Script/"):
            return path
        blueprint = unreal.load_asset(path.rsplit(".", 1)[0])
        if not isinstance(blueprint, unreal.Blueprint):
            return ""
        try:
            cls = unreal.BlueprintEditorLibrary.get_blueprint_parent_class(blueprint)
        except Exception:  # noqa: BLE001 - without a parent the dump keeps the script-exposed properties only
            return ""
    return ""


def native_class_chain(obj):
    """The C++ class names of the object's class and its native parents, read from the headers' "class UX : public UY" lines.
    Python's dir() lists only script-exposed properties; Edit-only ones (EngineSetup, Mass, WheelSetups, ...) are still readable by
    name, so their names are taken from the headers."""
    path = native_class_path(obj.get_class())
    if not path:
        return []
    names = []
    name = path.rsplit(".", 1)[-1]
    while name and name not in names:
        names.append(name)
        match = re.search(rf"class\s+(?:\w+\s+)?[UA]{name}\s*:\s*public\s+[UA](\w+)", header_text(name))
        name = match.group(1) if match else ""
    return names


def header_property_names(class_name):
    return _UPROPERTY.findall(header_text(class_name))


def object_properties(obj):
    lines = {}
    candidates = [name for name in dir(obj) if not name.startswith("_")]
    for class_name in native_class_chain(obj):
        candidates.extend(header_property_names(class_name))
    for name in candidates:
        try:
            value = obj.get_editor_property(name)
        except Exception:  # noqa: BLE001 - not every attribute is an editor property
            continue
        lines.setdefault(name, f"  {name} = {value_text(value)}")
    return [lines[key] for key in sorted(lines)]


def component_records(blueprint):
    """Every property of the default object and of each component it owns. T3D writes only values that differ from the
    archetype, which hides inherited settings (the vehicle movement component's engine, gears and wheels); this writes all."""
    generated = blueprint.generated_class()
    if generated is None:
        return ""
    cdo = unreal.get_default_object(generated)
    lines = [f"# {cdo.get_name()} ({cdo.get_class().get_name()})"] + object_properties(cdo)
    seen = set()
    for name in sorted(dir(cdo)):
        if name.startswith("_"):
            continue
        try:
            value = cdo.get_editor_property(name)
        except Exception:  # noqa: BLE001
            continue
        if isinstance(value, unreal.ActorComponent) and value.get_path_name() not in seen:
            seen.add(value.get_path_name())
            lines.append("")
            lines.append(f"# component {name}: {value.get_name()} ({value.get_class().get_name()})")
            lines.extend(object_properties(value))
    return "\n".join(lines) + "\n"


def decompile(blueprint):
    from editor_toolset.toolsets.blueprint import BlueprintTools  # EditorToolset plugin がある場合のみ。
    parts = []
    for graph in unreal.BlueprintEditorLibrary.list_graphs(blueprint):
        try:
            parts.append(f";; ===== {graph.get_name()} =====\n{BlueprintTools.read_graph_dsl(graph)}")
        except Exception as ex:  # noqa: BLE001 - one graph's failure must not hide the others
            parts.append(f";; ===== {graph.get_name()} ===== (decompile failed: {ex})")
    return "\n\n".join(parts)


def main():
    os.makedirs(OUT, exist_ok=True)
    summary = []
    for path in ASSETS:
        name = path.rsplit("/", 1)[-1]
        entry = {"asset": path, "written": [], "errors": []}
        asset = unreal.load_asset(path)
        if asset is None:
            entry["errors"].append("not found")
            summary.append(entry)
            continue
        entry["class"] = asset.get_class().get_name()
        try:
            export_t3d(asset, os.path.join(OUT, name + ".t3d"))
            entry["written"].append(name + ".t3d")
        except Exception as ex:  # noqa: BLE001
            entry["errors"].append("t3d: " + str(ex))
        if isinstance(asset, unreal.Blueprint):
            # runtime で重要な値（vehicle movement component などの component template や wheel class の
            # 設定）は Blueprint 自体のテキストではなく、生成された class と default object にあります。
            generated = asset.generated_class()
            for suffix, obj in ((".class.t3d", generated), (".defaults.t3d", unreal.get_default_object(generated) if generated else None)):
                if obj is None:
                    continue
                try:
                    export_t3d(obj, os.path.join(OUT, name + suffix))
                    entry["written"].append(name + suffix)
                except Exception as ex:  # noqa: BLE001
                    entry["errors"].append(suffix + ": " + str(ex))
            try:
                with open(os.path.join(OUT, name + ".components.txt"), "w", encoding="utf-8", newline="\n") as stream:
                    stream.write(component_records(asset))
                entry["written"].append(name + ".components.txt")
            except Exception as ex:  # noqa: BLE001
                entry["errors"].append("components: " + str(ex))
            try:
                with open(os.path.join(OUT, name + ".graphs.json"), "w", encoding="utf-8", newline="\n") as stream:
                    json.dump(graph_records(asset), stream, ensure_ascii=False, indent=1)
                entry["written"].append(name + ".graphs.json")
            except Exception as ex:  # noqa: BLE001
                entry["errors"].append("graphs: " + str(ex))
            try:
                text = decompile(asset)
                with open(os.path.join(OUT, name + ".dsl.txt"), "w", encoding="utf-8", newline="\n") as stream:
                    stream.write(text)
                entry["written"].append(name + ".dsl.txt")
            except Exception as ex:  # noqa: BLE001
                entry["errors"].append("dsl: " + str(ex))
        summary.append(entry)
        log(f"Logic dump {path}: {entry['written']} errors={entry['errors']}")
    with open(os.path.join(OUT, "_summary.json"), "w", encoding="utf-8", newline="\n") as stream:
        json.dump(summary, stream, ensure_ascii=False, indent=1)


try:
    main()
except Exception as ex:  # noqa: BLE001 - top level must make commandlet failure visible
    log_error("FAILED: " + str(ex))
    log_error(traceback.format_exc())
    sys.exit(1)
