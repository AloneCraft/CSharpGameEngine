// Physically based sky atmosphere (ADR-0069), after Hillaire 2020 "A Scalable and Production Ready
// Sky and Atmosphere Rendering Technique". One compute artifact owns every atmosphere pass; the
// host selects the pass through AtmosphereCB.Pass.x so the shared medium/LUT code exists once.
// Distances inside the atmosphere are kilometres; luminance values in the LUTs are per unit
// top-of-atmosphere sun illuminance and are scaled by SunIlluminance when they leave this file.
cbuffer AtmosphereCB : register(b0)
{
    float4 RayleighScattering;   // rgb 1/km, w exponential scale height km
    float4 MieScattering;        // rgb 1/km, w exponential scale height km
    float4 MieAbsorption;        // rgb 1/km, w phase anisotropy g
    float4 OzoneAbsorption;      // rgb 1/km, w tent tip altitude km
    float4 Planet;               // x bottom radius km, y top radius km, z ozone tent width km, w multi-scattering factor
    float4 GroundAlbedo;         // rgb linear albedo, w camera distance from planet centre km
    float4 SunDirection;         // xyz unit vector toward the sun (engine world), w sun disk angular radius rad
    float4 SunIlluminance;       // rgb top-of-atmosphere illuminance lux, w sky light intensity multiplier
    float4 LowerHemisphere;      // rgb lower hemisphere radiance for the sky light, w exposure scale
    float4 FogParams;            // x ray origin terms, y falloff per metre, z minimum transmittance, w fog enabled
    float4 FogColor;             // rgb inscattering luminance, w sky-atmosphere ambient contribution scale
    row_major float4x4 InverseViewProjection; // camera-relative clip -> world
    uint4 Pass;                  // x pass, y output width, z output height, w 1 = exposure from ExposureState (histogram auto exposure)
    // ADR-0078/M0110 volumetric clouds (Hillaire 2016 "Physically Based Sky, Atmosphere and Cloud Rendering in Frostbite").
    float4 CloudLayer;           // x layer bottom radius km, y layer top radius km, z tracing start max km, w tracing max km
    float4 CloudShape;           // x extinction per km, y coverage, z layout scale km, w stop-tracing transmittance
    float4 CloudAlbedo;          // rgb albedo, w sky light bottom occlusion
    float4 CloudPhase;           // x g, y g2, z blend, w 1 = clouds over the sky (and in the sky light), 0 = no clouds
    float4 CloudMultiScattering; // x contribution, y occlusion, z eccentricity, w view samples per texel
    float4 CloudPosition;        // xy camera over the planet km (R, F), zw wind offset km (R, F)
    uint4 CloudUpdate;           // x phase 0-15, y 1 = every texel, z panorama width, w panorama height
    float4 SkySphere;            // xyz centre (render space m), w radius m (0 = no sky sphere)
    float4 SkySphereDepth;       // x depth mapping (0 standard, 1 radial), y radial transition T m, z G = log2(1 + rMax / T)
    float4 CameraPosition;       // xyz camera (render space m)
};
Texture2D<float4> TransmittanceLut : register(t0);
Texture2D<float4> MultiScatteringLut : register(t1);
Texture2D<float4> SkyViewLut : register(t2);
Texture2D<float> SceneDepth : register(t3);
Texture2D<float4> CloudPanorama : register(t5);
Texture3D<float> CloudNoise : register(t6);
RWTexture2D<float4> OutputTexture : register(u0);
RWStructuredBuffer<float4> ShOutput : register(u1);
RWTexture3D<float> CloudNoiseOutput : register(u2);
StructuredBuffer<float4> ExposureState : register(t4);
SamplerState LinearClamp : register(s0);
SamplerState LinearWrap : register(s1);

static const float PI = 3.14159265359;
static const uint PassTransmittance = 0;
static const uint PassMultiScattering = 1;
static const uint PassSkyView = 2;
static const uint PassSkyLightSh = 3;
static const uint PassSkyBackground = 4;
static const uint PassCloudNoise = 5;
static const uint PassCloudPanorama = 6;
static const uint CloudNoiseSize = 32;
static const float2 TransmittanceLutSize = float2(256.0, 64.0);
static const float2 MultiScatteringLutSize = float2(32.0, 32.0);
static const float2 SkyViewLutSize = float2(192.0, 108.0);

float BottomRadius() { return Planet.x; }
float TopRadius() { return Planet.y; }

struct MediumSample
{
    float3 Scattering;
    float3 ScatteringRayleigh;
    float3 ScatteringMie;
    float3 Extinction;
};

MediumSample SampleMedium(float3 worldPos)
{
    float height = max(length(worldPos) - BottomRadius(), 0.0);
    float rayleighDensity = exp(-height / RayleighScattering.w);
    float mieDensity = exp(-height / MieScattering.w);
    float ozoneDensity = max(0.0, 1.0 - abs(height - OzoneAbsorption.w) / (Planet.z * 0.5));
    MediumSample s;
    s.ScatteringRayleigh = RayleighScattering.rgb * rayleighDensity;
    s.ScatteringMie = MieScattering.rgb * mieDensity;
    s.Scattering = s.ScatteringRayleigh + s.ScatteringMie;
    s.Extinction = s.Scattering + MieAbsorption.rgb * mieDensity + OzoneAbsorption.rgb * ozoneDensity;
    return s;
}

