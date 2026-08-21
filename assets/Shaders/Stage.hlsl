// Textured PBR-lite shader for the ECS-driven stage (ADR-0016 vertical slice extended by
// ADR-0018, point/spot lights added by ADR-0037): GGX Cook-Torrance specular + Lambert diffuse,
// tangent-space normal mapping, up to four data-driven directional lights + a fixed-capacity set of
// Forward+-culled local lights + flat ambient term, and a simplified exponential height fog.
// Model matrix + material factors are per-object (ObjectCB, b1); view/projection, camera position,
// lights, ambient and fog are per-frame (FrameCB, b0). Textures are shared per-material via a
// 5-descriptor SRV table (t0=BaseColor, t1=Normal, t2=MetallicRoughness, t3=Emissive, t4=ShadowAtlas);
// a material baked to a
// flat factor (no spatial variation) binds a neutral 1x1 fallback texture in that slot.

// Root-SRV StructuredBuffers (t5..t10) hold local lights, Forward+ tiles, selected Spot/Point
// projections, and the independent Area/Emissive center-projection metadata and Atlas views.
#define MAX_DIRECTIONAL_LIGHTS 4
#define SHADOW_ATLAS_SIZE 4096.0
#define MAX_LIGHTS_PER_FORWARD_PLUS_TILE 64
#define MAX_POINT_SHADOW_PROJECTIONS 6
#define MAX_AREA_AND_EMISSIVE_SHADOW_PROJECTIONS 4
#define SHADOW_ATLAS_TILE_SIZE 1024.0

struct DirectionalLightData
{
    float4 DirectionAndShadowBias;  // xyz = unit surface-to-light, w = shadow bias in meters
    float4 ColorTimesIntensity;     // xyz used
};

struct LocalLightData
{
    float4 PositionAndRange;       // xyz = camera-relative world position, w = range
    float4 DirectionAndInnerCos;   // xyz = spot forward, w = cos(inner half-angle)
    float4 ColorAndOuterCos;       // xyz = color * candela, w = cos(outer half-angle)
    float4 TypeAndPadding;         // x = 0 point/1 spot/2 area, y = shadow projection index/base or -1, z = shadow bias
    float4 RightAndHalfWidth;      // xyz = area right, w = half-width (m)
    float4 UpAndHalfHeight;        // xyz = area up, w = half-height (m)
};

struct SpotShadowProjectionData
{
    row_major float4x4 ViewProjection;
    float4 UvScaleOffset;          // PhysicalShadowAtlasPlan allocation rectangle
};

struct PointShadowProjectionData
{
    row_major float4x4 ViewProjection;
    float4 UvScaleOffset;          // PhysicalShadowAtlasPlan allocation rectangle
};

struct AreaShadowMetadataData
{
    float4 SourceRadiusAndPadding; // x = rectangle half diagonal (m), y/z = near/far clip (m)
};

struct AreaShadowProjectionData
{
    row_major float4x4 ViewProjection;
    float4 UvScaleOffset;          // PhysicalShadowAtlasPlan Area/Emissive allocation rectangle
};

cbuffer FrameCB : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 CameraPositionWS;           // xyz used
    DirectionalLightData DirectionalLights[MAX_DIRECTIONAL_LIGHTS];
    float4 AmbientColor;               // xyz used
    float4 FogParams0;                 // x = density, y = height falloff
    float4 FogColor;                   // xyz used
    float4 ExposureParams;             // x = 2^-EV100
    int4 LightCounts;                  // x = local StructuredBuffer count, y = shadowed directional index, z = directional count
    uint4 ForwardPlusGrid;             // x/y = tile count, z = tile edge in pixels, w = per-tile cap
    row_major float4x4 ShadowViewProjection;
    float4 DirectionalShadowAtlasUvScaleOffset; // xy = scale, zw = offset; PhysicalShadowAtlasPlan tile 0
};

cbuffer ObjectCB : register(b1)
{
    row_major float4x4 World;
    float4 BaseColorFactor;
    float4 MaterialParams; // x = roughness factor, y = metallic factor
    float4 EmissiveNits; // xyz = emitted luminance in cd/m²
};

