// Textured PBR-lite shader for the ECS-driven stage (ADR-0016 vertical slice extended by
// ADR-0018, point/spot lights added by ADR-0037): GGX Cook-Torrance specular + Lambert diffuse,
// tangent-space normal mapping, a single data-driven directional light + a fixed-capacity set of
// point/spot lights + flat ambient term, and a simplified exponential height fog.
// Model matrix + material factors are per-object (ObjectCB, b1); view/projection, camera position,
// lights, ambient and fog are per-frame (FrameCB, b0). Textures are shared per-material via a
// 4-descriptor SRV table (t0=BaseColor, t1=Normal, t2=MetallicRoughness, t3=DirectionalShadow);
// a material baked to a
// flat factor (no spatial variation) binds a neutral 1x1 fallback texture in that slot.

// Fixed capacities (no light culling/clustering in this vertical slice - see ADR-0037): the frame
// constant buffer always reserves room for the maximum, and FrameCB.LightCounts says how many of
// each are actually populated this frame. Must match StageRenderer.MaxPointLights/MaxSpotLights.
#define MAX_POINT_LIGHTS 4
#define MAX_SPOT_LIGHTS 4

struct PointLightData
{
    float4 PositionAndRange;      // xyz = world position, w = range (distance at which attenuation reaches zero)
    float4 ColorTimesIntensity;   // xyz used
};

struct SpotLightData
{
    float4 PositionAndRange;      // xyz = world position, w = range
    float4 DirectionAndInnerCos;  // xyz = unit direction the cone points toward, w = cos(inner half-angle)
    float4 ColorAndOuterCos;      // xyz = color * intensity, w = cos(outer half-angle)
};

cbuffer FrameCB : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 CameraPositionWS;           // xyz used
    float4 LightDirectionTowardLight;  // xyz used, unit length
    float4 LightColorTimesIntensity;   // xyz used
    float4 AmbientColor;               // xyz used
    float4 FogParams0;                 // x = density, y = height falloff
    float4 FogColor;                   // xyz used
    int4 LightCounts;                  // x = active point lights, y = active spot lights
    PointLightData PointLights[MAX_POINT_LIGHTS];
    SpotLightData SpotLights[MAX_SPOT_LIGHTS];
    row_major float4x4 ShadowViewProjection;
};

cbuffer ObjectCB : register(b1)
{
    row_major float4x4 World;
    float4 BaseColorFactor;
    float4 MaterialParams; // x = roughness factor, y = metallic factor
    float4 EmissiveFactor; // xyz = HDR emissive factor
};

Texture2D BaseColorTexture : register(t0);
Texture2D NormalTexture : register(t1);
Texture2D MetallicRoughnessTexture : register(t2);
Texture2D ShadowMap : register(t3);
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

float3 ComputePointLightContribution(PointLightData light, float3 worldPosition, float3 n, float3 v, float3 baseColor, float roughness, float metallic, float3 f0)
{
    float3 toLight = light.PositionAndRange.xyz - worldPosition;
    float distance = length(toLight);
    float range = light.PositionAndRange.w;

    if (distance >= range)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 l = toLight / max(distance, 1e-5);
    float attenuation = DistanceAttenuation(distance, range);

    return EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0) * light.ColorTimesIntensity.xyz * attenuation;
}

float3 ComputeSpotLightContribution(SpotLightData light, float3 worldPosition, float3 n, float3 v, float3 baseColor, float roughness, float metallic, float3 f0)
{
    float3 toLight = light.PositionAndRange.xyz - worldPosition;
    float distance = length(toLight);
    float range = light.PositionAndRange.w;

    if (distance >= range)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float3 l = toLight / max(distance, 1e-5);

    // The cone axis points AWAY from the light (toward the scene), so the surface is inside the
    // cone when the direction FROM the light TO the surface (-l) aligns with that axis.
    float3 coneAxis = light.DirectionAndInnerCos.xyz;
    float innerCos = light.DirectionAndInnerCos.w;
    float outerCos = light.ColorAndOuterCos.w;
    float cosAngle = dot(-l, coneAxis);
    float coneAttenuation = saturate((cosAngle - outerCos) / max(innerCos - outerCos, 1e-4));
    coneAttenuation *= coneAttenuation;

    if (coneAttenuation <= 0.0)
    {
        return float3(0.0, 0.0, 0.0);
    }

    float distanceAttenuation = DistanceAttenuation(distance, range);

    return EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0) * light.ColorAndOuterCos.xyz * distanceAttenuation * coneAttenuation;
}

float ComputeDirectionalShadow(float4 shadowPosition)
{
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 uv = (projected.xy * float2(0.5, -0.5)) + 0.5;
    if (any(uv <= 0.0) || any(uv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }

    float bias = 0.0015;
    float2 texel = 1.0 / 1024.0;
    float visibility = 0.0;
    [unroll]
    for (int x = -1; x <= 1; x++)
    {
        [unroll]
        for (int y = -1; y <= 1; y++)
        {
            visibility += ShadowMap.SampleCmpLevelZero(ShadowSampler, uv + float2(x, y) * texel, projected.z - bias);
        }
    }
    return visibility / 9.0;
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

    float3 l = normalize(LightDirectionTowardLight.xyz);
    float shadow = ComputeDirectionalShadow(input.ShadowPosition);
    float3 lighting = EvaluateLitSurface(n, v, l, baseColor, roughness, metallic, f0) * LightColorTimesIntensity.xyz * shadow;

    int numPointLights = LightCounts.x;
    for (int i = 0; i < numPointLights; i++)
    {
        lighting += ComputePointLightContribution(PointLights[i], input.WorldPosition, n, v, baseColor, roughness, metallic, f0);
    }

    int numSpotLights = LightCounts.y;
    for (int j = 0; j < numSpotLights; j++)
    {
        lighting += ComputeSpotLightContribution(SpotLights[j], input.WorldPosition, n, v, baseColor, roughness, metallic, f0);
    }

    float3 color = (AmbientColor.xyz * baseColor) + lighting;
    color += EmissiveFactor.xyz;

    // Simplified exponential height fog: density falls off with height above ground (Y=0).
    float viewDistance = length(CameraPositionWS.xyz - input.WorldPosition);
    float heightTerm = exp(-FogParams0.y * max(input.WorldPosition.y, 0.0));
    float fogFactor = saturate(1.0 - exp(-FogParams0.x * heightTerm * viewDistance));
    color = lerp(color, FogColor.xyz, fogFactor);

    // Keep the lighting result in HDR. StageRenderer composites this target through PostProcessPS,
    // where a thresholded spatial blur and ACES tone mapping are applied exactly once.
    return float4(color, 1.0);
}
