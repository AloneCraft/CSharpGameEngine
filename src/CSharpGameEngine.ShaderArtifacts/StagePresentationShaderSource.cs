namespace CSharpGameEngine.ShaderArtifacts;

/// <summary>
/// Engine-owned presentation entries appended to every Stage translation unit before DXC.
/// They share the Stage resource layout and are part of its hashed ABI; runtime never compiles
/// this source. MaterialParams.z carries the presentation mode and EmissiveNits.w carries the
/// selection weight. The shipping Stage entries intentionally ignore both components.
/// The radial entries (M0099, ADR-0076) wrap the standard pixel entries unchanged and add the
/// radial depth; FrameCB.DepthMapping0/1 carry their packet, which the standard entries never read.
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
    return PSMain(input);
}

float4 PreviewPSMain(VertexOutput input) : SV_Target
{
    float4 surface = PreviewSurfacePSMain(input);
    float selectionWeight = EmissiveNits.w;
    return float4(saturate(surface.rgb + selectionWeight * PreviewSelectionTint), surface.a);
}

float4 PreviewCleanPSMain(VertexOutput input) : SV_Target
{
    return PreviewSurfacePSMain(input);
}

static const float RadialMaximumDepth = 0.99999994;

float RadialLog2OnePlus(float x)
{
    float u = 1.0 + x;
    return u == 1.0 ? x * 1.4426950408889634 : log2(u) * (x / (u - 1.0));
}

float RadialSafeLength(float3 value)
{
    float largest = max(max(abs(value.x), abs(value.y)), abs(value.z));
    return largest > 0.0 ? largest * length(value / largest) : 0.0;
}

float RadialG(float distanceMeters)
{
    return RadialLog2OnePlus(distanceMeters / DepthMapping0.y);
}

float EncodeRadialDepth(float3 cameraRelativePosition)
{
    return min(RadialG(RadialSafeLength(cameraRelativePosition)) * DepthMapping0.z, RadialMaximumDepth);
}

bool IsBeyondRadialFarPlane(float3 worldPosition)
{
    return mul(float4(worldPosition, 1.0), ViewProjection).w > DepthMapping0.w;
}

struct RadialPixelOutput
{
    float4 Color : SV_Target;
    float Depth : SV_Depth;
};

RadialPixelOutput RadialPSMain(VertexOutput input)
{
    if (IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    RadialPixelOutput output;
    output.Color = PSMain(input);
    output.Depth = EncodeRadialDepth(input.WorldPosition - CameraPositionWS.xyz);
    return output;
}

RadialPixelOutput PreviewRadialPSMain(VertexOutput input)
{
    if (IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    RadialPixelOutput output;
    output.Color = PreviewPSMain(input);
    output.Depth = EncodeRadialDepth(input.WorldPosition - CameraPositionWS.xyz);
    return output;
}

RadialPixelOutput PreviewCleanRadialPSMain(VertexOutput input)
{
    if (IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    RadialPixelOutput output;
    output.Color = PreviewCleanPSMain(input);
    output.Depth = EncodeRadialDepth(input.WorldPosition - CameraPositionWS.xyz);
    return output;
}

VertexOutput RadialObserverVSMain(VertexInput input)
{
    VertexOutput output = VSMain(input);
    float3 fromCenter = output.WorldPosition - DepthMapping1.xyz;
    float distanceMeters = RadialSafeLength(fromCenter);
    float3 mapped = distanceMeters > 0.0 ? fromCenter * (DepthMapping0.y * RadialG(distanceMeters) / distanceMeters) : fromCenter;
    output.Position = mul(float4(mapped - (CameraPositionWS.xyz - DepthMapping1.xyz), 1.0), ViewProjection);
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
""";
}