Texture2D BaseColorTexture : register(t0);
Texture2D NormalTexture : register(t1);
Texture2D MetallicRoughnessTexture : register(t2);
Texture2D EmissiveTexture : register(t3);
Texture2D ShadowAtlas : register(t4);
StructuredBuffer<LocalLightData> LocalLights : register(t5);
StructuredBuffer<uint> ForwardPlusTileLightIndices : register(t6);
StructuredBuffer<SpotShadowProjectionData> SpotShadowProjections : register(t7);
StructuredBuffer<PointShadowProjectionData> PointShadowProjections : register(t8);
StructuredBuffer<AreaShadowMetadataData> AreaShadowMetadata : register(t9);
StructuredBuffer<AreaShadowProjectionData> AreaShadowProjections : register(t10);
SamplerState LinearSampler : register(s0);
SamplerComparisonState ShadowSampler : register(s1);

static const float PI = 3.14159265359;

struct VertexInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT; // xyz = tangent, w = bitangent sign
    float2 Uv : TEXCOORD;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float2 Uv : TEXCOORD1;
    float4 ShadowPosition : TEXCOORD2;
};

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0), World);
    output.Position = mul(worldPosition, ViewProjection);
    output.WorldPosition = worldPosition.xyz;
    output.Normal = mul(input.Normal, (float3x3)World);
    output.Tangent = float4(mul(input.Tangent.xyz, (float3x3)World), input.Tangent.w);
    output.Uv = input.Uv;
    output.ShadowPosition = mul(worldPosition, ShadowViewProjection);
    return output;
}

// シャドウパス(深度のみ・NumRenderTargets=0)専用の頂点シェーダー。ShadowConstantsBufferの内容と
// 正確に一致する最小限のcbufferだけを宣言する(ObjectCB(b1)はメインパスと共有・変更なし)。
// 以前はメインのVSMainを流用しており、ViewProjection(FrameCBのオフセット0)がシャドウパスでは
// b0の再バインド(ShadowConstantsBuffer)によって偶然シャドウ行列を指すという脆い挙動に依存していた
// うえ、VSMainが計算する未使用のShadowPosition varying(FrameCBのオフセット496から読む)が
// ShadowConstantsBufferの確保範囲を超えて読む未定義動作を含んでいた。
cbuffer ShadowFrameCB : register(b0)
{
    row_major float4x4 ShadowViewProjectionOnly;
};

float4 VSShadowMain(VertexInput input) : SV_Position
{
    float4 worldPosition = mul(float4(input.Position, 1.0), World);
    return mul(worldPosition, ShadowViewProjectionOnly);
}

// GGX normal distribution function (Trowbridge-Reitz).
float DistributionGgx(float nDotH, float roughness)
{
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float denom = (nDotH * nDotH * (alpha2 - 1.0)) + 1.0;
    return alpha2 / (PI * denom * denom);
}

// Smith height-correlated visibility term (= geometry term / (4 * NdotL * NdotV)), Karis 2013.
float VisibilitySmithGgxCorrelated(float nDotL, float nDotV, float roughness)
{
    float alpha = roughness * roughness;
    float alpha2 = alpha * alpha;
    float lambdaV = nDotL * sqrt((nDotV * nDotV * (1.0 - alpha2)) + alpha2);
    float lambdaL = nDotV * sqrt((nDotL * nDotL * (1.0 - alpha2)) + alpha2);
    return 0.5 / max(lambdaV + lambdaL, 1e-5);
}

float3 FresnelSchlick(float vDotH, float3 f0)
{
    return f0 + ((1.0 - f0) * pow(saturate(1.0 - vDotH), 5.0));
}

