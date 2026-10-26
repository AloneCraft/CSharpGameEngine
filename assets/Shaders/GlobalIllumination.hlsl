// 動的 diffuse global illumination（ADR-0069）。rasterize 方式の DDGI 風 probe volume です。
// renderer は Stage program 自身で各 probe を90度の6面として capture します（そのため capture 済みの
// surface には direct light、sky light、前回の GI bounce がすでに含まれます）。この compute
// artifact は1つの probe の capture を octahedral irradiance と distance-moment tile に畳み込みます。
// Root signature（host が所有する StageGlobalIllumination）：b0 GiCB、t0-t1 table（capture radiance
// と capture depth の Texture2DArray。probe ごとに6 slice）、t2 root SRV の sky light SH
// （StructuredBuffer<float4>）、u0-u1 table（irradiance atlas RWTexture2D<float4>、distance-moment
// atlas RWTexture2D<float2>）。Irradiance alpha は capture 済みの probe で1、clear 後は0です。
cbuffer GiCB : register(b0)
{
    float4 FaceRight[6];
    float4 FaceUp[6];
    float4 FaceForward[6];
    float4 CaptureParams;   // x = face resolution、yz = 予約領域、w = hysteresis（0 = 置換）
    uint4 ProbeTile;        // xy = irradiance tile の原点（border を含む texel）、zw = distance tile の原点
    float4 SkyParams;       // x = sky light の有効状態、y = sky texel の距離を書き込むか
    uint4 Control;          // x = capture array の最初の slice、y = 1 の場合は畳み込みの代わりに両 atlas を clear
};
Texture2DArray<float4> CaptureRadiance : register(t0);
Texture2DArray<float> CaptureDistance : register(t1);
StructuredBuffer<float4> SkyIrradianceSh : register(t2);
RWTexture2D<float4> IrradianceAtlas : register(u0);
RWTexture2D<float2> DistanceAtlas : register(u1);

static const float PI = 3.14159265359;
static const uint IrradianceTexels = 8;
static const uint DistanceTexels = 16;
static const uint CaptureSampleStride = 2;

// 単位球上の octahedral mapping（z が octahedron の極軸）。
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

// tile texel（border を含む）を、対応する方向を保持する内部 texel に対応付けます。border
// texel は反転した edge のコピーであり、octahedron の seam をまたぐ bilinear sampling を連続させます。
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

// SH buffer は irradiance を保持します（各 band には clamped-cosine factor
// A0 = pi、A1 = 2pi/3、A2 = pi/4 を乗算済み）。band の値を元に戻すと band-limited sky
// radiance が得られ、ここで畳み込むことで Stage program と完全に同じ SH irradiance を再現します。
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
    // 単位距離において、辺の長さが2/resolution の cube-map texel が占める立体角。
    float texelArea = 4.0 / (resolution * resolution);
    s.SolidAngle = texelArea / (rayLengthSquared * rayLength);
    uint slice = Control.x + face;
    float distance = CaptureDistance.Load(int4(texel, slice, 0));
    if (distance <= 0.0)
    {
        // sample ごとには clamp しません。band-limited radiance は暗い地面方向で負になるため、
        // 開けた空で Stage SH irradiance を再現できるのは、clamp していない合計値だけです。
        s.Radiance = SkyParams.x > 0.5 ? EvaluateSkyRadiance(s.Direction) : float3(0.0, 0.0, 0.0);
        s.Distance = SkyParams.y;
        return s;
    }
    s.Radiance = CaptureRadiance.Load(int4(texel, slice, 0)).rgb;
    s.Distance = min(distance, SkyParams.y);
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
