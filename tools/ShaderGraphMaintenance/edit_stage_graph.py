"""Applies a text edit to the production Stage shader graph and rebuilds its external subgraphs.

The Stage program is stored as typed HLSL syntax graphs (Stage.cgeshadergraph plus the Pbr,
Shadow, Pcss and Aces subgraphs). This script is the maintenance path for engine-authored
changes that are easier to express as HLSL than as visual nodes:

1. dump-hlsl composes the graph, and the fixed StagePresentationShaderSource suffix is removed;
2. an edit module rewrites that HLSL (python edit_stage_graph.py <edit.py>, where edit.py defines
   edit(source) -> source);
3. migrate-internal converts the result back to a syntax graph;
4. extract-subgraph-internal re-extracts the four subgraphs by declaration name, so indices follow
   any declarations the edit inserted;
5. the original document IDs are restored and the result is validated.

Run from the repository root after building tools/CSharpGameEngine.ShaderGraph.Cli (x64 Debug).
"""

import hashlib
import importlib.util
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
SHADERS = os.path.join(REPO, "assets", "Shaders")
CLI = os.path.join(REPO, "tools", "CSharpGameEngine.ShaderGraph.Cli", "bin", "x64", "Debug",
                   "net10.0-windows10.0.19041.0", "CSharpGameEngine.ShaderGraph.Cli.exe")
SUBGRAPHS = {
    "Pbr": ("PBR", ["PI", "DistributionGgx", "VisibilitySmithGgxCorrelated", "FresnelSchlick", "EvaluateLitSurface", "DistanceAttenuation"]),
    "Shadow": ("Shadow", ["ComputeAtlasShadowProjected;", "ComputeDirectionalShadow", "ComputeAtlasShadowProjected", "ComputeSpotShadow", "SelectPointShadowFace", "ComputePointShadow"]),
    "Pcss": ("PCSS", ["AreaPcssBlockerOffsets", "AreaPcssFilterOffsets", "LinearizeAreaShadowDepth", "ProjectAreaShadowDistance", "ComputeAreaShadow"]),
    "Aces": ("ACES", ["ToneMapAces"]),
}


def run(*arguments):
    result = subprocess.run([CLI, *arguments], capture_output=True, text=True)
    if result.returncode != 0:
        raise RuntimeError(f"{' '.join(arguments)} failed:\n{result.stdout}\n{result.stderr}")
    return result.stdout


def presentation_suffix():
    source = open(os.path.join(REPO, "src", "CSharpGameEngine.ShaderArtifacts", "StagePresentationShaderSource.cs"), encoding="utf-8").read()
    body = re.search(r'Source="""\r?\n(.*?)\r?\n(\s*)""";', source, re.S)
    indent = body.group(2)
    lines = [line[len(indent):] if line.startswith(indent) else line for line in body.group(1).split("\n")]
    return "\n".join(line.rstrip("\r") for line in lines)


def top_level_declarations(source):
    text = re.sub(r"//[^\n]*", "", source)
    declarations = []
    depth = 0
    start = 0
    index = 0
    while index < len(text):
        character = text[index]
        if character == "{":
            depth += 1
        elif character == "}":
            depth -= 1
            if depth == 0:
                following = index + 1
                while following < len(text) and text[following] in " \t\r\n":
                    following += 1
                if following < len(text) and text[following] == ";":
                    index = following
                declarations.append(text[start:index + 1].strip())
                start = index + 1
        elif character == ";" and depth == 0:
            declarations.append(text[start:index + 1].strip())
            start = index + 1
        index += 1
    return declarations


def declaration_index(declarations, name):
    forward_declaration = name.endswith(";")
    name = name.rstrip(";")
    for index, declaration in enumerate(declarations):
        header = declaration.split("{")[0] if not forward_declaration else declaration
        if forward_declaration and not (declaration.endswith(";") and "{" not in declaration):
            continue
        if not forward_declaration and declaration.endswith(";") and "{" not in declaration and "(" in declaration.split("=")[0]:
            continue
        if re.search(r"\b" + re.escape(name) + r"\b\s*(\(|\[|=)", header):
            return index
    raise KeyError(name)


def main():
    edit_module = sys.argv[1]
    specification = importlib.util.spec_from_file_location("stage_edit", edit_module)
    module = importlib.util.module_from_spec(specification)
    specification.loader.exec_module(module)
    work = tempfile.mkdtemp(prefix="cge-stage-edit-")
    try:
        dump = os.path.join(work, "dump.hlsl")
        run("dump-hlsl", os.path.join(SHADERS, "Stage.cgeshadergraph"), dump)
        composed = open(dump, "rb").read().decode("utf-8")
        suffix = presentation_suffix()
        if not composed.endswith(suffix):
            raise RuntimeError("The composed Stage HLSL does not end with StagePresentationShaderSource.")
        edited = module.edit(composed[:len(composed) - len(suffix)])
        source = os.path.join(work, "stage.hlsl")
        open(source, "w", encoding="utf-8", newline="\n").write(edited)
        graph = os.path.join(work, "Stage.cgeshadergraph")
        run("migrate-internal", source, "Stage", graph)
        declarations = top_level_declarations(edited)
        for file_name, (subgraph_name, names) in SUBGRAPHS.items():
            indices = sorted({declaration_index(declarations, name) for name in names})
            run("extract-subgraph-internal", graph, os.path.join(work, file_name + ".cgesubgraph"), subgraph_name, ",".join(map(str, indices)))
        replacements = {}
        for file_name in SUBGRAPHS:
            original = json.load(open(os.path.join(SHADERS, file_name + ".cgesubgraph"), encoding="utf-8"))["id"]
            replacements[json.load(open(os.path.join(work, file_name + ".cgesubgraph"), encoding="utf-8"))["id"]] = original
        replacements[json.load(open(graph, encoding="utf-8"))["id"]] = json.load(open(os.path.join(SHADERS, "Stage.cgeshadergraph"), encoding="utf-8"))["id"]
        for file_name in ["Stage.cgeshadergraph"] + [name + ".cgesubgraph" for name in SUBGRAPHS]:
            text = open(os.path.join(work, file_name), encoding="utf-8").read()
            for new_id, original_id in replacements.items():
                text = text.replace(new_id, original_id)
            open(os.path.join(work, file_name), "w", encoding="utf-8", newline="").write(text)
        run("validate", graph)
        check = os.path.join(work, "check.hlsl")
        run("dump-hlsl", graph, check)
        composed_check = open(check, "rb").read().decode("utf-8")
        golden = hashlib.sha256(composed_check[:len(composed_check) - len(suffix)].encode("utf-8")).hexdigest().upper()
        for file_name in ["Stage.cgeshadergraph"] + [name + ".cgesubgraph" for name in SUBGRAPHS]:
            shutil.copyfile(os.path.join(work, file_name), os.path.join(SHADERS, file_name))
        print("updated Stage graph;", len(declarations), "top-level declarations")
        print("golden composed Stage HLSL SHA-256 (ShaderGraphCompilerTests):", golden)
    finally:
        shutil.rmtree(work, ignore_errors=True)


if __name__ == "__main__":
    main()