// Shared Cook-Torrance evaluation for a light arriving from direction `l` (unit, surface-to-light),
// already scaled by whatever distance/cone attenuation the caller computed.
float3 EvaluateLitSurface(float3 n, float3 v, float3 l, float3 baseColor, float roughness, float metallic, float3 f0)
{
    float3 h = normalize(v + l);
    float nDotL = saturate(dot(n, l));
    float nDotV = saturate(dot(n, v)) + 1e-4;
    float nDotH = saturate(dot(n, h));
    float vDotH = saturate(dot(v, h));

    float3 fresnel = FresnelSchlick(vDotH, f0);
    float d = DistributionGgx(nDotH, roughness);
    float vis = VisibilitySmithGgxCorrelated(nDotL, nDotV, roughness);
    float3 specular = d * vis * fresnel;

    float3 kd = (1.0 - fresnel) * (1.0 - metallic);
    float3 diffuse = kd * baseColor / PI;

    return (diffuse + specular) * nDotL;
}

// Smooth windowed inverse-square falloff that reaches exactly zero at `range` (Karis, "Moving
// Frostbite to PBR" 2014), avoiding both an unbounded 1/d^2 singularity and a hard-edged cutoff.
float DistanceAttenuation(float distance, float range)
{
    float distSq = distance * distance;
    float rangeSq = max(range * range, 1e-4);
    float windowed = saturate(1.0 - ((distSq * distSq) / (rangeSq * rangeSq)));
    return (windowed * windowed) / max(distSq, 1e-4);
}

float ComputeSpotShadow(float3 worldPosition, int projectionIndex, float shadowBias);
float ComputePointShadow(float3 worldPosition, float3 lightPosition, int projectionBaseIndex, float shadowBias);
float ComputeAreaShadow(float3 worldPosition, int projectionIndex, float shadowBias);

// Identity-LTC small-rectangle approximation. The emitted RGB is luminance in nits, so the
// source's projected solid angle produces irradiance directly; EvaluateLitSurface supplies the
// receiver cosine and BRDF. A later Atlas phase layers Area PCSS over this direct term.
float3 ComputeAreaLightContribution(LocalLightData light, float3 worldPosition, float3 n, float3 v, float3 baseColor, float roughness, float metallic, float3 f0)
{
    float3 toLight = light.PositionAndRange.xyz - worldPosition;
    float distance = length(toLight);
    float range = light.PositionAndRange.w;
    if (distance >= range)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 l = toLight / max(distance, 1e-5);
    // Direction points from the rectangle's emitting face into the scene. A receiver behind the
    // plane has a non-positive source cosine and receives no direct contribution.
    float sourceCosine = dot(light.DirectionAndInnerCos.xyz, -l);
    if (sourceCosine <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float rectangleArea = 4.0 * max(light.RightAndHalfWidth.w, 0.0) * max(light.UpAndHalfHeight.w, 0.0);
    // A missing/non-selected Area allocation has y=-1, and must retain its direct contribution.
    // The selected center projection uses the existing 3x3 atlas PCF in this first checkpoint.
    float shadow = ComputeAreaShadow(worldPosition, (int)light.TypeAndPadding.y,
        light.TypeAndPadding.z);
    float projectedSolidAngle = min(PI, rectangleArea * sourceCosine / max(distance * distance, 1e-4));
    return EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0)
        * light.ColorAndOuterCos.xyz * projectedSolidAngle * shadow;
}

