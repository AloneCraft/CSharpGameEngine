// Screen-space ambient occlusion (ADR-0069): Scalable Ambient Obscurance (McGuire et al. 2012)
// on a half-resolution camera depth prepass, followed by a separable depth-aware blur. The Stage
// program multiplies only indirect light (sky light, GI, flat ambient) by the result.
// Root signature (host-owned, StageAmbientOcclusion): b0 AoCB, t0-t1 table (prepass depth
// Texture2D<float>, blur source Texture2D<float>), u0 table (RWTexture2D<float> output).
// Positions are reconstructed in the renderer's camera-relative space from the inverse
// view-projection, so the pass works for any perspective camera matrix.
cbuffer AoCB : register(b0)
{
    row_major float4x4 InverseViewProjection;
    float4 ViewDepthRow;       // fourth column of the view-projection: clip w (linear depth) = dot(float4(p, 1), row)
    float4 CameraPosition;     // xyz camera-relative eye
    float4 AoParams;           // x = radius (m), y = bias (m), z = intensity, w = pixels per metre at unit depth
    uint4 Pass;                // x = 0 AO, 1 horizontal blur, 2 vertical blur; yz = texture size
};
Texture2D<float> Depth : register(t0);
Texture2D<float> BlurSource : register(t1);
RWTexture2D<float> Output : register(u0);

static const uint SampleCount = 12;
static const float SpiralTurns = 7.0;
static const int BlurRadius = 4;

float3 ViewPosition(int2 pixel)
{
    float depth = Depth.Load(int3(pixel, 0));
    float2 ndc = ((float2)pixel + 0.5) / (float2)Pass.yz * 2.0 - 1.0;
    ndc.y = -ndc.y;
    float4 position = mul(float4(ndc, depth, 1.0), InverseViewProjection);
    return position.xyz / position.w;
}

float LinearDepth(float3 position)
{
    return dot(float4(position, 1.0), ViewDepthRow);
}

float3 ReconstructNormal(int2 pixel, float3 center)
{
    int2 last = (int2)Pass.yz - 1;
    float3 right = ViewPosition(min(pixel + int2(1, 0), last)) - center;
    float3 left = center - ViewPosition(max(pixel - int2(1, 0), 0));
    float3 down = ViewPosition(min(pixel + int2(0, 1), last)) - center;
    float3 up = center - ViewPosition(max(pixel - int2(0, 1), 0));
    float3 dx = dot(right, right) < dot(left, left) ? right : left;
    float3 dy = dot(down, down) < dot(up, up) ? down : up;
    float3 n = normalize(cross(dy, dx));
    return dot(n, CameraPosition.xyz - center) < 0.0 ? -n : n;
}

float InterleavedGradientNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
}

float ComputeObscurance(int2 pixel)
{
    float rawDepth = Depth.Load(int3(pixel, 0));
    if (rawDepth >= 1.0)
    {
        return 1.0;
    }
    float3 center = ViewPosition(pixel);
    float3 normal = ReconstructNormal(pixel, center);
    float radius = AoParams.x;
    float radius2 = radius * radius;
    float screenRadius = AoParams.w * radius / max(LinearDepth(center), 1e-3);
    if (screenRadius < 1.0)
    {
        return 1.0;
    }
    float angle = InterleavedGradientNoise((float2)pixel) * 6.2831853;
    float sum = 0.0;
    for (uint i = 0; i < SampleCount; i++)
    {
        float alpha = ((float)i + 0.5) / (float)SampleCount;
        float theta = alpha * SpiralTurns * 6.2831853 + angle;
        float2 offset = float2(cos(theta), sin(theta)) * (alpha * screenRadius);
        int2 samplePixel = clamp(pixel + (int2)round(offset), int2(0, 0), (int2)Pass.yz - 1);
        float3 v = ViewPosition(samplePixel) - center;
        float vv = dot(v, v);
        float vn = dot(v, normal);
        float f = max(radius2 - vv, 0.0);
        sum += f * f * f * max((vn - AoParams.y) / (0.01 + vv), 0.0);
    }
    float intensityDivR6 = AoParams.z / (radius2 * radius2 * radius2);
    return max(0.0, 1.0 - sum * intensityDivR6 * (5.0 / (float)SampleCount));
}

float Blur(int2 pixel, int2 direction)
{
    float centerDepth = LinearDepth(ViewPosition(pixel));
    float total = 0.0;
    float weightSum = 0.0;
    for (int k = -BlurRadius; k <= BlurRadius; k++)
    {
        int2 samplePixel = clamp(pixel + direction * k, int2(0, 0), (int2)Pass.yz - 1);
        float sampleDepth = LinearDepth(ViewPosition(samplePixel));
        float spatial = exp(-(float)(k * k) / 8.0);
        float range = exp(-abs(sampleDepth - centerDepth) / (0.05 * centerDepth + 0.01) * 4.0);
        float weight = spatial * range;
        total += BlurSource.Load(int3(samplePixel, 0)) * weight;
        weightSum += weight;
    }
    return total / max(weightSum, 1e-5);
}

[numthreads(8, 8, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    int2 pixel = (int2)dispatchThreadId.xy;
    if (any(dispatchThreadId.xy >= Pass.yz))
    {
        return;
    }
    if (Pass.x == 0u)
    {
        Output[pixel] = ComputeObscurance(pixel);
    }
    else if (Pass.x == 1u)
    {
        Output[pixel] = Blur(pixel, int2(1, 0));
    }
    else
    {
        Output[pixel] = Blur(pixel, int2(0, 1));
    }
}