// Returns the nearest positive intersection distance with a sphere centred at the origin, or -1.
float RaySphereIntersectNearest(float3 origin, float3 direction, float radius)
{
    float b = dot(origin, direction);
    float c = dot(origin, origin) - radius * radius;
    float discriminant = b * b - c;
    if (discriminant < 0.0)
    {
        return -1.0;
    }
    float root = sqrt(discriminant);
    float t0 = -b - root;
    float t1 = -b + root;
    if (t0 < 0.0 && t1 < 0.0)
    {
        return -1.0;
    }
    if (t0 < 0.0)
    {
        return max(0.0, t1);
    }
    return max(0.0, t0);
}

float RayleighPhase(float cosTheta)
{
    return 3.0 / (16.0 * PI) * (1.0 + cosTheta * cosTheta);
}

float CornetteShanksMiePhase(float g, float cosTheta)
{
    float k = 3.0 / (8.0 * PI) * (1.0 - g * g) / (2.0 + g * g);
    return k * (1.0 + cosTheta * cosTheta) / pow(max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4), 1.5);
}

float2 FromUnitToSubUvs(float2 uv, float2 size) { return (uv + 0.5 / size) * (size / (size + 1.0)); }
float2 FromSubUvsToUnit(float2 uv, float2 size) { return (uv - 0.5 / size) * (size / (size - 1.0)); }

float2 TransmittanceParamsToUv(float viewHeight, float viewZenithCosAngle)
{
    float h = sqrt(max(0.0, TopRadius() * TopRadius() - BottomRadius() * BottomRadius()));
    float rho = sqrt(max(0.0, viewHeight * viewHeight - BottomRadius() * BottomRadius()));
    float discriminant = viewHeight * viewHeight * (viewZenithCosAngle * viewZenithCosAngle - 1.0) + TopRadius() * TopRadius();
    float d = max(0.0, -viewHeight * viewZenithCosAngle + sqrt(max(discriminant, 0.0)));
    float dMin = TopRadius() - viewHeight;
    float dMax = rho + h;
    return float2((d - dMin) / max(dMax - dMin, 1e-6), rho / h);
}

void UvToTransmittanceParams(float2 uv, out float viewHeight, out float viewZenithCosAngle)
{
    float h = sqrt(TopRadius() * TopRadius() - BottomRadius() * BottomRadius());
    float rho = h * uv.y;
    viewHeight = sqrt(rho * rho + BottomRadius() * BottomRadius());
    float dMin = TopRadius() - viewHeight;
    float dMax = rho + h;
    float d = dMin + uv.x * (dMax - dMin);
    viewZenithCosAngle = d == 0.0 ? 1.0 : (h * h - rho * rho - d * d) / (2.0 * viewHeight * d);
    viewZenithCosAngle = clamp(viewZenithCosAngle, -1.0, 1.0);
}

float3 SampleTransmittance(float viewHeight, float viewZenithCosAngle)
{
    return TransmittanceLut.SampleLevel(LinearClamp, TransmittanceParamsToUv(viewHeight, viewZenithCosAngle), 0).rgb;
}

float3 SampleMultiScattering(float viewHeight, float sunZenithCosAngle)
{
    float2 uv = saturate(float2(sunZenithCosAngle * 0.5 + 0.5, (viewHeight - BottomRadius()) / (TopRadius() - BottomRadius())));
    uv = FromUnitToSubUvs(uv, MultiScatteringLutSize);
    return MultiScatteringLut.SampleLevel(LinearClamp, uv, 0).rgb;
}

struct IntegrationResult
{
    float3 Luminance;
    float3 MultiScatteringAs1;
    float3 Transmittance;
};

// Integrates single scattering (plus the multi-scattering approximation when enabled) along one ray.
IntegrationResult IntegrateScatteredLuminance(float3 worldPos, float3 worldDir, float3 sunDir, bool ground, uint sampleCount, bool miePhaseRayleighPhase, bool multiScattering, float tMaxLimit)
{
    IntegrationResult result;
    result.Luminance = 0.0;
    result.MultiScatteringAs1 = 0.0;
    result.Transmittance = 1.0;
    float tBottom = RaySphereIntersectNearest(worldPos, worldDir, BottomRadius());
    float tTop = RaySphereIntersectNearest(worldPos, worldDir, TopRadius());
    float tMax = 0.0;
    if (tBottom < 0.0)
    {
        if (tTop < 0.0)
        {
            return result;
        }
        tMax = tTop;
    }
    else if (tTop > 0.0)
    {
        tMax = min(tTop, tBottom);
    }
    tMax = min(tMax, tMaxLimit);
    float cosTheta = dot(worldDir, sunDir);
    float miePhase = CornetteShanksMiePhase(MieAbsorption.w, cosTheta);
    float rayleighPhase = RayleighPhase(cosTheta);
    float uniformPhase = 1.0 / (4.0 * PI);
    float3 throughput = 1.0;
    float dt = tMax / (float)sampleCount;
    for (uint s = 0; s < sampleCount; s++)
    {
        float t = tMax * ((float)s + 0.3) / (float)sampleCount;
        float3 p = worldPos + t * worldDir;
        MediumSample medium = SampleMedium(p);
        float3 sampleTransmittance = exp(-medium.Extinction * dt);
        float pHeight = length(p);
        float3 up = p / pHeight;
        float sunZenithCosAngle = dot(sunDir, up);
        float3 transmittanceToSun = SampleTransmittance(pHeight, sunZenithCosAngle);
        float3 phaseTimesScattering = miePhaseRayleighPhase
            ? medium.ScatteringMie * miePhase + medium.ScatteringRayleigh * rayleighPhase
            : medium.Scattering * uniformPhase;
        float tEarth = RaySphereIntersectNearest(p + up * 0.001, sunDir, BottomRadius());
        float earthShadow = tEarth >= 0.0 ? 0.0 : 1.0;
        float3 multiScatteredLuminance = multiScattering ? SampleMultiScattering(pHeight, sunZenithCosAngle) : 0.0;
        float3 inScattering = earthShadow * transmittanceToSun * phaseTimesScattering + multiScatteredLuminance * medium.Scattering;
        float3 safeExtinction = max(medium.Extinction, 1e-9);
        float3 msIntegral = (medium.Scattering - medium.Scattering * sampleTransmittance) / safeExtinction;
        result.MultiScatteringAs1 += throughput * msIntegral;
        float3 sIntegral = (inScattering - inScattering * sampleTransmittance) / safeExtinction;
        result.Luminance += throughput * sIntegral;
        throughput *= sampleTransmittance;
    }
    if (ground && tBottom >= 0.0 && tMax == tBottom)
    {
        float3 p = worldPos + tBottom * worldDir;
        float pHeight = length(p);
        float3 up = p / pHeight;
        float sunZenithCosAngle = dot(sunDir, up);
        float3 transmittanceToSun = SampleTransmittance(pHeight, sunZenithCosAngle);
        result.Luminance += transmittanceToSun * throughput * saturate(sunZenithCosAngle) * GroundAlbedo.rgb / PI;
    }
    result.Transmittance = throughput;
    return result;
}