float3 ComputeLocalLightContribution(LocalLightData light, float3 worldPosition, float3 n, float3 v, float3 baseColor, float roughness, float metallic, float3 f0)
{
    if (light.TypeAndPadding.x >= 1.5)
    {
        return ComputeAreaLightContribution(light, worldPosition, n, v, baseColor, roughness, metallic, f0);
    }

    float3 toLight = light.PositionAndRange.xyz - worldPosition;
    float distance = length(toLight);
    float range = light.PositionAndRange.w;

    if (distance >= range)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 l = toLight / max(distance, 1e-5);
    float coneAttenuation = 1.0;
    float shadow = 1.0;
    if (light.TypeAndPadding.x < 0.5)
    {
        shadow = ComputePointShadow(worldPosition, light.PositionAndRange.xyz,
            (int)light.TypeAndPadding.y, light.TypeAndPadding.z);
    }
    else
    {
        // The cone axis points AWAY from the light (toward the scene), so the surface is inside
        // the cone when the direction FROM the light TO the surface (-l) aligns with that axis.
        float innerCos = light.DirectionAndInnerCos.w;
        float outerCos = light.ColorAndOuterCos.w;
        float cosAngle = dot(-l, light.DirectionAndInnerCos.xyz);
        coneAttenuation = saturate((cosAngle - outerCos) / max(innerCos - outerCos, 1e-4));
        coneAttenuation *= coneAttenuation;
        if (coneAttenuation <= 0.0)
        {
            return float3(0.0, 0.0, 0.0);
        }
        shadow = ComputeSpotShadow(worldPosition, (int)light.TypeAndPadding.y,
            light.TypeAndPadding.z);
    }

    return EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0)
        * light.ColorAndOuterCos.xyz * DistanceAttenuation(distance, range) * coneAttenuation * shadow;
}

float ComputeAtlasShadowProjected(
    float3 projected, float2 tileUv, float4 uvScaleOffset, float shadowBias);

float ComputeDirectionalShadow(float4 shadowPosition, float shadowBias)
{
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 tileUv = (projected.xy * float2(0.5, -0.5)) + 0.5;
    if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    return ComputeAtlasShadowProjected(projected,tileUv,
        DirectionalShadowAtlasUvScaleOffset,shadowBias);
}

float ComputeAtlasShadowProjected(
    float3 projected,float2 tileUv,float4 uvScaleOffset,float shadowBias)
{
    float2 atlasUv = tileUv * uvScaleOffset.xy + uvScaleOffset.zw;
    float bias = max(shadowBias, 0.0);
    float2 texel = 1.0 / SHADOW_ATLAS_SIZE;
    // Keep PCF taps inside the allocated rectangle. This prevents neighboring atlas tiles from
    // influencing a valid projection as additional shadow-light types are added.
    float2 tileMin = uvScaleOffset.zw + (texel * 0.5);
    float2 tileMax = uvScaleOffset.zw + uvScaleOffset.xy - (texel * 0.5);
    float visibility = 0.0;
    [unroll]
    for (int x = -1; x <= 1; x++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            float2 sampleUv = clamp(
                atlasUv + float2(x, y) * texel,
                tileMin,tileMax);
            visibility += ShadowAtlas.SampleCmpLevelZero(ShadowSampler,sampleUv,projected.z-bias);
        }
    }
    return visibility / 9.0;
}

float ComputeSpotShadow(float3 worldPosition, int projectionIndex, float shadowBias)
{
    if (projectionIndex < 0 || projectionIndex >= max(LightCounts.w, 0))
    {
        return 1.0;
    }

    SpotShadowProjectionData projection = SpotShadowProjections[projectionIndex];
    float4 shadowPosition = mul(float4(worldPosition, 1.0), projection.ViewProjection);
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 tileUv = (projected.xy * float2(0.5, -0.5)) + 0.5;
    if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    return ComputeAtlasShadowProjected(projected,tileUv,projection.UvScaleOffset,shadowBias);
}

int SelectPointShadowFace(float3 toReceiver)
{
    float3 absDirection = abs(toReceiver);
    if (absDirection.x >= absDirection.y && absDirection.x >= absDirection.z)
    {
        return toReceiver.x >= 0.0 ? 0 : 1; // +X, -X
    }
    if (absDirection.y >= absDirection.z)
    {
        return toReceiver.y >= 0.0 ? 2 : 3; // +Y, -Y
    }
    return toReceiver.z >= 0.0 ? 4 : 5; // +Z, -Z
}

