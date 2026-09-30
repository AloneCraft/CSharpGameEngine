"""ADR-0069: dynamic diffuse GI probe sampling (StageGlobalIllumination on the C# side)."""


def replace_once(source, old, new):
    if source.count(old) != 1:
        raise ValueError(f"Expected exactly one occurrence of: {old[:80]}")
    return source.replace(old, new)


GI_FUNCTIONS = """float2 GiSignNotZero(float2 v)
{
    return select(v >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
}
float2 GiOctahedralEncode(float3 direction)
{
    float3 n = direction / (abs(direction.x) + abs(direction.y) + abs(direction.z));
    float2 e = n.xy;
    if (n.z < 0.0)
    {
        e = (1.0 - abs(n.yx)) * GiSignNotZero(n.xy);
    }
    return e;
}
float2 GiAtlasUv(uint probe, float3 direction, float interiorTexels, float2 atlasSize)
{
    float tile = interiorTexels + 2.0;
    uint row = GiGridCounts.w;
    float2 tileOrigin = float2((float)(probe % row), (float)(probe / row)) * tile;
    float2 texel = tileOrigin + 1.0 + (GiOctahedralEncode(direction) * 0.5 + 0.5) * interiorTexels;
    return texel / atlasSize;
}
float4 SampleGlobalIllumination(float3 worldPosition, float3 normal, float3 viewDirection)
{
    float3 spacing = GiGridSpacing.xyz;
    float3 counts = (float3)GiGridCounts.xyz;
    float minSpacing = min(spacing.x, min(spacing.y, spacing.z));
    float3 biasedPosition = worldPosition + (normal * 0.2 + viewDirection * 0.8) * (0.3 * minSpacing);
    float3 gridPosition = (biasedPosition - GiGridOrigin.xyz) / spacing;
    if (any(gridPosition < 0.0) || any(gridPosition > counts - 1.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float3 baseProbe = min(floor(gridPosition), counts - 2.0);
    float3 alpha = saturate(gridPosition - baseProbe);
    float3 irradiance = float3(0.0, 0.0, 0.0);
    float weightSum = 0.0;
    [unroll] for (int corner = 0; corner < 8; corner++)
    {
        float3 offset = float3((float)(corner & 1), (float)((corner >> 1) & 1), (float)((corner >> 2) & 1));
        float3 probeCoordinate = baseProbe + offset;
        float3 probePosition = GiGridOrigin.xyz + probeCoordinate * spacing;
        float3 trilinear = lerp(1.0 - alpha, alpha, offset);
        float weight = trilinear.x * trilinear.y * trilinear.z;
        float3 towardProbe = normalize(probePosition - worldPosition);
        float wrapShading = (dot(towardProbe, normal) + 1.0) * 0.5;
        weight *= wrapShading * wrapShading + 0.2;
        uint probe = (uint)(probeCoordinate.x + counts.x * (probeCoordinate.y + counts.y * probeCoordinate.z));
        float3 probeToPoint = biasedPosition - probePosition;
        float distanceToProbe = length(probeToPoint);
        float2 moments = GiDistanceAtlas.SampleLevel(LinearSampler, GiAtlasUv(probe, probeToPoint / max(distanceToProbe, 1e-4), 16.0, GiAtlasSize.zw), 0).xy;
        float variance = abs(moments.x * moments.x - moments.y);
        float chebyshev = 1.0;
        if (distanceToProbe > moments.x)
        {
            float excess = distanceToProbe - moments.x;
            chebyshev = variance / (variance + excess * excess);
            chebyshev = chebyshev * chebyshev * chebyshev;
        }
        weight *= max(chebyshev, 0.05);
        weight = max(weight, 1e-6);
        if (weight < 0.2)
        {
            weight *= weight * weight / 0.04;
        }
        float4 probeIrradiance = GiIrradianceAtlas.SampleLevel(LinearSampler, GiAtlasUv(probe, normal, 8.0, GiAtlasSize.xy), 0);
        weight *= probeIrradiance.a;
        irradiance += probeIrradiance.rgb * weight;
        weightSum += weight;
    }
    if (weightSum <= 1e-6)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(irradiance / weightSum * GiGridSpacing.w, 1.0);
}
"""


def edit(source):
    # Earlier migrations left mixed CRLF/LF line endings in the composed source.
    source = source.replace("\r\n", "\n")
    source = replace_once(source, """    float4 CascadeDepthRanges;
}""", """    float4 CascadeDepthRanges;
    float4 GiGridOrigin;
    float4 GiGridSpacing;
    uint4 GiGridCounts;
    float4 GiAtlasSize;
}""")
    source = replace_once(source, """StructuredBuffer<float4> SkyIrradianceSh : register(t11);
""", """StructuredBuffer<float4> SkyIrradianceSh : register(t11);
Texture2D GiIrradianceAtlas : register(t12);
Texture2D GiDistanceAtlas : register(t13);
""")
    source = replace_once(source, """float4 PSMain(VertexOutput input) : SV_Target
{""", GI_FUNCTIONS + """float4 PSMain(VertexOutput input) : SV_Target
{""")
    source = replace_once(source, """    float3 color = AmbientColor.xyz * baseColor + lighting;
    if (SkyLightParams.x > 0.5)
    {
        float nDotVAmbient = saturate((dot(n, v))) + 1e-4;
        float3 specularColor = EnvironmentBrdfApprox(f0, roughness, nDotVAmbient);
        float3 diffuseIrradiance = max((EvaluateSkyIrradiance(n)), 0.0);
        float3 reflectedIrradiance = max((EvaluateSkyIrradiance((reflect((-v), n)))), 0.0);
        color +=""", """    float3 color = AmbientColor.xyz * baseColor + lighting;
    float4 globalIllumination = float4(0.0, 0.0, 0.0, 0.0);
    if (GiGridCounts.w != 0u)
    {
        globalIllumination = SampleGlobalIllumination(input.WorldPosition, n, v);
    }
    if (SkyLightParams.x > 0.5 || globalIllumination.a > 0.0)
    {
        float nDotVAmbient = saturate((dot(n, v))) + 1e-4;
        float3 specularColor = EnvironmentBrdfApprox(f0, roughness, nDotVAmbient);
        float3 diffuseIrradiance = float3(0.0, 0.0, 0.0);
        float3 reflectedIrradiance = float3(0.0, 0.0, 0.0);
        if (SkyLightParams.x > 0.5)
        {
            diffuseIrradiance = max((EvaluateSkyIrradiance(n)), 0.0);
            reflectedIrradiance = max((EvaluateSkyIrradiance((reflect((-v), n)))), 0.0);
        }
        if (globalIllumination.a > 0.0)
        {
            float skyLuminance = dot(diffuseIrradiance, float3(0.2126, 0.7152, 0.0722));
            float probeLuminance = dot(globalIllumination.rgb, float3(0.2126, 0.7152, 0.0722));
            reflectedIrradiance *= lerp(1.0, saturate(probeLuminance / max(skyLuminance, 1e-4)), globalIllumination.a);
            diffuseIrradiance = lerp(diffuseIrradiance, globalIllumination.rgb, globalIllumination.a);
        }
        color +=""")
    return source
