"""M0129 unit 3 (ADR-0100 decision 2): the per-object constant buffer becomes the GPU scene, stage-v17.

ObjectCB (b1) is removed. Its fields move to two structured buffers the renderer fills once a frame:

- Instances (t20): one InstanceData an object - World, the conservative raster depth, the emissive scale, the selection weight
  and the index of its material;
- Materials (t21): one MaterialData a material - the base colour, roughness and metallic factors, the emission and the world grid.

A draw passes three root constants (DrawCB, b1): the instance it draws, the presentation mode and the draw flags. VSMain reads
the instance and hands its index to the pixel shader (VertexOutput.Instance), so a pixel never depends on how the vertex shader
came to its instance. DrawFlagMeshSpace draws the instance's mesh in its own space (the Lumen card capture).

What the CPU used to combine an object is now combined here from the same values: the emission is the material's emission times
the instance's scale, and a grid anchored to its object takes the negated translation of World. The presentation suffix
(StagePresentationShaderSource) is edited by hand in the same change, since its entries read the same data.
"""

import re

OBJECT_CB = """cbuffer ObjectCB : register(b1)
{
    row_major float4x4 World;
    float4 BaseColorFactor;
    float4 MaterialParams;
    float4 EmissiveNits;
    float4 WorldGridParams;
    float4 WorldGridColor;
    float4 WorldSubGridColor;
    float4 WorldGridAnchor;
    float4 ConservativeRasterDepth;
}
"""

SCENE = """cbuffer DrawCB : register(b1)
{
    uint DrawInstance;
    uint DrawPresentationMode;
    uint DrawFlags;
}
static const uint DrawFlagMeshSpace = 1u;
struct InstanceData
{
    row_major float4x4 World;
    float2 ConservativeRasterDepth;
    float EmissiveScale;
    float SelectionWeight;
    uint Material;
};
struct MaterialData
{
    float4 BaseColorFactor;
    float4 MaterialParams;
    float4 EmissiveNits;
    float4 WorldGridParams;
    float4 WorldGridColor;
    float4 WorldSubGridColor;
    float4 WorldGridAnchor;
};
StructuredBuffer<InstanceData> Instances : register(t20);
StructuredBuffer<MaterialData> Materials : register(t21);
"""

OBJECT_NAMES = ["World", "BaseColorFactor", "MaterialParams", "EmissiveNits", "WorldGridParams", "WorldGridColor", "WorldSubGridColor", "WorldGridAnchor", "ConservativeRasterDepth"]


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise RuntimeError(f"expected exactly one occurrence of {old!r}, found {source.count(old)}")
    return source.replace(old, new)


def section(source, start, end):
    first = source.index(start)
    last = source.index(end, first)
    return first, last


def bare(name):
    return re.compile(r"(?<![\w.])" + name + r"\b")


def edit(source):
    source = replace_once(source, OBJECT_CB, SCENE)
    source = replace_once(source, "    float3 DepthPosition : TEXCOORD3;\n", "    float3 DepthPosition : TEXCOORD3;\n    nointerpolation uint Instance : TEXCOORD4;\n")

    first, last = section(source, "VertexOutput VSMain(VertexInput input)\n{\n", "cbuffer ShadowFrameCB")
    vertex = source[first:last]
    vertex = replace_once(vertex, "    VertexOutput output;\n", "    VertexOutput output;\n    InstanceData instance = Instances[DrawInstance];\n    float4x4 world = instance.World;\n    if ((DrawFlags & DrawFlagMeshSpace) != 0u)\n    {\n        world = float4x4(1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 1.0);\n    }\n")
    vertex, count = bare("ConservativeRasterDepth").subn("instance.ConservativeRasterDepth", vertex)
    if count != 2:
        raise RuntimeError(f"VSMain: expected 2 ConservativeRasterDepth, found {count}")
    vertex, count = bare("World").subn("world", vertex)
    if count != 4:
        raise RuntimeError(f"VSMain: expected 4 World, found {count}")
    vertex = replace_once(vertex, "    output.Uv = input.Uv;\n", "    output.Uv = input.Uv;\n    output.Instance = DrawInstance;\n")
    source = source[:first] + vertex + source[last:]

    first, last = section(source, "float4 VSShadowMain(VertexInput input) : SV_Position\n{\n", "float DistributionGgx(")
    shadow = source[first:last]
    shadow, count = bare("World").subn("(Instances[DrawInstance].World)", shadow)
    if count != 1:
        raise RuntimeError(f"VSShadowMain: expected 1 World, found {count}")
    source = source[:first] + shadow + source[last:]

    first, last = section(source, "SurfaceData EvaluateSurface(VertexOutput input)\n{\n", "float3 EvaluateSurfaceLighting(")
    surface = source[first:last]
    surface = replace_once(surface, "SurfaceData EvaluateSurface(VertexOutput input)\n{\n", "SurfaceData EvaluateSurface(VertexOutput input)\n{\n    InstanceData instance = Instances[input.Instance];\n    MaterialData material = Materials[instance.Material];\n")
    surface = replace_once(surface, "input.WorldPosition + WorldGridAnchor.xyz;", "input.WorldPosition + (material.WorldGridAnchor.w > 0.5 ? -instance.World[3].xyz : material.WorldGridAnchor.xyz);")
    surface = replace_once(surface, "* EmissiveNits.xyz;", "* (material.EmissiveNits.xyz * instance.EmissiveScale);")
    for name, expected in [("BaseColorFactor", 1), ("MaterialParams", 2), ("WorldGridParams", 6), ("WorldGridColor", 1), ("WorldSubGridColor", 1)]:
        surface, count = bare(name).subn("material." + name, surface)
        if count != expected:
            raise RuntimeError(f"EvaluateSurface: expected {expected} {name}, found {count}")
    source = source[:first] + surface + source[last:]

    first, last = section(source, "struct InstanceData", "StructuredBuffer<InstanceData>")
    remaining = source[:first] + source[last:]
    for name in OBJECT_NAMES:
        if bare(name).search(remaining):
            raise RuntimeError(f"the graph still reads the removed object constant {name}")
    return source