float ComputePointShadow(
    float3 worldPosition, float3 lightPosition, int projectionBaseIndex, float shadowBias)
{
    float3 toReceiver = worldPosition - lightPosition;
    if (dot(toReceiver, toReceiver) <= 1e-8)
    {
        return 1.0;
    }

    int projectionIndex = projectionBaseIndex + SelectPointShadowFace(toReceiver);
    if (projectionBaseIndex < 0 || projectionIndex < 0
        || projectionIndex >= MAX_POINT_SHADOW_PROJECTIONS)
    {
        // Point allocation overflow or a non-shadow-casting Point never removes direct light.
        return 1.0;
    }

    PointShadowProjectionData projection = PointShadowProjections[projectionIndex];
    float4 shadowPosition = mul(float4(worldPosition, 1.0), projection.ViewProjection);
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 tileUv = (projected.xy * float2(0.5, -0.5)) + 0.5;
    if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    return ComputeAtlasShadowProjected(projected,tileUv,projection.UvScaleOffset,shadowBias);
}

static const float2 AreaPcssBlockerOffsets[8] =
{
    float2(-0.7071, -0.3535), float2(-0.3535, 0.7071),
    float2(0.3535, -0.7071), float2(0.7071, 0.3535),
    float2(-0.9239, 0.1464), float2(0.1464, 0.9239),
    float2(0.9239, -0.1464), float2(-0.1464, -0.9239)
};

static const float2 AreaPcssFilterOffsets[12] =
{
    float2(-0.3262, -0.4058), float2(-0.8401, -0.0736),
    float2(-0.6959, 0.4571), float2(-0.2033, 0.6207),
    float2(0.9623, -0.1949), float2(0.4734, -0.4800),
    float2(0.5195, 0.7670), float2(0.1855, -0.8931),
    float2(0.5074, 0.0644), float2(0.8962, 0.4125),
    float2(-0.3219, -0.9326), float2(-0.7916, -0.5977)
};

float LinearizeAreaShadowDepth(float depth, float nearDistance, float farDistance)
{
    return (nearDistance * farDistance)
        / max(farDistance - (depth * (farDistance - nearDistance)), 1e-5);
}

float ProjectAreaShadowDistance(float distance, float nearDistance, float farDistance)
{
    return farDistance * (distance - nearDistance)
        / max(distance * (farDistance - nearDistance), 1e-5);
}

