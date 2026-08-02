// Minimal Lambertian-shaded, base-color-textured mesh shader for the glTF model viewer vertical
// slice. Compiled with DXC at Shader Model 6.0 (see CSharpGameEngine.Compiler.Hlsl).
// No metallic/roughness/normal/emissive/occlusion maps yet - base color only.

cbuffer TransformCB : register(b0)
{
    row_major float4x4 MVP;
    float4 BaseColorFactor;
};

Texture2D BaseColorTexture : register(t0);
SamplerState BaseColorSampler : register(s0);

struct VertexInput
{
    float3 Position : POSITION;
    float3 Normal : NORMAL;
    float2 Uv : TEXCOORD0;
};

struct VertexOutput
{
    float4 Position : SV_Position;
    float3 Normal : NORMAL;
    float2 Uv : TEXCOORD0;
};

// Left as an un-normalized literal (no function-call initializer) and normalized inline in
// PSMain: a static const global initialized via a function call (e.g. normalize(...)) made DXC's
// debug-info generation emit a spurious "!dbg attachment points at wrong subprogram" diagnostic
// under -Zi. Functionally harmless either way, but avoided here for a clean compiler output.
static const float3 LightDirectionRaw = float3(0.4, 0.7, 0.5);
static const float3 AmbientColor = float3(0.12, 0.12, 0.15);

VertexOutput VSMain(VertexInput input)
{
    VertexOutput output;
    output.Position = mul(float4(input.Position, 1.0), MVP);
    output.Normal = input.Normal; // model transform is identity this milestone
    output.Uv = input.Uv;
    return output;
}

float4 PSMain(VertexOutput input) : SV_Target
{
    float4 sampled = BaseColorTexture.Sample(BaseColorSampler, input.Uv);
    float3 baseColor = sampled.rgb * BaseColorFactor.rgb;

    float3 n = normalize(input.Normal);
    float3 lightDirection = normalize(LightDirectionRaw);
    float ndotl = max(dot(n, lightDirection), 0.0);
    return float4(saturate(AmbientColor + baseColor * ndotl), 1.0);
}
