"""M0119 (ADR-0084): the logarithmic depth functions move from StagePresentationShaderSource into the Stage graph.

RadialMaximumDepth, RadialLog2OnePlus, RadialSafeLength, RadialG, EncodeRadialDepth and IsBeyondRadialFarPlane are
the one depth function every pass that writes a perspective view's depth shares (ADR-0076 invariant 4). They belong
to the Stage program, not to the suffix appended after it, so they become the Depth subgraph (Depth.cgesubgraph,
extracted by edit_stage_graph.py). The expressions are moved word for word: the radial entries still in the suffix
call them and every pixel stays as it was. Run this edit before the functions are deleted from the suffix: the text
moved is read from StagePresentationShaderSource.cs.
"""

import os
import re

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
SUFFIX_FILE = os.path.join(REPO, "src", "CSharpGameEngine.ShaderArtifacts", "StagePresentationShaderSource.cs")


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def depth_functions():
    text = open(SUFFIX_FILE, encoding="utf-8").read().replace("\r\n", "\n")
    start = text.index("static const float RadialMaximumDepth")
    end = text.index("struct RadialPixelOutput")
    block = re.sub(r"//[^\n]*\n", "", text[start:end])
    block = re.sub(r"\n\s*\n", "\n", block).strip("\n") + "\n"
    for name in ["RadialMaximumDepth", "RadialLog2OnePlus", "RadialSafeLength", "RadialG", "EncodeRadialDepth", "IsBeyondRadialFarPlane"]:
        if name not in block:
            raise ValueError(f"The suffix does not define {name}.")
    return block


def edit(source):
    source = source.replace("\r\n", "\n")
    return replace_once(source, "static const float PI = 3.14159265359;\n", "static const float PI = 3.14159265359;\n" + depth_functions())