float3 IntegrateTransmittance(float viewHeight, float viewZenithCosAngle)
{
    float3 worldPos = float3(0.0, viewHeight, 0.0);
    float3 worldDir = float3(sqrt(max(0.0, 1.0 - viewZenithCosAngle * viewZenithCosAngle)), viewZenithCosAngle, 0.0);
    float tMax = RaySphereIntersectNearest(worldPos, worldDir, TopRadius());
    const uint sampleCount = 40;
    float3 opticalDepth = 0.0;
    float dt = max(tMax, 0.0) / (float)sampleCount;
    for (uint s = 0; s < sampleCount; s++)
    {
        float t = ((float)s + 0.5) * dt;
        opticalDepth += SampleMedium(worldPos + t * worldDir).Extinction * dt;
    }
    return exp(-opticalDepth);
}

float3 ComputeMultiScattering(float viewHeight, float sunZenithCosAngle)
{
    float3 worldPos = float3(0.0, viewHeight, 0.0);
    float3 sunDir = normalize(float3(0.0, sunZenithCosAngle, -sqrt(saturate(1.0 - sunZenithCosAngle * sunZenithCosAngle))));
    const uint sqrtSampleCount = 8;
    float3 multiScatteringAs1 = 0.0;
    float3 luminance = 0.0;
    for (uint i = 0; i < sqrtSampleCount; i++)
    {
        for (uint j = 0; j < sqrtSampleCount; j++)
        {
            float randA = ((float)i + 0.5) / (float)sqrtSampleCount;
            float randB = ((float)j + 0.5) / (float)sqrtSampleCount;
            float theta = 2.0 * PI * randA;
            float phi = acos(1.0 - 2.0 * randB);
            float3 worldDir = float3(cos(theta) * sin(phi), cos(phi), sin(theta) * sin(phi));
            IntegrationResult r = IntegrateScatteredLuminance(worldPos, worldDir, sunDir, true, 20, false, false, 9000000.0);
            multiScatteringAs1 += r.MultiScatteringAs1;
            luminance += r.Luminance;
        }
    }
    float sphereSolidAngle = 4.0 * PI;
    float isotropicPhase = 1.0 / sphereSolidAngle;
    float sampleWeight = sphereSolidAngle / (float)(sqrtSampleCount * sqrtSampleCount);
    multiScatteringAs1 *= sampleWeight * isotropicPhase;
    luminance *= sampleWeight * isotropicPhase;
    float3 sumOfAllEvents = 1.0 / (1.0 - min(multiScatteringAs1, 0.999));
    return Planet.w * luminance * sumOfAllEvents;
}

void UvToSkyViewParams(float2 uv, float viewHeight, out float viewZenithCosAngle, out float lightViewCosAngle)
{
    uv = FromSubUvsToUnit(uv, SkyViewLutSize);
    float vHorizon = sqrt(max(0.0, viewHeight * viewHeight - BottomRadius() * BottomRadius()));
    float cosBeta = vHorizon / viewHeight;
    float beta = acos(cosBeta);
    float zenithHorizonAngle = PI - beta;
    if (uv.y < 0.5)
    {
        float coord = 2.0 * uv.y;
        coord = 1.0 - coord;
        coord *= coord;
        coord = 1.0 - coord;
        viewZenithCosAngle = cos(zenithHorizonAngle * coord);
    }
    else
    {
        float coord = uv.y * 2.0 - 1.0;
        coord *= coord;
        viewZenithCosAngle = cos(zenithHorizonAngle + beta * coord);
    }
    float coordX = uv.x * uv.x;
    lightViewCosAngle = -(coordX * 2.0 - 1.0);
}

float2 SkyViewParamsToUv(bool intersectGround, float viewZenithCosAngle, float lightViewCosAngle, float viewHeight)
{
    float vHorizon = sqrt(max(0.0, viewHeight * viewHeight - BottomRadius() * BottomRadius()));
    float cosBeta = vHorizon / viewHeight;
    float beta = acos(cosBeta);
    float zenithHorizonAngle = PI - beta;
    float2 uv;
    if (!intersectGround)
    {
        float coord = acos(clamp(viewZenithCosAngle, -1.0, 1.0)) / zenithHorizonAngle;
        coord = 1.0 - coord;
        coord = sqrt(saturate(coord));
        coord = 1.0 - coord;
        uv.y = coord * 0.5;
    }
    else
    {
        float coord = (acos(clamp(viewZenithCosAngle, -1.0, 1.0)) - zenithHorizonAngle) / beta;
        coord = sqrt(saturate(coord));
        uv.y = coord * 0.5 + 0.5;
    }
    uv.x = sqrt(saturate(-lightViewCosAngle * 0.5 + 0.5));
    return FromUnitToSubUvs(uv, SkyViewLutSize);
}

