"""ADR-0075 (M0098): a world-space procedural grid replaces baked grid textures on materials that carry one.

Unreal's M_PrototypeGrid / MF_ProcGrid projects a wire texture (R channel of T_GridChecker_A: a 5/512 wide
line on each border of the tile) along the world axes and blends the three projections with
CheapContrast(|N|, 1) = saturate(3|N| - 1). The edit evaluates the same lines analytically per pixel,
anti-aliased over the pixel footprint (the texture's mip chain does that in Unreal), and colours
lerp(lerp(surface, subGrid, subWire), grid, mainWire). Unreal's +Z is the engine's +Y and Unreal's +Y the
engine's -Z, so the XZ/YZ/XY projections become XY/ZY/XZ and the line pattern (symmetric about integers)
is unchanged. Materials without a grid (ObjectCB.WorldGridParams.x == 0) keep their output bit for bit.
"""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 BaseColorFactor;
    float4 MaterialParams;
    float4 EmissiveNits;
}""", """    float4 BaseColorFactor;
    float4 MaterialParams;
    float4 EmissiveNits;
    float4 WorldGridParams;
    float4 WorldGridColor;
    float4 WorldSubGridColor;
}""")
    source = replace_once(source, """float4 PSMain(VertexOutput input) : SV_Target
{""", """float WorldGridLineCoverage(float coordinate, float halfWidth)
{
    float footprint = max(fwidth(coordinate), 1e-6);
    float signedDistance = frac(coordinate + 0.5) - 0.5;
    float covered = saturate((min(signedDistance + 0.5 * footprint, halfWidth) - max(signedDistance - 0.5 * footprint, -halfWidth)) / footprint);
    return lerp(covered, 2.0 * halfWidth, smoothstep(0.5, 1.0, footprint));
}
float WorldGridPlane(float2 coordinate, float halfWidth)
{
    float lineU = WorldGridLineCoverage(coordinate.x, halfWidth);
    float lineV = WorldGridLineCoverage(coordinate.y, halfWidth);
    return lineU + lineV - lineU * lineV;
}
float WorldGridWire(float3 worldPosition, float3 normal, float cellMeters, float halfWidth)
{
    float3 cell = worldPosition / cellMeters;
    float3 weight = saturate(abs(normal) * 3.0 - 1.0);
    float wire = lerp(WorldGridPlane(cell.xy, halfWidth), WorldGridPlane(cell.zy, halfWidth), weight.x);
    return lerp(wire, WorldGridPlane(cell.xz, halfWidth), weight.y);
}
float4 PSMain(VertexOutput input) : SV_Target
{""")
    source = replace_once(source, """    float3 vertexNormal = normalize((input.Normal));
""", """    float3 vertexNormal = normalize((input.Normal));
    if (WorldGridParams.x > 0.0)
    {
        float mainWire = WorldGridWire(input.WorldPosition, vertexNormal, WorldGridParams.x, WorldGridParams.z);
        float subWire = WorldGridWire(input.WorldPosition, vertexNormal, WorldGridParams.x / WorldGridParams.y, WorldGridParams.z);
        baseColor = lerp(lerp(baseColor, WorldSubGridColor.rgb, subWire), WorldGridColor.rgb, mainWire);
    }
""")
    return source