// Area and Emissive patches share type=2. Their plan-owned Atlas tile is a center projection.
// This deliberately leaves pixels outside that projection directly lit; the source's rectangular
// solid-angle direct-light calculation remains valid even when no shadow tile covers the point.
float ComputeAreaShadow(float3 worldPosition, int projectionIndex, float shadowBias)
{
    if (projectionIndex < 0 || projectionIndex >= MAX_AREA_AND_EMISSIVE_SHADOW_PROJECTIONS)
    {
        // A non-selected Area/Emissive patch remains directly lit.
        return 1.0;
    }

    AreaShadowMetadataData metadata = AreaShadowMetadata[projectionIndex];
    float sourceRadius = metadata.SourceRadiusAndPadding.x;
    float nearDistance = metadata.SourceRadiusAndPadding.y;
    float farDistance = metadata.SourceRadiusAndPadding.z;
    if (sourceRadius <= 0.0 || nearDistance <= 0.0 || farDistance <= nearDistance)
    {
        return 1.0;
    }

    AreaShadowProjectionData projection = AreaShadowProjections[projectionIndex];
    float4 shadowPosition = mul(float4(worldPosition, 1.0), projection.ViewProjection);
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 tileUv = (projected.xy * float2(0.5, -0.5)) + 0.5;
    if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    float receiverDepth = projected.z;
    float receiverDistance = LinearizeAreaShadowDepth(
        receiverDepth, nearDistance, farDistance);
    float comparisonDistance = max(receiverDistance - max(shadowBias, 0.0), nearDistance);
    float comparisonDepth = ProjectAreaShadowDistance(
        comparisonDistance, nearDistance, farDistance);
    float2 atlasUv = tileUv * projection.UvScaleOffset.xy + projection.UvScaleOffset.zw;
    float2 atlasTexel = 1.0 / SHADOW_ATLAS_SIZE;
    float2 tileMin = projection.UvScaleOffset.zw + (atlasTexel * 0.5);
    float2 tileMax = projection.UvScaleOffset.zw + projection.UvScaleOffset.xy - (atlasTexel * 0.5);

    // Eight raw-depth taps find average blockers in an angular footprint proportional to the
    // rectangle half diagonal. Load intentionally bypasses the comparison sampler here.
    float blockerSearchRadius = clamp(
        0.5 * sourceRadius / max(receiverDistance, 0.01),
        1.0 / SHADOW_ATLAS_TILE_SIZE, 0.25);
    float2 blockerSearchAtlasRadius = blockerSearchRadius * projection.UvScaleOffset.xy;
    float blockerDistanceSum = 0.0;
    float blockerCount = 0.0;
    [unroll]
    for (int blockerTap = 0; blockerTap < 8; blockerTap++)
    {
        float2 sampleUv = clamp(
            atlasUv + (AreaPcssBlockerOffsets[blockerTap] * blockerSearchAtlasRadius),
            tileMin, tileMax);
        int2 sampleTexel = int2(sampleUv * SHADOW_ATLAS_SIZE);
        float blockerDepth = ShadowAtlas.Load(int3(sampleTexel, 0)).r;
        if (blockerDepth < comparisonDepth)
        {
            blockerDistanceSum += LinearizeAreaShadowDepth(
                blockerDepth, nearDistance, farDistance);
            blockerCount += 1.0;
        }
    }
    if (blockerCount <= 0.0)
    {
        return 1.0;
    }

    float blockerDistance = blockerDistanceSum / blockerCount;
    float penumbraWorldRadius = sourceRadius * max(receiverDistance - blockerDistance, 0.0)
        / max(blockerDistance, 0.01);
    float filterRadius = clamp(
        penumbraWorldRadius / max(2.0 * receiverDistance, 0.01),
        1.0 / SHADOW_ATLAS_TILE_SIZE, 0.25);
    float2 filterAtlasRadius = filterRadius * projection.UvScaleOffset.xy;
    float visibility = 0.0;
    [unroll]
    for (int filterTap = 0; filterTap < 12; filterTap++)
    {
        float2 sampleUv = clamp(
            atlasUv + (AreaPcssFilterOffsets[filterTap] * filterAtlasRadius),
            tileMin, tileMax);
        visibility += ShadowAtlas.SampleCmpLevelZero(
            ShadowSampler, sampleUv, comparisonDepth);
    }
    return visibility / 12.0;
}

float3 ToneMapAces(float3 color)
{
    float3 a = color * (2.51 * color + 0.03);
    float3 b = color * (2.43 * color + 0.59) + 0.14;
    return saturate(a / b);
}

struct PostProcessVertexOutput
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
};

PostProcessVertexOutput FullscreenVS(uint vertexId : SV_VertexID)
{
    float2 positions[3] = {
        float2(-1.0, -1.0), float2(-1.0, 3.0), float2(3.0, -1.0)
    };
    float2 uvs[3] = {
        float2(0.0, 1.0), float2(0.0, -1.0), float2(2.0, 1.0)
    };

    PostProcessVertexOutput output;
    output.Position = float4(positions[vertexId], 0.0, 1.0);
    output.Uv = uvs[vertexId];
    return output;
}

Texture2D SceneColor : register(t0);
SamplerState PostProcessSampler : register(s0);

