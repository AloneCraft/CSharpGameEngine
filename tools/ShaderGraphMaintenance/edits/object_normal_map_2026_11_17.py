"""M0130 unit 6 (ADR-0100 decision 3): an object-space normal map for virtual geometry, stage-v21.

A virtual geometry mesh drawn coarser than its source (the triangle floor) loses the shading of the detail between the kept
vertices. The cook bakes the source mesh's smooth normals into a texture in object space, addressed by the shared UVs, so every
level of the cluster hierarchy reads the same normal. DrawCB gains DrawObjectNormalMap: the view index in MaterialTextures plus
one, or zero for none. When it is set, EvaluateSurface replaces the interpolated vertex normal by the map's normal turned by the
cofactor of the instance's World (the vertex shader's normal transform; identity for DrawFlagMeshSpace); the material's tangent-space normal map is then
applied on top as before. Every other draw passes zero, so the seven golden frames stay bit for bit.
"""

DRAW_OLD = """    uint DrawFlags;
}
static const uint DrawFlagMeshSpace = 1u;
"""
DRAW_NEW = """    uint DrawFlags;
    uint DrawObjectNormalMap;
}
static const uint DrawFlagMeshSpace = 1u;
"""
NORMAL_OLD = "    float3 vertexNormal = normalize((input.Normal));\n"
NORMAL_NEW = """    float3 vertexNormal = normalize((input.Normal));
    if (DrawObjectNormalMap != 0u)
    {
        float3 objectNormal = ((MaterialTextures[(NonUniformResourceIndex((DrawObjectNormalMap - 1u)))]).Sample(LinearSampler, (input.Uv))).rgb * 2.0 - 1.0;
        float3x3 normalLinear = (DrawFlags & DrawFlagMeshSpace) != 0u ? float3x3(1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0) : (float3x3)(instance.World);
        float3x3 normalCofactor = float3x3((cross((normalLinear[1]), (normalLinear[2]))), (cross((normalLinear[2]), (normalLinear[0]))), (cross((normalLinear[0]), (normalLinear[1]))));
        float normalDeterminant = dot((normalLinear[0]), (normalCofactor[0]));
        vertexNormal = normalize((mul(objectNormal, normalCofactor) * (normalDeterminant < 0.0 ? -1.0 : 1.0)));
    }
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise RuntimeError(f"expected exactly one occurrence of {old!r}, found {source.count(old)}")
    return source.replace(old, new)


def edit(source):
    source = replace_once(source, DRAW_OLD, DRAW_NEW)
    first = source.index("SurfaceData EvaluateSurface(VertexOutput input)\n{\n")
    last = source.index("float3 EvaluateSurfaceLighting(", first)
    surface = source[first:last]
    if "InstanceData instance = Instances[(input.Instance)];" not in surface:
        raise RuntimeError("EvaluateSurface no longer reads the instance first")
    surface = replace_once(surface, NORMAL_OLD, NORMAL_NEW)
    return source[:first] + surface + source[last:]