float3 ComputeSkyView(float2 uv)
{
    float viewHeight = GroundAlbedo.w;
    float viewZenithCosAngle;
    float lightViewCosAngle;
    UvToSkyViewParams(uv, viewHeight, viewZenithCosAngle, lightViewCosAngle);
    float3 worldPos = float3(0.0, viewHeight, 0.0);
    float3 upVector = float3(0.0, 1.0, 0.0);
    float sunZenithCosAngle = dot(upVector, SunDirection.xyz);
    float3 sunDir = normalize(float3(sqrt(saturate(1.0 - sunZenithCosAngle * sunZenithCosAngle)), sunZenithCosAngle, 0.0));
    float viewZenithSinAngle = sqrt(saturate(1.0 - viewZenithCosAngle * viewZenithCosAngle));
    float3 worldDir = float3(viewZenithSinAngle * lightViewCosAngle, viewZenithCosAngle, viewZenithSinAngle * sqrt(saturate(1.0 - lightViewCosAngle * lightViewCosAngle)));
    IntegrationResult r = IntegrateScatteredLuminance(worldPos, worldDir, sunDir, false, 30, true, true, 9000000.0);
    return r.Luminance;
}

// Sky luminance per unit sun illuminance seen from the camera along an engine world direction.
float3 SampleSkyLuminance(float3 worldDir)
{
    float viewHeight = GroundAlbedo.w;
    float3 worldPos = float3(0.0, viewHeight, 0.0);
    float viewZenithCosAngle = worldDir.y;
    float2 horizontalView = worldDir.xz;
    float2 horizontalSun = SunDirection.xz;
    float horizontalViewLength = length(horizontalView);
    float horizontalSunLength = length(horizontalSun);
    float lightViewCosAngle = horizontalViewLength > 1e-5 && horizontalSunLength > 1e-5
        ? dot(horizontalView / horizontalViewLength, horizontalSun / horizontalSunLength)
        : 1.0;
    bool intersectGround = RaySphereIntersectNearest(worldPos, worldDir, BottomRadius()) >= 0.0;
    float2 uv = SkyViewParamsToUv(intersectGround, viewZenithCosAngle, lightViewCosAngle, viewHeight);
    return SkyViewLut.SampleLevel(LinearClamp, uv, 0).rgb;
}

float3 EvaluateShIrradiance(float3 n)
{
    return ShOutput[0].rgb * 0.282095
        + ShOutput[1].rgb * 0.488603 * n.y
        + ShOutput[2].rgb * 0.488603 * n.z
        + ShOutput[3].rgb * 0.488603 * n.x
        + ShOutput[4].rgb * 1.092548 * n.x * n.y
        + ShOutput[5].rgb * 1.092548 * n.y * n.z
        + ShOutput[6].rgb * 0.315392 * (3.0 * n.z * n.z - 1.0)
        + ShOutput[7].rgb * 1.092548 * n.x * n.z
        + ShOutput[8].rgb * 0.546274 * (n.x * n.x - n.y * n.y);
}

// ---- Volumetric clouds (ADR-0078/M0110) ----
// The clouds are traced into a panorama of the upper hemisphere around the camera (u = azimuth from +F, v = sqrt(elevation / 90 deg) so the
// horizon gets more rows). Each texel holds rgb = cloud luminance (already through the air in front of it) and a = the factor the sky behind
// it keeps. The sky composite and the sky light read it by direction.
uint CloudHash(uint3 v)
{
    v = v * 1664525u + 1013904223u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    v ^= v >> 16u;
    v.x += v.y * v.z;
    v.y += v.z * v.x;
    v.z += v.x * v.y;
    return v.x ^ v.y ^ v.z;
}

float CloudHash01(uint3 v)
{
    return (float)(CloudHash(v) >> 8) * (1.0 / 16777216.0);
}

float3 CloudGradient(int3 cell, int period)
{
    uint3 wrapped = (uint3)((cell % period + period) % period);
    uint h = CloudHash(wrapped);
    return float3((float)(h & 1023u), (float)((h >> 10) & 1023u), (float)((h >> 20) & 1023u)) / 511.5 - 1.0;
}

// Gradient noise that tiles every `period` cells, about -1 to 1.
float CloudPerlin(float3 x, int period)
{
    float3 cell = floor(x);
    int3 i = (int3)cell;
    float3 f = x - cell;
    float3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    float n000 = dot(CloudGradient(i + int3(0, 0, 0), period), f - float3(0.0, 0.0, 0.0));
    float n100 = dot(CloudGradient(i + int3(1, 0, 0), period), f - float3(1.0, 0.0, 0.0));
    float n010 = dot(CloudGradient(i + int3(0, 1, 0), period), f - float3(0.0, 1.0, 0.0));
    float n110 = dot(CloudGradient(i + int3(1, 1, 0), period), f - float3(1.0, 1.0, 0.0));
    float n001 = dot(CloudGradient(i + int3(0, 0, 1), period), f - float3(0.0, 0.0, 1.0));
    float n101 = dot(CloudGradient(i + int3(1, 0, 1), period), f - float3(1.0, 0.0, 1.0));
    float n011 = dot(CloudGradient(i + int3(0, 1, 1), period), f - float3(0.0, 1.0, 1.0));
    float n111 = dot(CloudGradient(i + int3(1, 1, 1), period), f - float3(1.0, 1.0, 1.0));
    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z);
}

