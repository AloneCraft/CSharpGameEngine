// Dynamic diffuse global illumination (ADR-0069), a rasterized DDGI-style probe volume. The
// renderer captures each probe as six 90-degree faces with the Stage program itself (so captured
// surfaces already include direct light, the sky light and the previous GI bounce); this compute
// artifact convolves one probe's capture into its octahedral irradiance and distance-moment tiles.
// Root signature (host-owned, StageGlobalIllumination): b0 GiCB, t0-t1 table (capture radiance
// and capture depth Texture2DArrays, six slices per probe), t2 root SRV sky light SH
// (StructuredBuffer<float4>), u0-u1 table (irradiance atlas RWTexture2D<float4>, distance-moment
// atlas RWTexture2D<float2>). Irradiance alpha is 1 for a captured probe and 0 after a clear.
cbuffer GiCB : register(b0)
{
    float4 FaceRight[6];
    float4 FaceUp[6];
    float4 FaceForward[6];
    float4 CaptureParams;   // x face resolution, y near, z far, w hysteresis (0 = replace)
    uint4 ProbeTile;        // xy irradiance tile origin (texels, including border), zw distance tile origin
    float4 SkyParams;       // x sky light enabled, y distance written for sky texels
    uint4 Control;          // x first capture array slice, y 1 = clear both atlases instead of convolving
};
Texture2DArray<float4> CaptureRadiance : register(t0);
Texture2DArray<float> CaptureDepth : register(t1);
StructuredBuffer<float4> SkyIrradianceSh : register(t2);
RWTexture2D<float4> IrradianceAtlas : register(u0);
RWTexture2D<float2> DistanceAtlas : register(u1);

static const float PI = 3.14159265359;
static const uint IrradianceTexels = 8;
static const uint DistanceTexels = 16;
static const uint CaptureSampleStride = 2;

// Octahedral mapping over the unit sphere (z is the octahedron's polar axis).
float2 SignNotZero(float2 v)
{
    return float2(v.x >= 0.0 ? 1.0 : -1.0, v.y >= 0.0 ? 1.0 : -1.0);
}

float3 OctahedralDecode(float2 e)
{
    float3 v = float3(e.x, e.y, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0.0)
    {
        v.xy = (1.0 - abs(v.yx)) * SignNotZero(v.xy);
    }
    return normalize(v);
}

// Maps a tile texel (border included) to the interior texel whose direction it stores. Border
// texels copy the mirrored edge so bilinear sampling across the octahedron seams stays continuous.
uint2 InteriorTexel(int2 texel, int size)
{
    int2 t = texel - 1;
    if (t.x < 0) { t.x = 0; t.y = size - 1 - t.y; }
    if (t.x > size - 1) { t.x = size - 1; t.y = size - 1 - t.y; }
    if (t.y < 0) { t.y = 0; t.x = size - 1 - t.x; }
    if (t.y > size - 1) { t.y = size - 1; t.x = size - 1 - t.x; }
    return uint2(clamp(t, 0, size - 1));
}

float3 TexelDirection(uint2 interior, uint size)
{
    float2 e = ((float2)interior + 0.5) / (float)size * 2.0 - 1.0;
    return OctahedralDecode(e);
}

// The SH buffer stores irradiance (each band already multiplied by the clamped-cosine factor
// A0 = pi, A1 = 2pi/3, A2 = pi/4). Dividing the bands back out recovers the band-limited sky
// radiance, so convolving it here reproduces the Stage program's SH irradiance exactly.
float3 EvaluateSkyRadiance(float3 n)
{
    float3 band0 = SkyIrradianceSh[0].rgb * 0.282095;
    float3 band1 = SkyIrradianceSh[1].rgb * (0.488603 * n.y)
        + SkyIrradianceSh[2].rgb * (0.488603 * n.z)
        + SkyIrradianceSh[3].rgb * (0.488603 * n.x);
    float3 band2 = SkyIrradianceSh[4].rgb * (1.092548 * n.x * n.y)
        + SkyIrradianceSh[5].rgb * (1.092548 * n.y * n.z)
        + SkyIrradianceSh[6].rgb * (0.315392 * (3.0 * n.z * n.z - 1.0))
        + SkyIrradianceSh[7].rgb * (1.092548 * n.x * n.z)
        + SkyIrradianceSh[8].rgb * (0.546274 * (n.x * n.x - n.y * n.y));
    return band0 / PI + band1 * (1.5 / PI) + band2 * (4.0 / PI);
}

struct CaptureSample
{
    float3 Direction;
    float SolidAngle;
    float3 Radiance;
    float Distance;
};

