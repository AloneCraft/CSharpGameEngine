"""M0132 unit 4 (ADR-0100 decision 5): the directional shadow reads the rebuilt virtual shadow maps, stage-v22.

The M0115 layout (4 levels of 32 x 32 pages, a 4096 atlas of 32 pages a row) becomes the M0132 layout: 16 levels of
128 x 128 pages of 128 x 128 texels (a level is 16384 texels wide), a page table of 16 x 128 x 128 entries per frame and an
atlas of 64 pages a row. FrameCB.VirtualShadowProjection grows from 4 to 16 matrices (the frame constants grow from 1248
to 2016 bytes) and VirtualShadowParams.z carries the pixels of a metre seen one metre away.

The level is the one the request compute shader chose for the pixel: the footprint rule is inserted from
VirtualShadowMapPlanner.Hlsl in src/Engine/Rendering/VirtualShadowMaps.cs (the same text the request shader copies), with the
neighbour differences replaced by the screen derivatives. When a texel the filter needs is in a page that is not resident,
the next coarser level is tried (the planner also keeps the page three levels up); when no level has it, the cascades are used.
The 200 m limit of M0115 is gone (level 15 is about 131 km wide). The depth range of a level is max(4096, 2 x its width).
"""
import os
import re

REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))


def planner_hlsl():
    text = open(os.path.join(REPO, "src", "Engine", "Rendering", "VirtualShadowMaps.cs"), encoding="utf-8").read().replace("\r\n", "\n")
    match = re.search(r'public const string Hlsl="""\n(.*?)\n""";', text, re.S)
    if match is None:
        raise RuntimeError("VirtualShadowMapPlanner.Hlsl was not found.")
    return match.group(1) + "\n"


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise RuntimeError(f"expected exactly one occurrence of {old!r}, found {source.count(old)}")
    return source.replace(old, new)


def function_span(source, signature):
    start = source.index(signature)
    open_brace = source.index("{", start)
    depth = 0
    index = open_brace
    while True:
        if source[index] == "{":
            depth += 1
        elif source[index] == "}":
            depth -= 1
            if depth == 0:
                return start, index + 1
        index += 1


def replace_function(source, signature, text):
    start, end = function_span(source, signature)
    return source[:start] + text.strip("\n") + source[end:]


READ_DEPTH = """
bool ReadDirectionalSoftDepth(int mode, int level, int2 texel, out float depth)
{
    int resolution = mode == 0 ? 512 : 16384;
    depth = 1.0;
    if (any((texel < 0)) || any((texel >= resolution)))
        return false;
    if (mode == 0)
    {
        float4 tile = CascadeUvScaleOffsets[level];
        int2 atlas = (int2)((((float2(texel) + 0.5) / 512.0) * tile.xy + tile.zw) * 4096.0);
        depth = (ShadowAtlas.Load((int3(atlas, 0)))).r;
    }
    else
    {
        uint2 page = (uint2)texel / 128;
        uint physical = VirtualShadowPages[(uint)VirtualShadowParams.y + ((uint)level * 128 + page.y) * 128 + page.x];
        if (physical == 0)
            return false;
        physical -= 1;
        int2 atlas = int2((physical % 64), (physical / 64)) * 128 + texel % 128;
        depth = (VirtualShadowAtlas.Load((int3(atlas, 0)))).r;
    }
    return true;
}
"""

LEVEL_HELPERS = """
float VirtualShadowLightPlaneLength(float3 difference)
{
    return length((mul((float4(difference, 0.0)), (VirtualShadowProjection[0]))).xy) * 4.0;
}
int VirtualShadowFirstLevel(float3 worldPosition, float3 positionDx, float3 positionDy)
{
    float lowerBound = distance(worldPosition, (CameraPositionWS.xyz)) / VirtualShadowParams.z * 0.5;
    return VirtualShadowLevelForFootprint((VirtualShadowFootprint((VirtualShadowLightPlaneLength(positionDx)), (VirtualShadowLightPlaneLength(positionDy)), lowerBound)));
}
"""

MATCHED = """
float ComputeMatchedDirectionalShadow(
    float3 worldPosition,
    float3 normal,
    float biasMeters,
    int mode,
    out bool complete)
{
    complete = true;
    float3 dx = ddx(worldPosition);
    float3 dy = ddy(worldPosition);
    float3 biased = worldPosition + normal * max(biasMeters, 0.001);
    int first = 0;
    int last = 4;
    if (mode != 0)
    {
        first = VirtualShadowFirstLevel(worldPosition, dx, dy);
        last = 16;
    }
    for (    int level = first;
 level < last; level++)
    {
        float worldSpan = 8.0 * exp2(((float)level));
        float depthRange = max(4096.0, 2.0 * worldSpan);
        float4x4 projection = VirtualShadowProjection[level];
        if (mode == 0)
        {
            if (dot((worldPosition - (CascadeSpheres[level]).xyz), (worldPosition - (CascadeSpheres[level]).xyz)) >= ((CascadeSpheres[level]).w * (CascadeSpheres[level]).w) * 0.96)
                continue;
            worldSpan = 2.0 * (CascadeSpheres[level]).w;
            depthRange = CascadeDepthRanges[level];
            projection = CascadeViewProjection[level];
        }
        float4 p = mul((float4(biased, 1.0)), projection);
        float2 uv = float2((p.x * 0.5 + 0.5), (0.5 - p.y * 0.5));
        float guard = (DirectionalSoftShadowParams.x == 0.0 ? 0.0 : DirectionalSoftShadowParams.y) / worldSpan + 1.0 / (mode == 0 ? 512.0 : 16384.0);
        if (((any((uv < guard)) || any((uv > 1.0 - guard))) || p.z < 0.0) || p.z > 1.0)
            continue;
        float3 pdx = (mul((float4(dx, 0.0)), projection)).xyz;
        float3 pdy = (mul((float4(dy, 0.0)), projection)).xyz;
        float2 duvdx = float2((pdx.x * 0.5), (-pdx.y * 0.5));
        float2 duvdy = float2((pdy.x * 0.5), (-pdy.y * 0.5));
        float determinant = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
        float2 gradient = abs(determinant) > 1e-16 ? float2((pdx.z * duvdy.y - pdy.z * duvdx.y), (pdy.z * duvdx.x - pdx.z * duvdy.x)) / determinant : float2(0.0, 0.0);
        float visibility;
        bool filled = FilterDirectionalSoft(mode, level, uv, (p.z), gradient, worldSpan, depthRange, biasMeters, visibility);
        if (mode == 0)
        {
            complete = filled;
            return visibility;
        }
        if (filled)
            return visibility;
    }
    complete = mode == 0;
    return 1.0;
}
"""

