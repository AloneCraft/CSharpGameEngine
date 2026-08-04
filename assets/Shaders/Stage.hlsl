// Textured PBR-lite shader for the ECS-driven stage (ADR-0016 vertical slice extended by
// ADR-0018): GGX Cook-Torrance specular + Lambert diffuse, tangent-space normal mapping, a single
// data-driven directional light + flat ambient term, and a simplified exponential height fog.
// Model matrix + material factors are per-object (ObjectCB, b1); view/projection, camera position,
// light, ambient and fog are per-frame (FrameCB, b0). Textures are shared per-material via a
// 3-descriptor SRV table (t0=BaseColor, t1=Normal, t2=MetallicRoughness); a material baked to a
// flat factor (no spatial variation) binds a neutral 1x1 fallback texture in that slot.

cbuffer FrameCB : register(b0)
{
    row_major float4x4 ViewProjection;
    float4 CameraPositionWS;           // xyz used
    float4 LightDirectionTowardLight;  // xyz used, unit length
    float4 LightColorTimesIntensity;   // xyz used
    float4 AmbientColor;               // xyz used
    float4 FogParams0;                 // x = density, y = height falloff
    float4 FogColor;                   // xyz used
};

cbuffer ObjectCB : register(b1)
{
    row_major float4x4 World;
    float4 BaseColorFactor;
    float4 MaterialParams; // x = roughness factor, y = metallic factor
};

Texture2D BaseColorTexture : register(t0);
Texture2D NormalTexture : register(t1);
Texture2D MetallicRoughnessTexture : register(t2);
SamplerState LinearSampler : register(s0);

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
    return output;
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
    float3 l = normalize(LightDirectionTowardLight.xyz);
    float3 h = normalize(v + l);

    float nDotL = saturate(dot(n, l));
    float nDotV = saturate(dot(n, v)) + 1e-4;
    float nDotH = saturate(dot(n, h));
    float vDotH = saturate(dot(v, h));

    float3 f0 = lerp(float3(0.04, 0.04, 0.04), baseColor, metallic);
    float3 fresnel = FresnelSchlick(vDotH, f0);

    float d = DistributionGgx(nDotH, roughness);
    float vis = VisibilitySmithGgxCorrelated(nDotL, nDotV, roughness);
    float3 specular = d * vis * fresnel;

    float3 kd = (1.0 - fresnel) * (1.0 - metallic);
    float3 diffuse = kd * baseColor / PI;

    float3 lighting = (diffuse + specular) * nDotL * LightColorTimesIntensity.xyz;
    float3 color = (AmbientColor.xyz * baseColor) + lighting;

    // Simplified exponential height fog: density falls off with height above ground (Y=0).
    float viewDistance = length(CameraPositionWS.xyz - input.WorldPosition);
    float heightTerm = exp(-FogParams0.y * max(input.WorldPosition.y, 0.0));
    float fogFactor = saturate(1.0 - exp(-FogParams0.x * heightTerm * viewDistance));
    color = lerp(color, FogColor.xyz, fogFactor);

    return float4(saturate(color), 1.0);
}