// One minus the distance to the nearest feature point (one per cell), tiling every `cells` cells: 1 at a point, about 0 between points.
float CloudWorley(float3 x, int cells)
{
    float3 cell = floor(x);
    int3 i = (int3)cell;
    float3 f = x - cell;
    float nearest = 1.0;
    [unroll] for (int z = -1; z <= 1; z++)
    {
        [unroll] for (int y = -1; y <= 1; y++)
        {
            [unroll] for (int xOffset = -1; xOffset <= 1; xOffset++)
            {
                int3 neighbour = i + int3(xOffset, y, z);
                uint3 wrapped = (uint3)((neighbour % cells + cells) % cells);
                float3 feature = float3(CloudHash01(wrapped), CloudHash01(wrapped + 97u), CloudHash01(wrapped + 389u));
                float3 offset = float3(xOffset, y, z) + feature - f;
                nearest = min(nearest, dot(offset, offset));
            }
        }
    }
    return 1.0 - saturate(sqrt(nearest));
}

// The tiling Perlin-Worley noise of the cloud shapes (Schneider's Nubis): billowing Perlin eroded toward Worley cells.
float CloudPerlinWorley(float3 uvw)
{
    float perlin = 0.5 * CloudPerlin(uvw * 4.0, 4) + 0.25 * CloudPerlin(uvw * 8.0, 8) + 0.125 * CloudPerlin(uvw * 16.0, 16);
    perlin = saturate(0.5 + 0.5 * perlin / 0.875);
    float worley = 0.625 * CloudWorley(uvw * 4.0, 4) + 0.25 * CloudWorley(uvw * 8.0, 8) + 0.125 * CloudWorley(uvw * 16.0, 16);
    float perlinWorley = (perlin - (worley - 1.0)) / (2.0 - worley);
    // The raw values gather between about 0.58 and 0.75 (the 5 % and 95 % points measured over the volume); spread them over 0..1 so the
    // coverage thresholds below mean what they say.
    return saturate((perlinWorley - 0.585) / (0.746 - 0.585));
}

float CloudRemap(float value, float low, float high)
{
    return saturate((value - low) / max(high - low, 1e-4));
}

// Cloud extinction per km at a planet-centred position (km). The field is fixed to the world (the camera's position over the planet) and
// carried by the wind.
float CloudExtinction(float3 p, out float heightFraction)
{
    float radius = length(p);
    heightFraction = saturate((radius - CloudLayer.x) / (CloudLayer.y - CloudLayer.x));
    float2 world = CloudPosition.xy + float2(p.x, p.z) - CloudPosition.zw;
    float layout = CloudNoise.SampleLevel(LinearWrap, float3(world / (CloudShape.z * 0.125), 0.5), 0);
    // Unreal's Cloud_GlobalCoverage shifts the coverage around one half: -0.2 leaves a blue sky with scattered cumulus.
    float coverage = saturate(0.5 * layout + 0.18 + 0.5 * CloudShape.y);
    // Cumulus: flat bases, tops a kilometre or two up in the 10 km layer, sparser higher.
    float profile = saturate(heightFraction * 50.0) * saturate((0.22 - heightFraction) / 0.14);
    float altitude = radius - CloudLayer.x;
    float3 q = float3(world.x, altitude, world.y);
    float base = CloudNoise.SampleLevel(LinearWrap, q / 12.0, 0);
    float cover = coverage * profile;
    float shape = CloudRemap(base, 1.0 - cover, 1.0);
    if (shape <= 0.0)
    {
        return 0.0;
    }
    float detail = CloudNoise.SampleLevel(LinearWrap, q / 2.0 + 0.37, 0);
    shape = CloudRemap(shape, (1.0 - detail) * 0.35, 1.0);
    return shape * CloudShape.x;
}

float HenyeyGreenstein(float g, float cosTheta)
{
    float denominator = max(1.0 + g * g - 2.0 * g * cosTheta, 1e-4);
    return (1.0 - g * g) / (4.0 * PI * denominator * sqrt(denominator));
}

float CloudPhaseFunction(float cosTheta, float eccentricityScale)
{
    return lerp(HenyeyGreenstein(CloudPhase.x * eccentricityScale, cosTheta), HenyeyGreenstein(CloudPhase.y * eccentricityScale, cosTheta), CloudPhase.z);
}

// Optical depth from a cloud sample toward the sun: six segments growing quadratically to 4.32 km.
float CloudOpticalDepthToSun(float3 p, float3 sunDir)
{
    float tau = 0.0;
    float previous = 0.0;
    [loop] for (uint i = 1; i <= 6; i++)
    {
        float t = 0.12 * (float)(i * i);
        float heightFraction;
        tau += CloudExtinction(p + sunDir * (0.5 * (t + previous)), heightFraction) * (t - previous);
        previous = t;
    }
    return tau;
}

float3 CloudPanoramaDirection(float2 uv)
{
    float elevation = uv.y * uv.y * (0.5 * PI);
    float azimuth = (uv.x - 0.5) * 2.0 * PI;
    float horizontal = cos(elevation);
    return float3(sin(azimuth) * horizontal, sin(elevation), cos(azimuth) * horizontal);
}

float2 CloudPanoramaUv(float3 worldDir)
{
    float elevation = asin(saturate(worldDir.y));
    return float2(atan2(worldDir.x, worldDir.z) / (2.0 * PI) + 0.5, sqrt(elevation / (0.5 * PI)));
}

float4 SampleCloudPanoramaTexel(float2 uv)
{
    float2 size = float2(CloudUpdate.zw);
    uv.y = clamp(uv.y, 0.5 / size.y, 1.0 - 0.5 / size.y);
    return CloudPanorama.SampleLevel(LinearWrap, uv, 0);
}

