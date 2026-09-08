namespace CSharpGameEngine.ShaderArtifacts;

/// <summary>
/// Engine-owned presentation entries appended to every Stage translation unit before DXC.
/// They share the Stage resource layout and are part of its hashed ABI; runtime never compiles
/// this source. MaterialParams.z carries the presentation mode and EmissiveNits.w carries the
/// selection weight. The shipping Stage entries intentionally ignore both components.
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
