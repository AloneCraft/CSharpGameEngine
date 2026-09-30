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
    uint4 Pass;                  // x pass, y output width, z output height
};
Texture2D<float4> TransmittanceLut : register(t0);
Texture2D<float4> MultiScatteringLut : register(t1);
Texture2D<float4> SkyViewLut : register(t2);
Texture2D<float> SceneDepth : register(t3);
RWTexture2D<float4> OutputTexture : register(u0);
RWStructuredBuffer<float4> ShOutput : register(u1);
SamplerState LinearClamp : register(s0);

static const float PI = 3.14159265359;
static const uint PassTransmittance = 0;
static const uint PassMultiScattering = 1;
static const uint PassSkyView = 2;
static const uint PassSkyLightSh = 3;
static const uint PassSkyBackground = 4;
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

// Radiance the sky light sees: atmosphere above the horizon, the authored colour below it.
float3 SkyLightRadiance(float3 worldDir)
{
    if (worldDir.y < 0.0)
    {
        return LowerHemisphere.rgb;
    }
    return SampleSkyLuminance(worldDir) * SunIlluminance.rgb;
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
        if (depth < 1.0)
        {
            return;
        }
        float2 ndc = float2(uv.x * 2.0 - 1.0, 1.0 - uv.y * 2.0);
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
        float3 skyAmbient = EvaluateShIrradiance(float3(0.0, 1.0, 0.0)) / PI;
        luminance = ApplyHeightFog(luminance, worldDir * 1000000.0, skyAmbient);
        OutputTexture[pixel] = float4(luminance * LowerHemisphere.w, 1.0);
        return;
    }
}