// Bilinear read, for the sky light's coarse sampling.
float4 SampleCloudPanorama(float3 worldDir)
{
    if (worldDir.y <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    return SampleCloudPanoramaTexel(CloudPanoramaUv(worldDir));
}

// Cubic B-spline read from four bilinear taps (Sigg and Hadwiger), for the sky the camera sees: the panorama is magnified several times,
// and a bilinear read shows its texel grid along the cloud edges.
float4 SampleCloudPanoramaSmooth(float3 worldDir)
{
    if (worldDir.y <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    float2 size = float2(CloudUpdate.zw);
    float2 position = CloudPanoramaUv(worldDir) * size - 0.5;
    float2 cell = floor(position);
    float2 f = position - cell;
    float2 f2 = f * f;
    float2 f3 = f2 * f;
    float2 w0 = (1.0 / 6.0) * (-f3 + 3.0 * f2 - 3.0 * f + 1.0);
    float2 w1 = (1.0 / 6.0) * (3.0 * f3 - 6.0 * f2 + 4.0);
    float2 w2 = (1.0 / 6.0) * (-3.0 * f3 + 3.0 * f2 + 3.0 * f + 1.0);
    float2 w3 = (1.0 / 6.0) * f3;
    float2 g0 = w0 + w1;
    float2 g1 = w2 + w3;
    float2 h0 = (cell + 0.5 + w1 / g0 - 1.0) / size;
    float2 h1 = (cell + 0.5 + w3 / g1 + 1.0) / size;
    return g0.x * (g0.y * SampleCloudPanoramaTexel(float2(h0.x, h0.y)) + g1.y * SampleCloudPanoramaTexel(float2(h0.x, h1.y)))
        + g1.x * (g0.y * SampleCloudPanoramaTexel(float2(h1.x, h0.y)) + g1.y * SampleCloudPanoramaTexel(float2(h1.x, h1.y)));
}

// Marches one view ray through the cloud layer (Hillaire's energy-conserving integration).
float4 TraceCloud(float3 dir, float jitter)
{
    float viewRadius = GroundAlbedo.w;
    float3 origin = float3(0.0, viewRadius, 0.0);
    float tBottom = RaySphereIntersectNearest(origin, dir, CloudLayer.x);
    float tTop = RaySphereIntersectNearest(origin, dir, CloudLayer.y);
    float tEnter;
    float tExit;
    if (viewRadius < CloudLayer.x)
    {
        if (tBottom < 0.0 || tTop < 0.0)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }
        tEnter = tBottom;
        tExit = tTop;
    }
    else if (viewRadius < CloudLayer.y)
    {
        tEnter = 0.0;
        tExit = tBottom >= 0.0 ? tBottom : max(tTop, 0.0);
    }
    else
    {
        if (tTop < 0.0)
        {
            return float4(0.0, 0.0, 0.0, 1.0);
        }
        tEnter = tTop;
        tExit = tTop + CloudLayer.w;
    }
    if (tEnter > CloudLayer.z || tExit <= tEnter)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    tExit = min(tExit, tEnter + CloudLayer.w);
    uint sampleCount = max((uint)CloudMultiScattering.w, 1u);
    // The sky light on the clouds is the clear sky's mean radiance over the upper hemisphere (the zenith and four directions 30 degrees up),
    // read from the sky view LUT: it does not see the clouds, so the panorama never feeds back into itself.
    float3 skyAmbient = SampleSkyLuminance(float3(0.0, 1.0, 0.0)) * 0.5;
    skyAmbient += SampleSkyLuminance(float3(0.866025, 0.5, 0.0)) * 0.125;
    skyAmbient += SampleSkyLuminance(float3(-0.866025, 0.5, 0.0)) * 0.125;
    skyAmbient += SampleSkyLuminance(float3(0.0, 0.5, 0.866025)) * 0.125;
    skyAmbient += SampleSkyLuminance(float3(0.0, 0.5, -0.866025)) * 0.125;
    skyAmbient *= SunIlluminance.rgb * SunIlluminance.w;
    float dt = (tExit - tEnter) / (float)sampleCount;
    float3 sunDir = SunDirection.xyz;
    float cosTheta = dot(dir, sunDir);
    float3 luminance = 0.0;
    float transmittance = 1.0;
    float weightSum = 0.0;
    float distanceSum = 0.0;
    [loop] for (uint s = 0; s < sampleCount; s++)
    {
        float t = tEnter + ((float)s + jitter) * dt;
        float3 p = origin + dir * t;
        float heightFraction;
        float extinction = CloudExtinction(p, heightFraction);
        if (extinction <= 0.0)
        {
            continue;
        }
        float pRadius = length(p);
        float3 up = p / pRadius;
        float3 sunLuminance = SunIlluminance.rgb * SampleTransmittance(pRadius, dot(sunDir, up));
        float tauSun = CloudOpticalDepthToSun(p, sunDir);
        float sunTerm = 0.0;
        float contribution = 1.0;
        float occlusion = 1.0;
        float eccentricity = 1.0;
        [unroll] for (uint octave = 0; octave < 3; octave++)
        {
            sunTerm += contribution * CloudPhaseFunction(cosTheta, eccentricity) * exp(-tauSun * occlusion);
            contribution *= CloudMultiScattering.x;
            occlusion *= CloudMultiScattering.y;
            eccentricity *= CloudMultiScattering.z;
        }
        float3 ambient = skyAmbient * lerp(1.0 - CloudAlbedo.w, 1.0, heightFraction);
        float3 source = extinction * CloudAlbedo.rgb * (sunLuminance * sunTerm + ambient);
        float sampleTransmittance = exp(-extinction * dt);
        float3 integrated = (source - source * sampleTransmittance) / extinction;
        luminance += transmittance * integrated;
        float weight = transmittance * (1.0 - sampleTransmittance);
        weightSum += weight;
        distanceSum += weight * t;
        transmittance *= sampleTransmittance;
        if (transmittance < CloudShape.w)
        {
            break;
        }
    }
    if (weightSum <= 0.0)
    {
        return float4(0.0, 0.0, 0.0, 1.0);
    }
    // The air between the camera and the clouds: the transmittance to the luminance-weighted cloud depth dims the clouds and leaves that
    // much of the sky's in-scattering in front of them.
    float3 cloudPoint = origin + dir * (distanceSum / weightSum);
    float cloudRadius = length(cloudPoint);
    float3 toTop = SampleTransmittance(viewRadius, dir.y);
    float3 cloudToTop = SampleTransmittance(cloudRadius, dot(dir, cloudPoint / cloudRadius));
    float3 aerial = saturate(toTop / max(cloudToTop, 1e-4));
    float skyKept = transmittance + (1.0 - transmittance) * (1.0 - dot(aerial, 1.0 / 3.0));
    return float4(luminance * aerial, skyKept);
}

// Radiance the sky light sees: atmosphere (and clouds) above the horizon, the authored colour below it.
float3 SkyLightRadiance(float3 worldDir)
{
    if (worldDir.y < 0.0)
    {
        return LowerHemisphere.rgb;
    }
    float3 sky = SampleSkyLuminance(worldDir) * SunIlluminance.rgb;
    if (CloudPhase.w > 0.0)
    {
        float4 cloud = SampleCloudPanorama(worldDir);
        sky = sky * cloud.a + cloud.rgb;
    }
    return sky;
}

float3 ApplyHeightFog(float3 color, float3 cameraToReceiver, float3 skyAmbientLuminance)
{
    if (FogParams.w <= 0.0)
    {
        return color;
    }
    float rayLength = length(cameraToReceiver);
    float falloff = max(-127.0, FogParams.y * cameraToReceiver.y);
    float lineIntegral = abs(falloff) > 1e-4
        ? FogParams.x * (1.0 - exp2(-falloff)) / falloff
        : FogParams.x * 0.6931471805599453;
    lineIntegral *= rayLength;
    float transmittance = max(saturate(exp2(-lineIntegral)), FogParams.z);
    float3 inscattering = FogColor.rgb + skyAmbientLuminance * FogColor.w;
    return color * transmittance + inscattering * (1.0 - transmittance);
}

groupshared float3 ShPartial[64][9];

void ProjectSh(float3 direction, float3 radiance, float weight, inout float3 coefficients[9])
{
    float x = direction.x;
    float y = direction.y;
    float z = direction.z;
    float basis[9] = {
        0.282095,
        0.488603 * y,
        0.488603 * z,
        0.488603 * x,
        1.092548 * x * y,
        1.092548 * y * z,
        0.315392 * (3.0 * z * z - 1.0),
        1.092548 * x * z,
        0.546274 * (x * x - y * y),
    };
    [unroll] for (uint i = 0; i < 9; i++)
    {
        coefficients[i] += radiance * basis[i] * weight;
    }
}

[numthreads(8, 8, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    uint pass = Pass.x;
    if (pass == PassSkyLightSh)
    {
        // 64 threads x 32 directions stratify the full sphere (32 latitude bands x 64 azimuths).
        float3 coefficients[9];
        [unroll] for (uint c = 0; c < 9; c++)
        {
            coefficients[c] = 0.0;
        }
        const uint latitudeBands = 32;
        const uint azimuthSteps = 64;
        for (uint k = 0; k < 32; k++)
        {
            uint sampleIndex = groupIndex * 32 + k;
            uint band = sampleIndex / azimuthSteps;
            uint step = sampleIndex % azimuthSteps;
            float cosTheta = 1.0 - 2.0 * ((float)band + 0.5) / (float)latitudeBands;
            float sinTheta = sqrt(saturate(1.0 - cosTheta * cosTheta));
            float phi = 2.0 * PI * ((float)step + 0.5) / (float)azimuthSteps;
            float3 direction = float3(sinTheta * cos(phi), cosTheta, sinTheta * sin(phi));
            float weight = 4.0 * PI / (float)(latitudeBands * azimuthSteps);
            ProjectSh(direction, SkyLightRadiance(direction) * SunIlluminance.w, weight, coefficients);
        }
        [unroll] for (uint c2 = 0; c2 < 9; c2++)
        {
            ShPartial[groupIndex][c2] = coefficients[c2];
        }
        GroupMemoryBarrierWithGroupSync();
        if (groupIndex == 0)
        {
            // Irradiance convolution of radiance SH: A0 = pi, A1 = 2pi/3, A2 = pi/4.
            static const float bandScale[9] = { PI, 2.0 * PI / 3.0, 2.0 * PI / 3.0, 2.0 * PI / 3.0, PI / 4.0, PI / 4.0, PI / 4.0, PI / 4.0, PI / 4.0 };
            for (uint i = 0; i < 9; i++)
            {
                float3 total = 0.0;
                for (uint t = 0; t < 64; t++)
                {
                    total += ShPartial[t][i];
                }
                ShOutput[i] = float4(total * bandScale[i], 0.0);
            }
        }
        return;
    }
    if (pass == PassCloudNoise)
    {
        uint3 voxel = dispatchThreadId;
        if (voxel.x >= CloudNoiseSize || voxel.y >= CloudNoiseSize || voxel.z >= CloudNoiseSize)
        {
            return;
        }
        CloudNoiseOutput[voxel] = CloudPerlinWorley(((float3)voxel + 0.5) / (float)CloudNoiseSize);
        return;
    }
    if (pass == PassCloudPanorama)
    {
        // Either every texel, or one texel of each 4 x 4 block in Bayer order, so 16 frames refresh the whole panorama.
        static const uint2 bayerOffsets[16] = {
            uint2(0, 0), uint2(2, 2), uint2(2, 0), uint2(0, 2), uint2(1, 1), uint2(3, 3), uint2(3, 1), uint2(1, 3),
            uint2(1, 0), uint2(3, 2), uint2(3, 0), uint2(1, 2), uint2(0, 1), uint2(2, 3), uint2(2, 1), uint2(0, 3) };
        uint2 texel = CloudUpdate.y != 0u ? dispatchThreadId.xy : dispatchThreadId.xy * 4u + bayerOffsets[CloudUpdate.x & 15u];
        if (texel.x >= CloudUpdate.z || texel.y >= CloudUpdate.w)
        {
            return;
        }
        float2 panoramaUv = ((float2)texel + 0.5) / float2(CloudUpdate.zw);
        // A fixed start offset per texel: the panorama is the same each time it is refreshed with the same inputs.
        OutputTexture[texel] = TraceCloud(CloudPanoramaDirection(panoramaUv), CloudHash01(uint3(texel, 7u)));
        return;
    }
    uint2 pixel = dispatchThreadId.xy;
    if (pixel.x >= Pass.y || pixel.y >= Pass.z)
    {
        return;
    }
    float2 size = float2(Pass.y, Pass.z);
    float2 uv = ((float2)pixel + 0.5) / size;
    if (pass == PassTransmittance)
    {
        float viewHeight;
        float viewZenithCosAngle;
        UvToTransmittanceParams(uv, viewHeight, viewZenithCosAngle);
        OutputTexture[pixel] = float4(IntegrateTransmittance(viewHeight, viewZenithCosAngle), 1.0);
        return;
    }
    if (pass == PassMultiScattering)
    {
        float2 unitUv = FromSubUvsToUnit(uv, MultiScatteringLutSize);
        float sunZenithCosAngle = unitUv.x * 2.0 - 1.0;
        float viewHeight = BottomRadius() + saturate(unitUv.y) * (TopRadius() - BottomRadius() - 0.01) + 0.005;
        OutputTexture[pixel] = float4(ComputeMultiScattering(viewHeight, sunZenithCosAngle), 1.0);
        return;
    }
    if (pass == PassSkyView)
    {
        OutputTexture[pixel] = float4(ComputeSkyView(uv), 1.0);
        return;
    }
    if (pass == PassSkyBackground)
    {
        float depth = SceneDepth.Load(int3(pixel, 0));
        float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
        if (depth < 1.0)
        {
            // M0110: with a sky sphere (Unreal's SM_SkySphere and its Is Sky material), geometry farther than the sphere is covered by the sky.
            if (SkySphere.w <= 0.0)
            {
                return;
            }
            float3 surface;
            if (SkySphereDepth.x == 0.0)
            {
                float4 surfacePoint = mul(float4(ndc, depth, 1.0), InverseViewProjection);
                surface = surfacePoint.xyz / surfacePoint.w;
            }
            else
            {
                // Radial depth (ADR-0076): depth = log2(1 + r / T) / G, so r = T (2^(depth G) - 1) along the pixel's ray.
                float4 farRay = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
                float4 nearRay = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
                float3 rayDir = normalize(farRay.xyz / farRay.w - nearRay.xyz / nearRay.w);
                surface = CameraPosition.xyz + rayDir * (SkySphereDepth.y * (exp2(depth * SkySphereDepth.z) - 1.0));
            }
            if (length(surface - SkySphere.xyz) <= SkySphere.w)
            {
                return;
            }
        }
        float4 farPoint = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
        float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
        float3 worldDir = normalize(farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w);
        float3 luminance = SampleSkyLuminance(worldDir) * SunIlluminance.rgb;
        float sunCos = dot(worldDir, SunDirection.xyz);
        float sunRadius = SunDirection.w;
        if (sunCos > cos(sunRadius))
        {
            float3 worldPos = float3(0.0, GroundAlbedo.w, 0.0);
            bool blocked = RaySphereIntersectNearest(worldPos, worldDir, BottomRadius()) >= 0.0;
            if (!blocked)
            {
                float solidAngle = 2.0 * PI * (1.0 - cos(sunRadius));
                float3 transmittance = SampleTransmittance(GroundAlbedo.w, worldDir.y);
                luminance += SunIlluminance.rgb / solidAngle * transmittance;
            }
        }
        // The fog's sky contribution is the sky light's zenith irradiance expressed as the
        // luminance of an equivalent uniform sky (E/pi); geometry fog in the Stage shader uses the
        // same SH value so sky and surfaces fade into one colour.
        if (CloudPhase.w > 0.0)
        {
            // M0110: the clouds in front of the sky (and of the sun disk).
            float4 cloud = SampleCloudPanoramaSmooth(worldDir);
            luminance = luminance * cloud.a + cloud.rgb;
        }
        float3 skyAmbient = EvaluateShIrradiance(float3(0.0, 1.0, 0.0)) / PI;
        luminance = ApplyHeightFog(luminance, worldDir * 1000000.0, skyAmbient);
        float exposure = LowerHemisphere.w;
        if (Pass.w != 0u)
        {
            exposure = ExposureState[0].y;
        }
        OutputTexture[pixel] = float4(luminance * exposure, 1.0);
        return;
    }
}
