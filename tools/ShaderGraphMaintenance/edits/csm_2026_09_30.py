"""ADR-0069: four-cascade directional shadows (DirectionalShadowCascades on the C# side)."""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


def edit(source):
    source = replace_once(source, """    float4 PhysicalFogColor;
}""", """    float4 PhysicalFogColor;
    row_major float4x4 CascadeViewProjection[4];
    float4 CascadeSpheres[4];
    float4 CascadeUvScaleOffsets[4];
    float4 CascadeDepthRanges;
}""")
    source = replace_once(source, """float4 PSMain(VertexOutput input) : SV_Target
{""", """float ComputeCascadedShadow(float3 worldPosition, float3 normal, float biasMeters)
{
    [unroll] for (int cascade = 0; cascade < 4; cascade++)
    {
        float4 sphere = CascadeSpheres[cascade];
        float3 offset = worldPosition - sphere.xyz;
        if (dot(offset, offset) < sphere.w * sphere.w * 0.96)
        {
            float texelWorld = 2.0 * sphere.w / 512.0;
            float3 biasedPosition = worldPosition + normal * (texelWorld * 1.5);
            float4 shadowPosition = mul(float4(biasedPosition, 1.0), CascadeViewProjection[cascade]);
            float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
            float2 tileUv = projected.xy * float2(0.5, -0.5) + 0.5;
            if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
            {
                return 1.0;
            }
            float depthBias = (max(biasMeters, 0.0) + texelWorld) / CascadeDepthRanges[cascade];
            return ComputeAtlasShadowProjected(projected, tileUv, CascadeUvScaleOffsets[cascade], depthBias);
        }
    }
    return 1.0;
}
float4 PSMain(VertexOutput input) : SV_Target
{""")
    source = replace_once(source, """        float shadow = LightCounts.y == k ? ComputeDirectionalShadow((input.ShadowPosition), ((light.DirectionAndShadowBias).w)) : 1.0;""",
                          """        float shadow = LightCounts.y == k ? ComputeCascadedShadow((input.WorldPosition), vertexNormal, ((light.DirectionAndShadowBias).w)) : 1.0;""")
    return source
