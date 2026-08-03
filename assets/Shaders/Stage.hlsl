// Minimal Lambertian-shaded, untextured mesh shader for the ECS-driven stage vertical slice
// (ADR-0016). Model matrix is per-object (ObjectCB, b1); view/projection is per-frame (FrameCB, b0).
// No texture/UV - StageRenderer only draws solid-colored procedural meshes.

cbuffer FrameCB : register(b0)
{
    row_major float4x4 ViewProjection;
};

cbuffer ObjectCB : register(b1)
{
    row_major float4x4 World;
    float4 BaseColorFactor;
};

struct VertexInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float3 Normal : NORMAL;
};

static const float3 LightDirectionRaw = float3(0.4, 0.7, 0.5);
static const float3 AmbientColor = float3(0.12, 0.12, 0.15);

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    float4 worldPosition = mul(float4(input.Position, 1.0), World);
    output.Position = mul(worldPosition, ViewProjection);
    output.Normal = mul(input.Normal, (float3x3)World);
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float3 n = normalize(input.Normal);
    float3 lightDirection = normalize(LightDirectionRaw);
    float ndotl = max(dot(n, lightDirection), 0.0);
    return float4(saturate(AmbientColor + BaseColorFactor.rgb * ndotl), 1.0);
}