CaptureSample ReadCapture(uint face, uint2 texel)
{
    float resolution = CaptureParams.x;
    float2 ndc = ((float2)texel + 0.5) / resolution * 2.0 - 1.0;
    ndc.y = -ndc.y;
    float3 ray = FaceForward[face].xyz + FaceRight[face].xyz * ndc.x + FaceUp[face].xyz * ndc.y;
    float rayLengthSquared = dot(ray, ray);
    float rayLength = sqrt(rayLengthSquared);
    CaptureSample s;
    s.Direction = ray / rayLength;
    // Solid angle of a cube-map texel of side 2/resolution at unit distance.
    float texelArea = 4.0 / (resolution * resolution);
    s.SolidAngle = texelArea / (rayLengthSquared * rayLength);
    uint slice = Control.x + face;
    float depth = CaptureDepth.Load(int4(texel, slice, 0));
    if (depth >= 1.0)
    {
        // Not clamped per sample: the band-limited radiance rings negative toward a dark ground,
        // and only the unclamped sum reproduces the Stage SH irradiance in open sky.
        s.Radiance = SkyParams.x > 0.5 ? EvaluateSkyRadiance(s.Direction) : float3(0.0, 0.0, 0.0);
        s.Distance = SkyParams.y;
        return s;
    }
    float nearZ = CaptureParams.y;
    float farZ = CaptureParams.z;
    float viewZ = nearZ * farZ / (farZ - depth * (farZ - nearZ));
    s.Radiance = CaptureRadiance.Load(int4(texel, slice, 0)).rgb;
    s.Distance = min(viewZ * rayLength, SkyParams.y);
    return s;
}

[numthreads(8, 8, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID)
{
    uint2 tileTexel = dispatchThreadId.xy;
    if (Control.y != 0u)
    {
        uint irradianceWidth;
        uint irradianceHeight;
        uint distanceWidth;
        uint distanceHeight;
        IrradianceAtlas.GetDimensions(irradianceWidth, irradianceHeight);
        DistanceAtlas.GetDimensions(distanceWidth, distanceHeight);
        if (tileTexel.x < irradianceWidth && tileTexel.y < irradianceHeight)
        {
            IrradianceAtlas[tileTexel] = float4(0.0, 0.0, 0.0, 0.0);
        }
        if (tileTexel.x < distanceWidth && tileTexel.y < distanceHeight)
        {
            DistanceAtlas[tileTexel] = float2(0.0, 0.0);
        }
        return;
    }
    uint resolution = (uint)CaptureParams.x;
    float hysteresis = CaptureParams.w;
    if (tileTexel.x < IrradianceTexels + 2 && tileTexel.y < IrradianceTexels + 2)
    {
        float3 normal = TexelDirection(InteriorTexel((int2)tileTexel, IrradianceTexels), IrradianceTexels);
        float3 irradiance = 0.0;
        for (uint face = 0; face < 6; face++)
        {
            for (uint y = 0; y < resolution; y += CaptureSampleStride)
            {
                for (uint x = 0; x < resolution; x += CaptureSampleStride)
                {
                    CaptureSample s = ReadCapture(face, uint2(x, y));
                    irradiance += s.Radiance * max(dot(normal, s.Direction), 0.0) * s.SolidAngle * (float)(CaptureSampleStride * CaptureSampleStride);
                }
            }
        }
        irradiance = max(irradiance, 0.0);
        uint2 atlasTexel = ProbeTile.xy + tileTexel;
        float4 previous = IrradianceAtlas[atlasTexel];
        IrradianceAtlas[atlasTexel] = float4(lerp(irradiance, previous.rgb, hysteresis), 1.0);
    }
    if (tileTexel.x < DistanceTexels + 2 && tileTexel.y < DistanceTexels + 2)
    {
        float3 direction = TexelDirection(InteriorTexel((int2)tileTexel, DistanceTexels), DistanceTexels);
        float2 moments = 0.0;
        float weightSum = 0.0;
        for (uint face2 = 0; face2 < 6; face2++)
        {
            for (uint y2 = 0; y2 < resolution; y2 += CaptureSampleStride)
            {
                for (uint x2 = 0; x2 < resolution; x2 += CaptureSampleStride)
                {
                    CaptureSample s = ReadCapture(face2, uint2(x2, y2));
                    float weight = pow(max(dot(direction, s.Direction), 0.0), 50.0) * s.SolidAngle;
                    moments += float2(s.Distance, s.Distance * s.Distance) * weight;
                    weightSum += weight;
                }
            }
        }
        moments /= max(weightSum, 1e-8);
        uint2 atlasTexel2 = ProbeTile.zw + tileTexel;
        float2 previousMoments = DistanceAtlas[atlasTexel2];
        DistanceAtlas[atlasTexel2] = lerp(moments, previousMoments, hysteresis);
    }
}
