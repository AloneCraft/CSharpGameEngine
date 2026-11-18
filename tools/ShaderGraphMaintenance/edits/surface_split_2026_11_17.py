"""M0129 unit 2 (ADR-0100 decision 2, the surface half of ADR-0085 decision 2): PSMain becomes EvaluateSurface + EvaluateSurfaceLighting.

EvaluateSurface is the only place the main pass reads the material (the object constants and the four material textures) and
returns SurfaceData. EvaluateSurfaceLighting lights a SurfaceData and reads nothing of the material. The GPU scene (unit 3) and
the later virtual geometry and terrain entries replace or reuse EvaluateSurface without touching the lighting.

Every expression is moved word for word and the order of the floating-point additions is kept, so the seven golden frames stay
bit for bit. The only reordering is the emissive texture sample, which moves from after the lighting into EvaluateSurface; a
texture sample has no side effect. The finer split of the lighting ADR-0085 names (directional, local, indirect, fog) is left to
the milestone that implements the render paths.
"""

SIGNATURE = "StagePixelOutput PSMain(VertexOutput input)\n{\n"
SURFACE_START = "    float3 baseColor = "
LIGHTING_START = "    float3 v = normalize("
EMISSIVE = "    color += (EmissiveTexture.Sample(LinearSampler, (input.Uv))).rgb * EmissiveNits.xyz;\n"
RETURN = "    return CompleteStageSurface(input, (float4(color, 1.0)));\n}\n"

SURFACE_DATA = """struct SurfaceData
{
    float3 Position;
    float3 BaseColor;
    float Metallic;
    float Roughness;
    float3 Normal;
    float3 VertexNormal;
    float3 Emissive;
};
"""

SURFACE_END = """    SurfaceData surface;
    surface.Position = input.WorldPosition;
    surface.BaseColor = baseColor;
    surface.Metallic = metallic;
    surface.Roughness = roughness;
    surface.Normal = n;
    surface.VertexNormal = vertexNormal;
    surface.Emissive = (EmissiveTexture.Sample(LinearSampler, (input.Uv))).rgb * EmissiveNits.xyz;
    return surface;
}
"""

LIGHTING_HEAD = """float3 EvaluateSurfaceLighting(SurfaceData surface, float4 pixelPosition)
{
    float3 baseColor = surface.BaseColor;
    float roughness = surface.Roughness;
    float metallic = surface.Metallic;
    float3 vertexNormal = surface.VertexNormal;
    float3 n = surface.Normal;
"""

NEW_MAIN = """    SurfaceData surface = EvaluateSurface(input);
    return CompleteStageSurface(input, (float4((EvaluateSurfaceLighting(surface, (input.Position))), 1.0)));
}
"""


def split_once(text, marker):
    if text.count(marker) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {marker[:80]}")
    index = text.index(marker)
    return text[:index], text[index:]


def edit(source):
    source = source.replace("\r\n", "\n")
    before, main = split_once(source, SIGNATURE)
    if main.count(RETURN) != 1 or main[main.index(RETURN) + len(RETURN):].strip():
        raise ValueError("PSMain is not the last declaration of the Stage graph, or its return changed.")
    body = main[len(SIGNATURE):main.index(RETURN)]
    guard, rest = split_once(body, SURFACE_START)
    surface, lighting = split_once(rest, LIGHTING_START)
    if lighting.count(EMISSIVE) != 1:
        raise ValueError("The emissive term is not where this edit expects it.")
    lighting = lighting.replace(EMISSIVE, "    color += surface.Emissive;\n")
    lighting = lighting.replace("input.WorldPosition", "surface.Position").replace("input.Position", "pixelPosition")
    if "input." in lighting:
        raise ValueError("The lighting still reads the vertex output: " + lighting[lighting.index("input."):][:80])
    for name in ["BaseColorTexture", "NormalTexture", "MetallicRoughnessTexture", "EmissiveTexture", "BaseColorFactor", "MaterialParams", "EmissiveNits", "WorldGrid"]:
        if name in lighting:
            raise ValueError(f"The lighting still reads the material: {name}")
    return (
        before
        + SURFACE_DATA
        + "SurfaceData EvaluateSurface(VertexOutput input)\n{\n" + surface + SURFACE_END
        + LIGHTING_HEAD + lighting + "    return color;\n}\n"
        + SIGNATURE + guard + NEW_MAIN
    )