float4 PostProcessPS(PostProcessVertexOutput input) : SV_Target
{
    uint width;
    uint height;
    SceneColor.GetDimensions(width, height);
    float2 texel = 1.0 / float2(width, height);

    // Thresholded 3x3 spatial blur: neighboring HDR pixels contribute only when bright.
    float3 center = SceneColor.SampleLevel(PostProcessSampler, input.Uv, 0.0).rgb;
    float3 bloom = 0.0;
    const float weights[3] = { 0.0625, 0.125, 0.0625 };
    [unroll]
    for (int x = -1; x <= 1; x++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            float3 sampleColor = SceneColor.SampleLevel(
                PostProcessSampler, input.Uv + (float2(x, y) * texel), 0.0).rgb;
            float3 bright = max(sampleColor - 1.0, 0.0);
            bloom += bright * weights[x + 1] * weights[y + 1];
        }
    }

    float3 color = center + (bloom * 1.8);
    return float4(ToneMapAces(color), 1.0);
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float3 baseColor = BaseColorTexture.Sample(LinearSampler, input.Uv).rgb * BaseColorFactor.rgb;

    float3 mr = MetallicRoughnessTexture.Sample(LinearSampler, input.Uv).rgb;
    float roughness = saturate(mr.g * MaterialParams.x);
    float metallic = saturate(mr.b * MaterialParams.y);
    roughness = max(roughness, 0.045); // avoid a singular GGX lobe at exactly zero roughness.

    float3 vertexNormal = normalize(input.Normal);
    float3 tangent = normalize(input.Tangent.xyz - (vertexNormal * dot(vertexNormal, input.Tangent.xyz)));
    float3 bitangent = cross(vertexNormal, tangent) * input.Tangent.w;
    float3x3 tbn = float3x3(tangent, bitangent, vertexNormal);

    float3 normalSampleTs = normalize((NormalTexture.Sample(LinearSampler, input.Uv).rgb * 2.0) - 1.0);
    float3 n = normalize(mul(normalSampleTs, tbn));

    float3 v = normalize(CameraPositionWS.xyz - input.WorldPosition);
    float3 f0 = lerp(float3(0.04, 0.04, 0.04), baseColor, metallic);

    float3 lighting = float3(0.0, 0.0, 0.0);
    int numDirectionalLights = LightCounts.z;
    for (int k = 0; k < numDirectionalLights; k++)
    {
        DirectionalLightData light = DirectionalLights[k];
        float3 l = normalize(light.DirectionAndShadowBias.xyz);
        float shadow = LightCounts.y == k
            ? ComputeDirectionalShadow(input.ShadowPosition, light.DirectionAndShadowBias.w)
            : 1.0;
        lighting += EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0)
            * light.ColorTimesIntensity.xyz * shadow;
    }

    uint tileEdgePixels = max(ForwardPlusGrid.z, 1u);
    uint tileX = min((uint)(input.Position.x / (float)tileEdgePixels), ForwardPlusGrid.x - 1u);
    uint tileY = min((uint)(input.Position.y / (float)tileEdgePixels), ForwardPlusGrid.y - 1u);
    uint tileRecordOffset = ((tileY * ForwardPlusGrid.x) + tileX) * (MAX_LIGHTS_PER_FORWARD_PLUS_TILE + 1u);
    uint tileLightCount = min(
        ForwardPlusTileLightIndices[tileRecordOffset], (uint)MAX_LIGHTS_PER_FORWARD_PLUS_TILE);
    uint localLightCount = (uint)max(LightCounts.x, 0);
    [loop]
    for (uint tileLight = 0u; tileLight < tileLightCount; tileLight++)
    {
        uint localLightIndex = ForwardPlusTileLightIndices[tileRecordOffset + tileLight + 1u];
        if (localLightIndex < localLightCount)
        {
            lighting += ComputeLocalLightContribution(
                LocalLights[localLightIndex], input.WorldPosition, n, v, baseColor, roughness, metallic, f0);
        }
    }

    float3 color = (AmbientColor.xyz * baseColor) + lighting;
    color += EmissiveTexture.Sample(LinearSampler, input.Uv).rgb * EmissiveNits.xyz;

    // Simplified exponential height fog: density falls off with height above ground (Y=0).
    float viewDistance = length(CameraPositionWS.xyz - input.WorldPosition);
    float heightTerm = exp(-FogParams0.y * max(input.WorldPosition.y, 0.0));
    float fogFactor = saturate(1.0 - exp(-FogParams0.x * heightTerm * viewDistance));
    color = lerp(color, FogColor.xyz, fogFactor);
    color *= ExposureParams.x;

    // Keep the lighting result in HDR. StageRenderer composites this target through PostProcessPS,
    // where a thresholded spatial blur and ACES tone mapping are applied exactly once.
    return float4(color, 1.0);
}
