namespace CSharpGameEngine.ShaderArtifacts;

/// <summary>
/// Engine-owned presentation entries appended to every Stage translation unit before DXC.
/// They share the Stage resource layout and are part of its hashed ABI; runtime never compiles
/// this source. MaterialParams.z carries the presentation mode and EmissiveNits.w carries the
/// selection weight. The shipping Stage entries intentionally ignore both components.
/// The Stage graph owns view depth and its single vertex/pixel interface (ADR-0084). During migration the legacy
/// radial entry names delegate directly to those entries; they no longer transform vertices or add depth.
/// The Lumen entries (M0111) write the surface prepass and the surface cache cards from the same material inputs as PSMain.
/// </summary>
public static class StagePresentationShaderSource {
    public const string Source="""

struct OverlayVertexInput
{
    float3 Position : POSITION;
    float4 Color : COLOR;
};

struct OverlayVertexOutput
{
    float4 Position : SV_Position;
    float4 Color : COLOR;
};

static const float3 PreviewSelectionTint = float3(0.6, 0.35, 0.05);

float4 PreviewSurfacePSMain(VertexOutput input)
{
    float renderMode = MaterialParams.z;
    if (renderMode > 0.5 && renderMode < 1.5)
    {
        float3 baseColor = BaseColorTexture.Sample(LinearSampler, input.Uv).rgb * BaseColorFactor.rgb;
        return float4(saturate(baseColor), 1.0);
    }
    if (renderMode >= 1.5 && renderMode < 2.5)
    {
        float3 vertexNormal = normalize(input.Normal);
        float3 tangent = normalize(input.Tangent.xyz - vertexNormal * dot(vertexNormal, input.Tangent.xyz));
        float3 bitangent = cross(vertexNormal, tangent) * input.Tangent.w;
        float3x3 tbn = float3x3(tangent, bitangent, vertexNormal);
        float3 normalSampleTs = normalize(NormalTexture.Sample(LinearSampler, input.Uv).rgb * 2.0 - 1.0);
        float3 normalWs = normalize(mul(normalSampleTs, tbn));
        return float4(saturate(normalWs * 0.5 + 0.5), 1.0);
    }
    return PSMain(input).Color;
}

StagePixelOutput PreviewPSMain(VertexOutput input)
{
    if (DepthMapping0.x == 1.0 && IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    float4 surface = PreviewSurfacePSMain(input);
    float selectionWeight = EmissiveNits.w;
    return CompleteStageSurface(input, float4(saturate(surface.rgb + selectionWeight * PreviewSelectionTint), surface.a));
}

StagePixelOutput PreviewCleanPSMain(VertexOutput input)
{
    if (DepthMapping0.x == 1.0 && IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    return CompleteStageSurface(input, PreviewSurfacePSMain(input));
}

// Transitional ABI names: the Stage graph now performs the depth work itself.
VertexOutput RadialVSMain(VertexInput input)
{
    return VSMain(input);
}

StagePixelOutput RadialPSMain(VertexOutput input)
{
    return PSMain(input);
}

StagePixelOutput PreviewRadialPSMain(VertexOutput input)
{
    return PreviewPSMain(input);
}

StagePixelOutput PreviewCleanRadialPSMain(VertexOutput input)
{
    return PreviewCleanPSMain(input);
}

VertexOutput RadialObserverVSMain(VertexInput input)
{
    VertexOutput output = VSMain(input);
    float3 fromCenter = output.WorldPosition - DepthMapping1.xyz;
    float distanceMeters = RadialSafeLength(fromCenter);
    float3 mapped = distanceMeters > 0.0 ? fromCenter * (DepthMapping0.y * RadialG(distanceMeters) / distanceMeters) : fromCenter;
    output.DepthPosition = mapped - (CameraPositionWS.xyz - DepthMapping1.xyz);
    float4 clip = mul(float4(output.DepthPosition, 1.0), ViewProjection);
    output.Position = float4(clip.xy, 0.0, clip.w);
    output.ClipDistance = float2(clip.z, clip.w - clip.z);
    return output;
}

OverlayVertexOutput OverlayVSMain(OverlayVertexInput input)
{
    OverlayVertexOutput output;
    output.Position = mul(float4(input.Position, 1.0), ViewProjection);
    output.Color = input.Color;
    return output;
}

float4 OverlayPSMain(OverlayVertexOutput input) : SV_Target
{
    return input.Color;
}

struct ScreenVertexInput
{
    float2 Position : POSITION;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR;
};

struct ScreenVertexOutput
{
    float4 Position : SV_Position;
    float2 Uv : TEXCOORD0;
    float4 Color : COLOR;
};

// The screen layer (M0104): the game's UI over the tone-mapped frame. It uses the post-process root signature:
// t0 is the batch's texture, s0 a linear clamp sampler and b0 four root constants (x: 0 image, 1 distance field).
Texture2D ScreenTexture : register(t0);
SamplerState ScreenSampler : register(s0);
cbuffer ScreenCB : register(b0)
{
    float4 ScreenParams;
}

// The vertex positions arrive already in clip space: the renderer converts the layer's pixels when it receives them.
ScreenVertexOutput ScreenVSMain(ScreenVertexInput input)
{
    ScreenVertexOutput output;
    output.Position = float4(input.Position, 0.0, 1.0);
    output.Uv = input.Uv;
    output.Color = input.Color;
    return output;
}

float4 ScreenPSMain(ScreenVertexOutput input) : SV_Target
{
    float4 texel = ScreenTexture.Sample(ScreenSampler, input.Uv);
    if (ScreenParams.x > 0.5)
    {
        // Distance field: 0.5 is the outline. One pixel of the screen is fwidth(distance) of distance, so the edge is one pixel wide.
        float distance = texel.a;
        float edgeWidth = max(fwidth(distance), 1.0 / 255.0);
        float coverage = smoothstep(0.5 - edgeWidth, 0.5 + edgeWidth, distance);
        return float4(input.Color.rgb, input.Color.a * coverage);
    }
    return texel * input.Color;
}

// ADR-0078/M0111: the Lumen-like lighting's entries. Both draw with VSMain and the Stage root signature.
float3 LumenShadingNormal(VertexOutput input)
{
    float3 vertexNormal = normalize(input.Normal);
    float3 tangent = normalize(input.Tangent.xyz - vertexNormal * dot(vertexNormal, input.Tangent.xyz));
    float3 bitangent = cross(vertexNormal, tangent) * input.Tangent.w;
    float3x3 tbn = float3x3(tangent, bitangent, vertexNormal);
    float3 normalSampleTs = normalize(NormalTexture.Sample(LinearSampler, input.Uv).rgb * 2.0 - 1.0);
    return normalize(mul(normalSampleTs, tbn));
}

struct LumenSurfaceOutput
{
    float4 NormalRoughness : SV_Target0;
    float Distance : SV_Target1;
    float Depth : SV_Depth;
};

// The surface prepass the final gather and the reflections read: the shading normal and roughness PSMain uses and the distance from the camera
// (which PSMain compares with its own to know it shades the same surface).
LumenSurfaceOutput LumenSurfacePSMain(VertexOutput input)
{
    if (DepthMapping0.x == 1.0 && IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    float roughness = max(saturate(MetallicRoughnessTexture.Sample(LinearSampler, input.Uv).g * MaterialParams.x), 0.045);
    LumenSurfaceOutput output;
    output.NormalRoughness = float4(LumenShadingNormal(input), roughness);
    output.Distance = RadialSafeLength(input.WorldPosition - CameraPositionWS.xyz);
    output.Depth = DepthMapping0.x == 1.0 ? EncodeRadialDepth(input.WorldPosition - CameraPositionWS.xyz) : input.Position.z;
    return output;
}

struct LumenCardOutput
{
    float4 Albedo : SV_Target0;
    float4 Normal : SV_Target1;
    float4 Emissive : SV_Target2;
};

// The surface cache capture: one mesh in its own space (the host gives it an identity World) through a card's orthographic projection. The
// albedo is the diffuse colour (a metal has none), the normal is in the mesh's space and the emission leaves out the object's run-time scale,
// which the card lighting applies.
LumenCardOutput LumenCardPSMain(VertexOutput input)
{
    float3 baseColor = BaseColorTexture.Sample(LinearSampler, input.Uv).rgb * BaseColorFactor.rgb;
    float metallic = saturate(MetallicRoughnessTexture.Sample(LinearSampler, input.Uv).b * MaterialParams.y);
    LumenCardOutput output;
    output.Albedo = float4(baseColor * (1.0 - metallic), 1.0);
    output.Normal = float4(LumenShadingNormal(input) * 0.5 + 0.5, 1.0);
    output.Emissive = float4(EmissiveTexture.Sample(LinearSampler, input.Uv).rgb * EmissiveNits.xyz, 1.0);
    return output;
}
""";
}
