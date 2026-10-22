namespace CSharpGameEngine.ShaderArtifacts;

/// <summary>
/// Engine-owned presentation entries appended to every Stage translation unit before DXC.
/// They share the Stage resource layout and are part of its hashed ABI; runtime never compiles
/// this source. MaterialParams.z carries the presentation mode and EmissiveNits.w carries the
/// selection weight. The shipping Stage entries intentionally ignore both components.
/// The radial entries (M0099, ADR-0076) wrap the standard pixel entries unchanged and add the
/// radial depth; FrameCB.DepthMapping0/1 carry their packet and ObjectCB.RadialRasterDepth each object's
/// conservative raster depth for the main pass, which the standard entries never read.
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

// log2(1 + x) for x >= 0 without the hardware log2, whose approximation error swamps small arguments and costs
// several ulps elsewhere (M0100, measured on an RTX 4060 Ti). ln(1 + x) = 2 atanh(s): below sqrt(2) - 1 with
// s = x / (2 + x); above it 1 + x = m * 2^e with m in (sqrt(1/2), sqrt(2)] and s = (m - 1) / (m + 1). |s| <= 0.1716,
// so the odd series to s^9 is exact to float precision. RadialDepthCurve.Log2OnePlus is the same expression.
float RadialLog2OnePlus(float x)
{
    float exponent = 0.0;
    float s;
    if (x < 0.41421356)
    {
        s = x / (2.0 + x);
    }
    else
    {
        uint bits = asuint(1.0 + x);
        exponent = float(int(bits >> 23) - 127);
        float m = asfloat((bits & 0x007FFFFFu) | 0x3F800000u);
        if (m > 1.41421356)
        {
            m *= 0.5;
            exponent += 1.0;
        }
        s = (m - 1.0) / (m + 1.0);
    }
    float s2 = s * s;
    float series = s * (2.0 + s2 * (0.6666667 + s2 * (0.4 + s2 * (0.2857143 + s2 * 0.2222222))));
    return exponent + series * 1.4426950;
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

// RadialVSMain rasterizes the depth a - b/w, which never exceeds the encoded depth, so the shader promises a depth at
// least the rasterized one and the hardware may still reject hidden fragments before running it.
struct RadialConservativePixelOutput
{
    float4 Color : SV_Target;
    float Depth : SV_DepthGreaterEqual;
};

// The radial main pass rasterizes its conservative depth without depth clipping (that depth reaches zero nearer than
// its tangent), so it clips with the standard projection's own near and far distances instead. Clip distances round a
// clipped vertex differently from the depth planes, so values interpolated on a clipped triangle may differ from the
// standard ones by one 8-bit level; unclipped triangles keep them exactly. The members mirror VertexOutput
// (ShaderGraphCompilerTests keeps them in step).
struct RadialVertexOutput
{
    float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float2 Uv : TEXCOORD1;
    float4 ShadowPosition : TEXCOORD2;
    float2 ClipDistance : SV_ClipDistance0;
};

// A pixel shader that declares SV_DepthGreaterEqual must read SV_Position with centroid, no-perspective interpolation;
// the other members mirror VertexOutput and are handed to the standard pixel entries unchanged. Without MSAA the
// centroid is the pixel centre, so SV_Position keeps its value.
struct RadialPixelInput
{
    noperspective centroid float4 Position : SV_Position;
    float3 WorldPosition : TEXCOORD0;
    float3 Normal : NORMAL;
    float4 Tangent : TANGENT;
    float2 Uv : TEXCOORD1;
    float4 ShadowPosition : TEXCOORD2;
};

VertexOutput ToVertexOutput(RadialPixelInput input)
{
    VertexOutput output;
    output.Position = input.Position;
    output.WorldPosition = input.WorldPosition;
    output.Normal = input.Normal;
    output.Tangent = input.Tangent;
    output.Uv = input.Uv;
    output.ShadowPosition = input.ShadowPosition;
    return output;
}

RadialVertexOutput RadialVSMain(VertexInput input)
{
    VertexOutput standard = VSMain(input);
    RadialVertexOutput output;
    // (a, b) = ObjectCB.RadialRasterDepth is tangent at this object's own view depth (M0100), so hidden fragments are
    // rejected early at every distance; X, Y and W are the standard ones.
    output.Position = float4(standard.Position.xy, RadialRasterDepth.x * standard.Position.w - RadialRasterDepth.y, standard.Position.w);
    output.WorldPosition = standard.WorldPosition;
    output.Normal = standard.Normal;
    output.Tangent = standard.Tangent;
    output.Uv = standard.Uv;
    output.ShadowPosition = standard.ShadowPosition;
    output.ClipDistance = float2(standard.Position.z, standard.Position.w - standard.Position.z);
    return output;
}

RadialConservativePixelOutput RadialPSMain(RadialPixelInput input)
{
    if (IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    RadialConservativePixelOutput output;
    output.Color = PSMain(ToVertexOutput(input));
    output.Depth = EncodeRadialDepth(input.WorldPosition - CameraPositionWS.xyz);
    return output;
}

RadialConservativePixelOutput PreviewRadialPSMain(RadialPixelInput input)
{
    if (IsBeyondRadialFarPlane(input.WorldPosition))
    {
        discard;
    }
    RadialConservativePixelOutput output;
    output.Color = PreviewPSMain(ToVertexOutput(input));
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
""";
}