VIRTUAL = """
float ComputeVirtualDirectionalShadow(float3 worldPosition, float3 normal, float biasMeters)
{
    if (VirtualShadowParams.x == 0.0)
        return ComputeCascadedShadow(worldPosition, normal, biasMeters);
    if (DirectionalSoftShadowParams.z > 0.0)
    {
        bool complete;
        float soft = ComputeMatchedDirectionalShadow(worldPosition, normal, biasMeters, 1, complete);
        if (complete)
            return soft;
        return ComputeMatchedDirectionalShadow(worldPosition, normal, biasMeters, 0, complete);
    }
    float3 positionDx = ddx(worldPosition);
    float3 positionDy = ddy(worldPosition);
    for (    int level = VirtualShadowFirstLevel(worldPosition, positionDx, positionDy);
 level < 16; level++)
    {
        float4 p = mul((float4(worldPosition, 1.0)), (VirtualShadowProjection[level]));
        float2 uv = float2((p.x * 0.5 + 0.5), (0.5 - p.y * 0.5));
        if (((any((uv < 0.0)) || any((uv >= 1.0))) || p.z < 0.0) || p.z > 1.0)
            continue;
        float texelWorld = (8.0 * exp2(((float)level))) / 16384.0;
        float depthRange = max(4096.0, 16.0 * exp2(((float)level)));
        p = mul((float4((worldPosition + (normal * texelWorld) * 1.5), 1.0)), (VirtualShadowProjection[level]));
        uv = float2((p.x * 0.5 + 0.5), (0.5 - p.y * 0.5));
        int2 center = (int2)floor((uv * 16384.0));
        float3 pdx = (mul((float4(positionDx, 0.0)), (VirtualShadowProjection[level]))).xyz;
        float3 pdy = (mul((float4(positionDy, 0.0)), (VirtualShadowProjection[level]))).xyz;
        float2 duvdx = float2((pdx.x * 0.5), (-pdx.y * 0.5));
        float2 duvdy = float2((pdy.x * 0.5), (-pdy.y * 0.5));
        float planeDet = duvdx.x * duvdy.y - duvdx.y * duvdy.x;
        float2 planeGradient = abs(planeDet) > 1e-16 ? float2((pdx.z * duvdy.y - pdy.z * duvdx.y), (pdy.z * duvdx.x - pdx.z * duvdy.x)) / planeDet : float2(0.0, 0.0);
        float result = 0.0;
        bool filled = true;
        for (        int i = 0;
 i < 9; i++)
        {
            int2 t = center + int2((i % 3 - 1), (i / 3 - 1));
            float depth;
            if (filled && ReadDirectionalSoftDepth(1, level, t, depth))
            {
                float receiverDepth = p.z + dot(planeGradient, (((float2)t + 0.5) / 16384.0 - uv));
                result += receiverDepth - biasMeters / depthRange <= depth ? 1.0 : 0.0;
            }
            else
            {
                filled = false;
            }
        }
        if (filled)
            return result / 9.0;
    }
    return ComputeCascadedShadow(worldPosition, normal, biasMeters);
}
"""


def edit(source):
    source = replace_once(source, "    row_major float4x4 VirtualShadowProjection[4];", "    row_major float4x4 VirtualShadowProjection[16];")
    source = replace_function(source, "bool ReadDirectionalSoftDepth(", READ_DEPTH)
    source = replace_once(source, "    float resolution = mode == 0 ? 512.0 : 4096.0;\n    float2 pixel", "    float resolution = mode == 0 ? 512.0 : 16384.0;\n    float2 pixel")
    source = replace_once(source, "    float resolution = mode == 0 ? 512.0 : 4096.0;\n    float searchRadius", "    float resolution = mode == 0 ? 512.0 : 16384.0;\n    float searchRadius")
    start, _ = function_span(source, "float ComputeMatchedDirectionalShadow(")
    source = source[:start] + planner_hlsl() + LEVEL_HELPERS.lstrip("\n") + source[start:]
    source = replace_function(source, "float ComputeMatchedDirectionalShadow(", MATCHED)
    source = replace_function(source, "float ComputeVirtualDirectionalShadow(", VIRTUAL)
    return source
