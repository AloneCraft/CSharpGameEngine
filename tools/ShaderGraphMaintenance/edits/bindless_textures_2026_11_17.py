"""M0129 unit 4 (ADR-0100 decision 2): the material textures become one unbounded array, stage-v18.

The four material texture registers (t0 to t3) and the per-material descriptor table that filled them are removed. Every
material texture the renderer keeps is one view in Texture2D MaterialTextures[] (t0, space1), and MaterialData carries the
four indices (x base colour, y normal, z metallic-roughness, w emissive). A draw no longer switches a texture table, so a draw
that the GPU itself writes can show any material.

Each sample keeps its sampler, its coordinates and its view format (the renderer still makes an sRGB view for the base colour
and the emission), so the seven golden frames stay bit for bit. NonUniformResourceIndex is used although one draw still shows
one material, because the later indirect draws mix materials within a wave.

The shadow atlas keeps t4: the renderer moves its view from each material's table into the environment table, which needs no
shader change. The presentation suffix (StagePresentationShaderSource) is edited by hand in the same change.
"""

TEXTURES = """Texture2D BaseColorTexture : register(t0);
Texture2D NormalTexture : register(t1);
Texture2D MetallicRoughnessTexture : register(t2);
Texture2D EmissiveTexture : register(t3);
"""

ARRAY = "Texture2D MaterialTextures[] : register(t0, space1);\n"

SAMPLES = {
    "BaseColorTexture.Sample(": "MaterialTextures[NonUniformResourceIndex(material.Textures.x)].Sample(",
    "NormalTexture.Sample(": "MaterialTextures[NonUniformResourceIndex(material.Textures.y)].Sample(",
    "MetallicRoughnessTexture.Sample(": "MaterialTextures[NonUniformResourceIndex(material.Textures.z)].Sample(",
    "EmissiveTexture.Sample(": "MaterialTextures[NonUniformResourceIndex(material.Textures.w)].Sample(",
}


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise RuntimeError(f"expected exactly one occurrence of {old!r}, found {source.count(old)}")
    return source.replace(old, new)


def edit(source):
    source = replace_once(source, TEXTURES, ARRAY)
    source = replace_once(source, "    float4 WorldGridAnchor;\n};\nStructuredBuffer<InstanceData>", "    float4 WorldGridAnchor;\n    uint4 Textures;\n};\nStructuredBuffer<InstanceData>")
    first = source.index("SurfaceData EvaluateSurface(VertexOutput input)\n{\n")
    last = source.index("float3 EvaluateSurfaceLighting(", first)
    surface = source[first:last]
    for old, new in SAMPLES.items():
        surface = replace_once(surface, old, new)
    source = source[:first] + surface + source[last:]
    for name in ["BaseColorTexture", "NormalTexture", "MetallicRoughnessTexture", "EmissiveTexture"]:
        if name in source:
            raise RuntimeError(f"the graph still reads the removed texture {name}")
    return source
