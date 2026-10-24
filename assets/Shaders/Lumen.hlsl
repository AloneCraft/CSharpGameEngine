// Lumen-like dynamic global illumination and reflections (ADR-0078/M0111). Everything is recomputed every frame from the scene as it is; nothing is
// baked. The host (StageLumen) selects the pass through LumenCB.Pass.x:
//   0 card lighting       the surface cache: each card texel (albedo, normal and emission captured from its mesh) is lit by the sun (shadow map,
//                         or the global distance field off the cascades), the local lights and last frame's radiance cache, so bounces add up.
//   1 radiance cache      world probes in clipmaps around the camera trace the global distance field; a hit reads the surface cache, a miss the sky.
//   2 screen probe trace  a probe every few pixels traces the screen, then the distance field near it, then reads the radiance cache further away.
//   3 screen probe filter the probes' radiance is filtered with their neighbours and projected to irradiance spherical harmonics.
//   4 integrate           each pixel interpolates its probes' irradiance and blends it with last frame's (reprojected).
//   5 reflection trace    a GGX-sampled ray per pixel: the screen, then the distance field (the surface cache), then the sky; rough lobes read the
//                         probes instead.
//   6 reflection resolve  the traces are filtered and blended with last frame's.
//   7 visualize           debugging (Unreal's r.Lumen.Visualize): the frame shows the diffuse irradiance, the reflections or the surface cache.
// Root signature (host-owned, StageLumen): 0 = root CBV b0 LumenCB, 1 = root CBV b1 the Stage FrameCB of the frame, 2-14 = root SRVs t0-t12,
// 15 = SRV table t13-t37, 16 = UAV table u0-u9. s0 linear clamp, s1 linear wrap, s2 shadow comparison, s3 point clamp.
struct DirectionalLightData
{
    float4 DirectionAndShadowBias;
    float4 ColorTimesIntensity;
};
struct LocalLightData
{
    float4 PositionAndRange;
    float4 DirectionAndInnerCos;
    float4 ColorAndOuterCos;
    float4 TypeAndPadding;
    float4 RightAndHalfWidth;
    float4 UpAndHalfHeight;
};
struct ShadowProjectionData
{
    row_major float4x4 ViewProjection;
    float4 UvScaleOffset;
};
// The Stage program's FrameCB (stage-v13), read for the lights, the shadow cascades and the sky light flag.
cbuffer FrameCB : register(b1)
{
    row_major float4x4 ViewProjection;
    float4 CameraPositionWS;
    DirectionalLightData DirectionalLights[4];
    float4 AmbientColor;
    float4 FogParams0;
    float4 FogColor;
    float4 ExposureParams;
    int4 LightCounts;
    uint4 ForwardPlusGrid;
    row_major float4x4 ShadowViewProjection;
    float4 DirectionalShadowAtlasUvScaleOffset;
    float4 SkyLightParams;
    float4 PhysicalFogParams;
    float4 PhysicalFogColor;
    row_major float4x4 CascadeViewProjection[4];
    float4 CascadeSpheres[4];
    float4 CascadeUvScaleOffsets[4];
    float4 CascadeDepthRanges;
    float4 GiGridOrigin;
    float4 GiGridSpacing;
    uint4 GiGridCounts;
    float4 GiAtlasSize;
    float4 AmbientOcclusionParams;
    float4 DepthMapping0;
    float4 DepthMapping1;
    float4 LumenParams;
};
cbuffer LumenCB : register(b0)
{
    uint4 Pass;                          // x pass, y work items, z frame number, w flags (Flag*)
    uint4 Screen;                        // xy screen size, zw reflection trace size
    uint4 ProbeGrid;                     // xy screen probes, z pixels per probe, w reflection downsample (1 or 2)
    float4 Jitter;                       // xy this frame's probe pixel within its tile, zw ray jitter within an octahedral texel
    row_major float4x4 CameraViewProjection;   // render space -> clip (x, y and w are the camera's)
    row_major float4x4 InverseViewProjection;  // clip -> render space (finite planes, for pixel rays)
    row_major float4x4 PreviousViewProjection; // last frame's view-projection applied to this frame's render space
    float4 Camera;                       // xyz camera (render space), w exposure of the scene colour history (manual exposure)
    float4 PreviousCamera;               // xyz last frame's camera in this frame's render space
    float4 Trace;                        // x max trace distance m, y screen probe near field m, z screen trace distance m, w max roughness to trace
    float4 Surface;                      // x atlas texels, y 1 / atlas texels, z diffuse colour boost, w skylight leaking
    float4 GridOrigin;                   // x, z render-space corner of the object grid, w cell size m
    uint4 Grid;                          // x cells per side, y objects
    float4 CacheScale[4];                // radiance cache level: x probe spacing m, y probes per side, z 1 / probes per side
    float4 CacheOrigin[4];               // xyz render-space position of the level's lowest probe
    uint4 CacheSlot[4];                  // xyz slot of the lowest probe (its coordinate mod probes per side), w first probe index of the level
    uint4 Cache;                         // x levels, y probes per side, z probes per atlas row, w unused
    float4 ClipmapScale[4];              // the global distance field (StageGlobalDistanceField.ClipmapConstants)
    float4 ClipmapOrigin[4];
    float4 ClipmapTexel[4];
    uint4 Clipmap;                       // x levels
    float4 Temporal;                     // x diffuse new-sample weight, y reflection new-sample weight, z radiance cache new-sample weight, w this frame's exposure
};
struct LumenObject
{
    float4 WorldToLocal0;                // local = (dot(row.xyz, p) + row.w, ...)
    float4 WorldToLocal1;
    float4 WorldToLocal2;
    float4 LocalToWorld0;                // world = (dot(row.xyz, q) + row.w, ...)
    float4 LocalToWorld1;
    float4 LocalToWorld2;
    float4 BoundsMin;                    // xyz the cards' box in the mesh's space, w mesh record (asuint)
    float4 BoundsMax;                    // xyz, w distance scale (the object's smallest scale)
    uint4 Cards;                         // x first card, y faces ready (bit per face), z unused, w emissive scale (asuint)
};
struct MeshRecord
{
    float3 BoundsMin;
    float VoxelSize;
    uint3 Resolution;
    uint DataOffset;
};
StructuredBuffer<LumenObject> Objects : register(t0);
StructuredBuffer<uint4> Cards : register(t1);              // x, y atlas texel of the card's tile, z width, w height
StructuredBuffer<uint2> GridCells : register(t2);          // x first entry in GridObjects, y count
StructuredBuffer<uint> GridObjects : register(t3);
StructuredBuffer<MeshRecord> Meshes : register(t4);
ByteAddressBuffer MeshDistances : register(t5);
StructuredBuffer<LocalLightData> LocalLights : register(t6);
StructuredBuffer<ShadowProjectionData> SpotShadowProjections : register(t7);
StructuredBuffer<ShadowProjectionData> PointShadowProjections : register(t8);
StructuredBuffer<float4> SkyIrradianceSh : register(t9);
StructuredBuffer<uint4> WorkItems : register(t10);
StructuredBuffer<float4> ExposureState : register(t11);
StructuredBuffer<uint> CacheValid : register(t12);         // per probe: 1 once traced since it entered its clip
Texture3D<float> ClipmapLevels[4] : register(t13);         // t13-t16
Texture2D<float4> CardAlbedo : register(t17);
Texture2D<float4> CardNormal : register(t18);
Texture2D<float4> CardEmissive : register(t19);
Texture2D<float> CardDepth : register(t20);
Texture2D<float4> CardRadiance : register(t21);
Texture2D<float> ShadowAtlas : register(t22);
Texture2D<float4> SkyRadiance : register(t23);
Texture2D<float4> CacheRadiance : register(t24);
StructuredBuffer<float4> CacheIrradiance : register(t25);
Texture2D<float4> SurfaceNormal : register(t26);
Texture2D<float> SurfaceDistance : register(t27);
Texture2D<float4> PreviousSurfaceNormal : register(t28);
Texture2D<float> PreviousSurfaceDistance : register(t29);
Texture2D<float4> SceneColorHistory : register(t30);
Texture2D<float4> ProbeRadiance : register(t31);
StructuredBuffer<float4> ProbeInfo : register(t32);        // per screen probe: position (w valid), normal (w distance)
StructuredBuffer<float4> ProbeSh : register(t33);          // per screen probe: 9 irradiance SH coefficients
Texture2D<float4> DiffuseHistory : register(t34);
Texture2D<float4> ReflectionHistory : register(t35);
Texture2D<float4> ReflectionTrace : register(t36);
StructuredBuffer<float4> HistoryExposureState : register(t37);  // the auto exposure state the scene colour history was drawn with
RWTexture2D<float4> CardRadianceOut : register(u0);
RWTexture2D<float4> CacheRadianceOut : register(u1);
RWStructuredBuffer<float4> CacheIrradianceOut : register(u2);
RWTexture2D<float4> ProbeRadianceOut : register(u3);
RWStructuredBuffer<float4> ProbeInfoOut : register(u4);
RWStructuredBuffer<float4> ProbeShOut : register(u5);
RWTexture2D<float4> DiffuseOut : register(u6);
RWTexture2D<float4> ReflectionTraceOut : register(u7);
RWTexture2D<float4> ReflectionOut : register(u8);
RWTexture2D<float4> VisualizeOut : register(u9);
SamplerState LinearClamp : register(s0);
SamplerState LinearWrap : register(s1);
SamplerComparisonState ShadowSampler : register(s2);
SamplerState PointClamp : register(s3);

static const float PI = 3.14159265359;
static const uint PassCardLighting = 0;
static const uint PassRadianceCache = 1;
static const uint PassScreenProbeTrace = 2;
static const uint PassScreenProbeFilter = 3;
static const uint PassIntegrate = 4;
static const uint PassReflectionTrace = 5;
static const uint PassReflectionResolve = 6;
static const uint PassVisualize = 7;
static const uint FlagHistory = 1u;
static const uint FlagSkyMap = 2u;
static const uint FlagGatherScreenTraces = 4u;
static const uint FlagReflectionScreenTraces = 8u;
static const uint FlagAutoExposure = 16u;
static const uint FlagSceneColorHistory = 32u;
static const uint OctahedronTexels = 8;
static const uint CacheTile = OctahedronTexels + 2;
static const uint MaximumCellObjects = 32;
// The surface cache, the probes and the reflections hold radiance in half floats; anything brighter than this (or not finite) is clamped or dropped.
static const float MaximumRadiance = 60000.0;

float3 Sanitize(float3 value, float maximum)
{
    return all(isfinite(value)) ? clamp(value, 0.0, maximum) : float3(0.0, 0.0, 0.0);
}

// ---- Directions --------------------------------------------------------------------------------------------------------------------------
// Octahedral mapping with +Z as the pole, as the GI probes and the sky radiance map use it.
float2 OctahedralEncode(float3 direction)
{
    float3 n = direction / (abs(direction.x) + abs(direction.y) + abs(direction.z));
    float2 e = n.xy;
    if (n.z < 0.0)
    {
        e = (1.0 - abs(n.yx)) * select(n.xy >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
    }
    return e;
}
float3 OctahedralDecode(float2 e)
{
    float3 v = float3(e, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0.0)
    {
        v.xy = (1.0 - abs(v.yx)) * select(v.xy >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
    }
    return normalize(v);
}
// The direction of octahedral texel (x, y) of a size x size map, shifted by a jitter in [0, 1)^2 (0.5 is the texel centre).
float3 TexelDirection(uint2 texel, float2 jitter, float size)
{
    return OctahedralDecode(((float2)texel + jitter) / size * 2.0 - 1.0);
}
// Solid angle weight of an octahedral texel centre (the octahedron's point v has density 1 / |v|^3); the callers normalise the sum.
float TexelSolidAngle(uint2 texel, float size)
{
    float2 e = ((float2)texel + 0.5) / size * 2.0 - 1.0;
    float3 v = float3(e, 1.0 - abs(e.x) - abs(e.y));
    if (v.z < 0.0)
    {
        v.xy = (1.0 - abs(v.yx)) * select(v.xy >= 0.0, float2(1.0, 1.0), float2(-1.0, -1.0));
    }
    float lengthSquared = dot(v, v);
    return 1.0 / (lengthSquared * sqrt(lengthSquared));
}
void TangentFrame(float3 n, out float3 t, out float3 b)
{
    float3 reference = abs(n.y) < 0.99 ? float3(0.0, 1.0, 0.0) : float3(1.0, 0.0, 0.0);
    t = normalize(cross(reference, n));
    b = cross(n, t);
}
uint Hash(uint value)
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    value ^= value >> 16;
    return value;
}
float2 Random2(uint2 pixel, uint frame)
{
    uint seed = Hash(pixel.x + Hash(pixel.y + Hash(frame)));
    return float2((float)(seed & 0xffffu) / 65536.0, (float)(Hash(seed) & 0xffffu) / 65536.0);
}

// ---- Spherical harmonics (the sky light's convention: irradiance coefficients, evaluated with the radiance basis) --------------------------
void ShBasis(float3 d, out float basis[9])
{
    basis[0] = 0.282095;
    basis[1] = 0.488603 * d.y;
    basis[2] = 0.488603 * d.z;
    basis[3] = 0.488603 * d.x;
    basis[4] = 1.092548 * d.x * d.y;
    basis[5] = 1.092548 * d.y * d.z;
    basis[6] = 0.315392 * (3.0 * d.z * d.z - 1.0);
    basis[7] = 1.092548 * d.x * d.z;
    basis[8] = 0.546274 * (d.x * d.x - d.y * d.y);
}
static const float ShBandScale[9] = { PI, 2.0 * PI / 3.0, 2.0 * PI / 3.0, 2.0 * PI / 3.0, PI / 4.0, PI / 4.0, PI / 4.0, PI / 4.0, PI / 4.0 };
float3 EvaluateSkyIrradiance(float3 n)
{
    if (SkyLightParams.x <= 0.5)
    {
        return float3(0.0, 0.0, 0.0);
    }
    float basis[9];
    ShBasis(n, basis);
    float3 result = 0.0;
    [unroll] for (uint i = 0; i < 9; i++)
    {
        result += SkyIrradianceSh[i].rgb * basis[i];
    }
    return max(result, 0.0);
}
float3 SkyRadianceAt(float3 direction)
{
    if ((Pass.w & FlagSkyMap) != 0u)
    {
        return SkyRadiance.SampleLevel(LinearClamp, OctahedralEncode(direction) * 0.5 + 0.5, 0).rgb;
    }
    return EvaluateSkyIrradiance(direction) / PI;
}

// ---- Global distance field (as GlobalDistanceField.hlsl) ----------------------------------------------------------------------------------
float SampleGlobalDistance(float3 position, out float voxelSize)
{
    uint levelCount = Clipmap.x;
    [loop] for (uint level = 0; level < levelCount; level++)
    {
        float3 grid = (position - ClipmapOrigin[level].xyz) / ClipmapScale[level].x;
        float resolution = ClipmapScale[level].z;
        if (all(grid >= 1.0) && all(grid <= resolution - 1.0))
        {
            voxelSize = ClipmapScale[level].x;
            float3 uvw = (ClipmapTexel[level].xyz + grid) * ClipmapScale[level].w;
            return ClipmapLevels[level].SampleLevel(LinearWrap, uvw, 0);
        }
    }
    voxelSize = ClipmapScale[levelCount - 1].x;
    return ClipmapScale[levelCount - 1].y;
}
float3 GlobalNormal(float3 position, float voxelSize)
{
    float h = voxelSize * 0.5;
    float unused;
    float3 gradient = float3(
        SampleGlobalDistance(position + float3(h, 0.0, 0.0), unused) - SampleGlobalDistance(position - float3(h, 0.0, 0.0), unused),
        SampleGlobalDistance(position + float3(0.0, h, 0.0), unused) - SampleGlobalDistance(position - float3(0.0, h, 0.0), unused),
        SampleGlobalDistance(position + float3(0.0, 0.0, h), unused) - SampleGlobalDistance(position - float3(0.0, 0.0, h), unused));
    float lengthSquared = dot(gradient, gradient);
    return lengthSquared > 1e-12 ? gradient * rsqrt(lengthSquared) : float3(0.0, 1.0, 0.0);
}
// Sphere tracing from tMin to tMax: x = distance to the hit (or tMax), y = voxel size at the hit, z = 1 on a hit. A hit is half a voxel from the
// surface: a wall thinner than a voxel never reads much below that between the voxel centres around it; TraceSurface confirms the candidate on
// the object's own mesh distance field.
float4 TraceGlobal(float3 origin, float3 direction, float tMin, float tMax, uint maximumSteps)
{
    float t = tMin;
    [loop] for (uint stepIndex = 0; stepIndex < maximumSteps; stepIndex++)
    {
        float voxelSize;
        float fieldDistance = SampleGlobalDistance(origin + direction * t, voxelSize);
        if (fieldDistance < voxelSize * 0.5)
        {
            return float4(t, voxelSize, 1.0, 0.0);
        }
        t += max(fieldDistance, voxelSize * 0.25);
        if (t >= tMax)
        {
            break;
        }
    }
    return float4(tMax, 0.0, 0.0, 0.0);
}

// ---- Mesh distance fields (as GlobalDistanceField.hlsl) ----------------------------------------------------------------------------------
float LoadMeshDistance(MeshRecord mesh, uint3 voxel)
{
    uint index = mesh.DataOffset + (voxel.z * mesh.Resolution.y + voxel.y) * mesh.Resolution.x + voxel.x;
    uint word = MeshDistances.Load((index >> 1) << 2);
    return f16tof32((index & 1u) != 0u ? (word >> 16) : (word & 0xffffu));
}
float MeshDistance(MeshRecord mesh, float3 local)
{
    float3 boundsMax = mesh.BoundsMin + float3(mesh.Resolution) * mesh.VoxelSize;
    float3 inside = clamp(local, mesh.BoundsMin, boundsMax);
    float outside = length(local - inside);
    float3 grid = clamp((inside - mesh.BoundsMin) / mesh.VoxelSize - 0.5, 0.0, float3(mesh.Resolution) - 1.0);
    float3 cell = floor(grid);
    uint3 g0 = (uint3)cell;
    uint3 g1 = min(g0 + 1u, mesh.Resolution - 1u);
    float3 f = grid - cell;
    float d00 = lerp(LoadMeshDistance(mesh, uint3(g0.x, g0.y, g0.z)), LoadMeshDistance(mesh, uint3(g1.x, g0.y, g0.z)), f.x);
    float d10 = lerp(LoadMeshDistance(mesh, uint3(g0.x, g1.y, g0.z)), LoadMeshDistance(mesh, uint3(g1.x, g1.y, g0.z)), f.x);
    float d01 = lerp(LoadMeshDistance(mesh, uint3(g0.x, g0.y, g1.z)), LoadMeshDistance(mesh, uint3(g1.x, g0.y, g1.z)), f.x);
    float d11 = lerp(LoadMeshDistance(mesh, uint3(g0.x, g1.y, g1.z)), LoadMeshDistance(mesh, uint3(g1.x, g1.y, g1.z)), f.x);
    return lerp(lerp(d00, d10, f.y), lerp(d01, d11, f.y), f.z) * mesh.VoxelSize + outside;
}
float3 MeshNormal(MeshRecord mesh, float3 local)
{
    float h = mesh.VoxelSize * 0.5;
    float3 gradient = float3(
        MeshDistance(mesh, local + float3(h, 0.0, 0.0)) - MeshDistance(mesh, local - float3(h, 0.0, 0.0)),
        MeshDistance(mesh, local + float3(0.0, h, 0.0)) - MeshDistance(mesh, local - float3(0.0, h, 0.0)),
        MeshDistance(mesh, local + float3(0.0, 0.0, h)) - MeshDistance(mesh, local - float3(0.0, 0.0, h)));
    float lengthSquared = dot(gradient, gradient);
    return lengthSquared > 1e-12 ? gradient * rsqrt(lengthSquared) : float3(0.0, 1.0, 0.0);
}

// ---- Objects and their cards --------------------------------------------------------------------------------------------------------------
// Card f of an object looks at its box from the side of axis CardAxis[f]; (CardU, CardV) span the card and CardU x CardV = CardAxis.
static const float3 CardAxis[6] = { float3(1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0), float3(0.0, 1.0, 0.0), float3(0.0, -1.0, 0.0), float3(0.0, 0.0, 1.0), float3(0.0, 0.0, -1.0) };
static const float3 CardU[6] = { float3(0.0, 0.0, -1.0), float3(0.0, 0.0, 1.0), float3(1.0, 0.0, 0.0), float3(1.0, 0.0, 0.0), float3(1.0, 0.0, 0.0), float3(-1.0, 0.0, 0.0) };
static const float3 CardV[6] = { float3(0.0, 1.0, 0.0), float3(0.0, 1.0, 0.0), float3(0.0, 0.0, -1.0), float3(0.0, 0.0, 1.0), float3(0.0, 1.0, 0.0), float3(0.0, 1.0, 0.0) };
float3 ToLocal(LumenObject item, float3 p)
{
    return float3(dot(item.WorldToLocal0.xyz, p) + item.WorldToLocal0.w, dot(item.WorldToLocal1.xyz, p) + item.WorldToLocal1.w, dot(item.WorldToLocal2.xyz, p) + item.WorldToLocal2.w);
}
float3 ToLocalDirection(LumenObject item, float3 d)
{
    return float3(dot(item.WorldToLocal0.xyz, d), dot(item.WorldToLocal1.xyz, d), dot(item.WorldToLocal2.xyz, d));
}
float3 ToWorld(LumenObject item, float3 q)
{
    return float3(dot(item.LocalToWorld0.xyz, q) + item.LocalToWorld0.w, dot(item.LocalToWorld1.xyz, q) + item.LocalToWorld1.w, dot(item.LocalToWorld2.xyz, q) + item.LocalToWorld2.w);
}
// A normal goes from the mesh's space to the world through the inverse transpose: the rows of WorldToLocal weighted by its components.
float3 ToWorldNormal(LumenObject item, float3 n)
{
    return normalize(item.WorldToLocal0.xyz * n.x + item.WorldToLocal1.xyz * n.y + item.WorldToLocal2.xyz * n.z);
}
float3 ToLocalNormal(LumenObject item, float3 n)
{
    return normalize(item.LocalToWorld0.xyz * n.x + item.LocalToWorld1.xyz * n.y + item.LocalToWorld2.xyz * n.z);
}
// Metres per unit of the mesh's space along a direction of that space (the object's scale that way).
float WorldScale(LumenObject item, float3 axis)
{
    return length(float3(dot(item.LocalToWorld0.xyz, axis), dot(item.LocalToWorld1.xyz, axis), dot(item.LocalToWorld2.xyz, axis)));
}
// Where a point of the mesh's space falls on card f: xy in [-1, 1] across the card (y up), z the depth in [0, 1] from the card's face.
float3 CardCoordinates(LumenObject item, uint face, float3 q, out float depthExtent)
{
    float3 centre = (item.BoundsMin.xyz + item.BoundsMax.xyz) * 0.5;
    float3 halfSize = max((item.BoundsMax.xyz - item.BoundsMin.xyz) * 0.5, 1e-5);
    float hu = dot(abs(CardU[face]), halfSize);
    float hv = dot(abs(CardV[face]), halfSize);
    float hn = dot(abs(CardAxis[face]), halfSize);
    float3 offset = q - centre;
    depthExtent = 2.0 * hn;
    return float3(dot(offset, CardU[face]) / hu, dot(offset, CardV[face]) / hv, (hn - dot(offset, CardAxis[face])) / (2.0 * hn));
}
float3 CardPoint(LumenObject item, uint face, float2 clip, float depth)
{
    float3 centre = (item.BoundsMin.xyz + item.BoundsMax.xyz) * 0.5;
    float3 halfSize = max((item.BoundsMax.xyz - item.BoundsMin.xyz) * 0.5, 1e-5);
    float hu = dot(abs(CardU[face]), halfSize);
    float hv = dot(abs(CardV[face]), halfSize);
    float hn = dot(abs(CardAxis[face]), halfSize);
    return centre + CardU[face] * (clip.x * hu) + CardV[face] * (clip.y * hv) + CardAxis[face] * (hn - depth * 2.0 * hn);
}
// The surface cache's radiance at a point of the mesh's space with the given normal: the cards facing it, each kept only where its captured
// depth is that point's (a card does not see the parts its own mesh hides). w is the summed weight (0 = no card saw it).
float4 SampleCards(LumenObject item, float3 q, float3 localNormal)
{
    float3 total = 0.0;
    float weightSum = 0.0;
    [loop] for (uint face = 0; face < 6; face++)
    {
        if ((item.Cards.y & (1u << face)) == 0u)
        {
            continue;
        }
        float facing = dot(localNormal, CardAxis[face]);
        if (facing <= 0.1)
        {
            continue;
        }
        float depthExtent;
        float3 coordinates = CardCoordinates(item, face, q, depthExtent);
        if (any(abs(coordinates.xy) > 1.02))
        {
            continue;
        }
        uint4 card = Cards[item.Cards.x + face];
        float2 size = float2(card.zw);
        float2 texel = (float2)card.xy + float2(coordinates.x * 0.5 + 0.5, 0.5 - coordinates.y * 0.5) * size;
        texel = clamp(texel, (float2)card.xy + 0.5, (float2)card.xy + size - 0.5);
        int3 nearest = int3(int2(texel), 0);
        if (CardAlbedo.Load(nearest).a < 0.5)
        {
            continue;
        }
        // The captured depth must be this point's, within three texels (in metres) and 2 % of the box's depth.
        float3 halfSize = (item.BoundsMax.xyz - item.BoundsMin.xyz) * 0.5;
        float texelMeters = max(2.0 * dot(abs(CardU[face]), halfSize) * WorldScale(item, CardU[face]) / size.x,
            2.0 * dot(abs(CardV[face]), halfSize) * WorldScale(item, CardV[face]) / size.y);
        float depthMeters = depthExtent * WorldScale(item, CardAxis[face]);
        float tolerance = max(0.15, 3.0 * texelMeters) + 0.02 * depthMeters;
        if (abs(CardDepth.Load(nearest) - coordinates.z) * depthMeters > tolerance)
        {
            continue;
        }
        float weight = facing * facing;
        total += CardRadiance.SampleLevel(LinearClamp, texel * Surface.y, 0).rgb * weight;
        weightSum += weight;
    }
    return weightSum > 0.0 ? float4(total / weightSum, weightSum) : float4(0.0, 0.0, 0.0, 0.0);
}
// The object whose surface is nearest a render-space point (from the object grid's column), and the point in its space.
bool FindObject(float3 p, float accept, out uint found, out float3 foundLocal)
{
    found = 0u;
    foundLocal = 0.0;
    int2 cell = int2(floor((p.xz - GridOrigin.xz) / GridOrigin.w));
    if (any(cell < 0) || any(cell >= (int)Grid.x))
    {
        return false;
    }
    uint2 range = GridCells[(uint)cell.y * Grid.x + (uint)cell.x];
    float best = accept;
    bool hasFound = false;
    [loop] for (uint i = 0; i < min(range.y, MaximumCellObjects); i++)
    {
        uint index = GridObjects[range.x + i];
        LumenObject item = Objects[index];
        float3 q = ToLocal(item, p);
        float scale = item.BoundsMax.w;
        float3 margin = (accept / max(scale, 1e-4)) + (item.BoundsMax.xyz - item.BoundsMin.xyz) * 0.02;
        if (any(q < item.BoundsMin.xyz - margin) || any(q > item.BoundsMax.xyz + margin))
        {
            continue;
        }
        float d = abs(MeshDistance(Meshes[asuint(item.BoundsMin.w)], q)) * scale;
        if (d < best)
        {
            best = d;
            found = index;
            foundLocal = q;
            hasFound = true;
        }
    }
    return hasFound;
}
struct SurfaceHit
{
    float Distance;     // along the ray (the trace's end on a miss)
    float VoxelSize;    // the global field's voxel where it hit
    bool Hit;
    bool HasObject;     // a surface cache object is there (else a mesh the cache does not hold)
    uint Object;
    float3 Local;       // the hit in the object's space
};
// Sphere tracing through the global distance field. Each candidate it finds (half a voxel from a surface) is confirmed on the nearest object's own
// mesh distance field, which is much finer: a ray that only passes near a surface goes on past it, and a wall thinner than a voxel is still hit.
// A candidate with no surface cache object there (a mesh beyond the cache's view distance) is a hit without cards.
SurfaceHit TraceSurface(float3 origin, float3 direction, float tMin, float tMax, uint maximumSteps)
{
    SurfaceHit result;
    result.Distance = tMax;
    result.VoxelSize = 0.0;
    result.Hit = false;
    result.HasObject = false;
    result.Object = 0u;
    result.Local = 0.0;
    float t = tMin;
    [loop] for (uint attempt = 0; attempt < 4; attempt++)
    {
        float4 candidate = TraceGlobal(origin, direction, t, tMax, maximumSteps);
        if (candidate.z <= 0.5)
        {
            return result;
        }
        uint index;
        float3 q;
        if (!FindObject(origin + direction * candidate.x, max(candidate.y * 1.5, 0.3), index, q))
        {
            result.Distance = candidate.x;
            result.VoxelSize = candidate.y;
            result.Hit = true;
            return result;
        }
        LumenObject item = Objects[index];
        MeshRecord mesh = Meshes[asuint(item.BoundsMin.w)];
        float3 localStep = ToLocalDirection(item, direction);
        float localPerWorld = max(length(localStep), 1e-6);
        float3 localDirection = localStep / localPerWorld;
        float travelled = 0.0;
        float d = MeshDistance(mesh, q);
        [loop] for (uint refine = 0; refine < 8 && abs(d) > mesh.VoxelSize * 0.25; refine++)
        {
            q += localDirection * d;
            travelled += d;
            d = MeshDistance(mesh, q);
        }
        if (abs(d) * item.BoundsMax.w <= max(0.03, mesh.VoxelSize * item.BoundsMax.w))
        {
            result.Distance = candidate.x + travelled / localPerWorld;
            result.VoxelSize = candidate.y;
            result.Hit = true;
            result.HasObject = true;
            result.Object = index;
            result.Local = q;
            return result;
        }
        t = candidate.x + candidate.y * 0.5;
        if (t >= tMax)
        {
            return result;
        }
    }
    return result;
}
// The light leaving a surface a ray hit: the hit object's cards. found is false when no card saw the point (or no object holds it).
float3 SurfaceRadiance(SurfaceHit hit, out bool found)
{
    found = false;
    if (!hit.HasObject)
    {
        return 0.0;
    }
    LumenObject item = Objects[hit.Object];
    float4 cards = SampleCards(item, hit.Local, MeshNormal(Meshes[asuint(item.BoundsMin.w)], hit.Local));
    found = cards.a > 0.0;
    return cards.rgb;
}

// ---- Radiance cache --------------------------------------------------------------------------------------------------------------------
uint CacheProbe(uint level, int3 local)
{
    uint resolution = Cache.y;
    uint3 slot = ((uint3)local + CacheSlot[level].xyz) % resolution;
    return CacheSlot[level].w + (slot.z * resolution + slot.y) * resolution + slot.x;
}
float2 CacheAtlasUv(uint probe, float3 direction)
{
    uint row = Cache.z;
    float2 origin = float2((float)(probe % row), (float)(probe / row)) * (float)CacheTile;
    float2 texel = origin + 1.0 + (OctahedralEncode(direction) * 0.5 + 0.5) * (float)OctahedronTexels;
    uint width;
    uint height;
    CacheRadiance.GetDimensions(width, height);
    return texel / float2(width, height);
}
// Trilinear interpolation of the finest level whose probes surround p. Mode 0 = irradiance toward n, 1 = radiance along n. w = 1 when found.
float4 CacheLookup(float3 p, float3 n, uint mode)
{
    [loop] for (uint level = 0; level < Cache.x; level++)
    {
        float spacing = CacheScale[level].x;
        float3 grid = (p - CacheOrigin[level].xyz) / spacing;
        float last = CacheScale[level].y - 1.0;
        if (any(grid < 0.0) || any(grid > last))
        {
            continue;
        }
        float3 base = min(floor(grid), last - 1.0);
        float3 alpha = saturate(grid - base);
        float3 total = 0.0;
        float weightSum = 0.0;
        [unroll] for (uint corner = 0; corner < 8; corner++)
        {
            int3 offset = int3(corner & 1u, (corner >> 1) & 1u, (corner >> 2) & 1u);
            int3 local = int3(base) + offset;
            uint probe = CacheProbe(level, local);
            if (CacheValid[probe] == 0u || CacheIrradiance[probe * 9].w <= 0.0)
            {
                continue;
            }
            float3 trilinear = lerp(1.0 - alpha, alpha, (float3)offset);
            float weight = trilinear.x * trilinear.y * trilinear.z;
            float3 probePosition = CacheOrigin[level].xyz + (float3)local * spacing;
            float3 towardProbe = probePosition - p;
            float towardLength = length(towardProbe);
            if (mode == 0u && towardLength > 1e-4)
            {
                float wrap = (dot(towardProbe / towardLength, n) + 1.0) * 0.5;
                weight *= wrap * wrap + 0.05;
            }
            float3 value;
            if (mode == 0u)
            {
                float basis[9];
                ShBasis(n, basis);
                value = 0.0;
                [unroll] for (uint i = 0; i < 9; i++)
                {
                    value += CacheIrradiance[probe * 9 + i].rgb * basis[i];
                }
                value = max(value, 0.0);
            }
            else
            {
                value = CacheRadiance.SampleLevel(LinearClamp, CacheAtlasUv(probe, n), 0).rgb;
            }
            total += value * weight;
            weightSum += weight;
        }
        if (weightSum > 1e-4)
        {
            return float4(total / weightSum, 1.0);
        }
    }
    return float4(0.0, 0.0, 0.0, 0.0);
}

// ---- Screen ------------------------------------------------------------------------------------------------------------------------------
float3 PixelRay(float2 pixel)
{
    float2 ndc = float2((pixel.x + 0.5) / (float)Screen.x * 2.0 - 1.0, 1.0 - (pixel.y + 0.5) / (float)Screen.y * 2.0);
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    return normalize(farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w);
}
float3 SurfacePosition(uint2 pixel, float distance)
{
    return Camera.xyz + PixelRay((float2)pixel) * distance;
}
float HistoryExposure()
{
    return (Pass.w & FlagAutoExposure) != 0u ? HistoryExposureState[0].y : Camera.w;
}
// Last frame's lit colour (exposure removed) where a render-space point was, or w = 0 off screen or without history.
float4 HistoryColor(float3 p)
{
    if ((Pass.w & FlagSceneColorHistory) == 0u)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float4 clip = mul(float4(p, 1.0), PreviousViewProjection);
    if (clip.w <= 1e-4)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = float2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float exposure = HistoryExposure();
    float3 color = SceneColorHistory.SampleLevel(LinearClamp, uv, 0).rgb;
    // A pixel the history could not hold (half-float overflow while the exposure adapts) is no sample.
    if (!(exposure > 0.0) || any(!isfinite(color)) || any(color >= 65000.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(Sanitize(color / exposure, MaximumRadiance), 1.0);
}
// How far a ray point is behind the surface the screen shows where the point projects: x = that depth (negative in front), y = the surface's
// distance (0 = the sky). z = 0 when the point is off the screen or behind the camera.
float3 ScreenDepth(float3 q)
{
    float4 clip = mul(float4(q, 1.0), CameraViewProjection);
    if (clip.w <= 1e-4)
    {
        return float3(0.0, 0.0, 0.0);
    }
    float2 uv = float2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return float3(0.0, 0.0, 0.0);
    }
    float sceneDistance = SurfaceDistance.Load(int3(int2(uv * (float2)Screen.xy), 0));
    return float3(length(q - Camera.xyz) - sceneDistance, sceneDistance, 1.0);
}
// March the depth of this frame's surface prepass along a ray. When a step goes from in front of the screen's surface to behind it, the crossing is
// bisected; it is a hit only if the ray there is just behind the surface (a ray passing behind an object is not one).
bool ScreenTrace(float3 origin, float3 direction, float maximumDistance, uint steps, float jitter, out float3 radiance)
{
    radiance = 0.0;
    float previous = 0.0;
    [loop] for (uint i = 0; i < steps; i++)
    {
        float fraction = ((float)i + jitter) / (float)steps;
        float t = maximumDistance * fraction * fraction + 0.02;
        float3 depth = ScreenDepth(origin + direction * t);
        if (depth.z <= 0.0)
        {
            return false;
        }
        if (depth.y > 0.0 && depth.x > 0.0)
        {
            float low = previous;
            float high = t;
            [unroll] for (uint refine = 0; refine < 6; refine++)
            {
                float middle = (low + high) * 0.5;
                float3 middleDepth = ScreenDepth(origin + direction * middle);
                if (middleDepth.y > 0.0 && middleDepth.x > 0.0)
                {
                    high = middle;
                }
                else
                {
                    low = middle;
                }
            }
            float3 crossing = ScreenDepth(origin + direction * high);
            if (crossing.y > 0.0 && crossing.x < 0.05 + 0.01 * crossing.y + 0.5 * (high - low))
            {
                float4 history = HistoryColor(origin + direction * high);
                if (history.w <= 0.0)
                {
                    return false;
                }
                radiance = history.rgb;
                return true;
            }
        }
        previous = t;
    }
    return false;
}

// ---- Direct light on the surface cache ----------------------------------------------------------------------------------------------------
float ComputeAtlasShadowProjected(float3 projected, float2 tileUv, float4 uvScaleOffset, float shadowBias)
{
    float2 atlasUv = tileUv * uvScaleOffset.xy + uvScaleOffset.zw;
    float bias = max(shadowBias, 0.0);
    float2 texel = 1.0 / 4096.0;
    float2 tileMin = uvScaleOffset.zw + texel * 0.5;
    float2 tileMax = uvScaleOffset.zw + uvScaleOffset.xy - texel * 0.5;
    float visibility = 0.0;
    [unroll] for (int x = -1; x <= 1; x++)
    {
        [unroll] for (int y = -1; y <= 1; y++)
        {
            visibility += ShadowAtlas.SampleCmpLevelZero(ShadowSampler, clamp(atlasUv + float2(x, y) * texel, tileMin, tileMax), projected.z - bias);
        }
    }
    return visibility / 9.0;
}
// The sun's visibility from the cascades (as the Stage program), or -1 off them.
float CascadedShadow(float3 worldPosition, float3 normal, float biasMeters)
{
    [unroll] for (int cascade = 0; cascade < 4; cascade++)
    {
        float4 sphere = CascadeSpheres[cascade];
        float3 offset = worldPosition - sphere.xyz;
        if (dot(offset, offset) < sphere.w * sphere.w * 0.96)
        {
            float texelWorld = 2.0 * sphere.w / 512.0;
            float4 shadowPosition = mul(float4(worldPosition + normal * (texelWorld * 1.5), 1.0), CascadeViewProjection[cascade]);
            float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
            float2 tileUv = projected.xy * float2(0.5, -0.5) + 0.5;
            if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
            {
                return -1.0;
            }
            float depthBias = (max(biasMeters, 0.0) + texelWorld) / CascadeDepthRanges[cascade];
            return ComputeAtlasShadowProjected(projected, tileUv, CascadeUvScaleOffsets[cascade], depthBias);
        }
    }
    return -1.0;
}
float SunVisibility(float3 p, float3 n, float3 l, float biasMeters)
{
    float cascaded = CascadedShadow(p, n, biasMeters);
    if (cascaded >= 0.0)
    {
        return cascaded;
    }
    float voxelSize;
    SampleGlobalDistance(p, voxelSize);
    float4 hit = TraceGlobal(p + n * (voxelSize * 1.5), l, voxelSize * 0.5, 200.0, 64);
    return hit.z > 0.5 ? 0.0 : 1.0;
}
float ProjectedShadow(StructuredBuffer<ShadowProjectionData> projections, int index, float3 p, float bias)
{
    ShadowProjectionData projection = projections[index];
    float4 shadowPosition = mul(float4(p, 1.0), projection.ViewProjection);
    float3 projected = shadowPosition.xyz / max(shadowPosition.w, 1e-5);
    float2 tileUv = projected.xy * float2(0.5, -0.5) + 0.5;
    if (any(tileUv <= 0.0) || any(tileUv >= 1.0) || projected.z <= 0.0 || projected.z >= 1.0)
    {
        return 1.0;
    }
    return ComputeAtlasShadowProjected(projected, tileUv, projection.UvScaleOffset, bias);
}
float DistanceAttenuation(float distance, float range)
{
    float distSq = distance * distance;
    float rangeSq = max(range * range, 1e-4);
    float windowed = saturate(1.0 - distSq * distSq / (rangeSq * rangeSq));
    return windowed * windowed / max(distSq, 1e-4);
}
// Irradiance on a surface point from every light the frame has (Lambert; the cards keep no view-dependent light).
float3 DirectIrradiance(float3 p, float3 n)
{
    float3 irradiance = 0.0;
    for (int k = 0; k < LightCounts.z; k++)
    {
        DirectionalLightData light = DirectionalLights[k];
        float3 l = normalize(light.DirectionAndShadowBias.xyz);
        float nDotL = saturate(dot(n, l));
        if (nDotL <= 0.0)
        {
            continue;
        }
        float visibility = LightCounts.y == k ? SunVisibility(p, n, l, light.DirectionAndShadowBias.w) : 1.0;
        irradiance += light.ColorTimesIntensity.xyz * nDotL * visibility;
    }
    uint localCount = (uint)min(max(LightCounts.x, 0), 128);
    [loop] for (uint i = 0; i < localCount; i++)
    {
        LocalLightData light = LocalLights[i];
        float3 toLight = light.PositionAndRange.xyz - p;
        float distance = length(toLight);
        float range = light.PositionAndRange.w;
        if (distance >= range || distance <= 1e-5)
        {
            continue;
        }
        float3 l = toLight / distance;
        float nDotL = saturate(dot(n, l));
        if (nDotL <= 0.0)
        {
            continue;
        }
        float type = light.TypeAndPadding.x;
        if (type >= 1.5)
        {
            float sourceCosine = dot(light.DirectionAndInnerCos.xyz, -l);
            if (sourceCosine <= 0.0)
            {
                continue;
            }
            float area = 4.0 * max(light.RightAndHalfWidth.w, 0.0) * max(light.UpAndHalfHeight.w, 0.0);
            irradiance += light.ColorAndOuterCos.xyz * nDotL * min(PI, area * sourceCosine / max(distance * distance, 1e-4));
            continue;
        }
        float shadow = 1.0;
        float cone = 1.0;
        int projection = (int)light.TypeAndPadding.y;
        if (type < 0.5)
        {
            if (projection >= 0)
            {
                float3 toReceiver = -toLight;
                float3 a = abs(toReceiver);
                int face = a.x >= a.y && a.x >= a.z ? (toReceiver.x >= 0.0 ? 0 : 1) : (a.y >= a.z ? (toReceiver.y >= 0.0 ? 2 : 3) : (toReceiver.z >= 0.0 ? 4 : 5));
                if (projection + face < 6)
                {
                    shadow = ProjectedShadow(PointShadowProjections, projection + face, p, light.TypeAndPadding.z);
                }
            }
        }
        else
        {
            float cosAngle = dot(-l, light.DirectionAndInnerCos.xyz);
            cone = saturate((cosAngle - light.ColorAndOuterCos.w) / max(light.DirectionAndInnerCos.w - light.ColorAndOuterCos.w, 1e-4));
            cone *= cone;
            if (cone <= 0.0)
            {
                continue;
            }
            if (projection >= 0 && projection < LightCounts.w)
            {
                shadow = ProjectedShadow(SpotShadowProjections, projection, p, light.TypeAndPadding.z);
            }
        }
        irradiance += light.ColorAndOuterCos.xyz * nDotL * DistanceAttenuation(distance, range) * cone * shadow;
    }
    return irradiance;
}

// ---- Passes ------------------------------------------------------------------------------------------------------------------------------
groupshared float3 SharedRadiance[64];
groupshared float SharedWeight[64];

// Projects the 64 directions held in SharedRadiance (one per thread, octahedral) to irradiance SH; thread c < 9 returns coefficient c.
float3 ProjectSharedToIrradiance(uint coefficient)
{
    float3 total = 0.0;
    float weightSum = 0.0;
    for (uint t = 0; t < 64; t++)
    {
        uint2 texel = uint2(t % OctahedronTexels, t / OctahedronTexels);
        float basis[9];
        ShBasis(TexelDirection(texel, float2(0.5, 0.5), (float)OctahedronTexels), basis);
        float weight = SharedWeight[t];
        total += SharedRadiance[t] * basis[coefficient] * weight;
        weightSum += weight;
    }
    return total * (4.0 * PI / max(weightSum, 1e-6)) * ShBandScale[coefficient];
}

void CardLighting(uint3 groupId, uint3 groupThread)
{
    uint4 item = WorkItems[groupId.x];
    uint4 card = Cards[item.x];
    uint2 inCard = uint2(item.y & 0xffffu, item.y >> 16) * 8u + groupThread.xy;
    if (inCard.x >= card.z || inCard.y >= card.w)
    {
        return;
    }
    uint2 texel = card.xy + inCard;
    float4 albedo = CardAlbedo.Load(int3(texel, 0));
    if (albedo.a < 0.5)
    {
        CardRadianceOut[texel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    LumenObject owner = Objects[item.z];
    uint face = item.w;
    float2 clip = float2(((float)inCard.x + 0.5) / (float)card.z * 2.0 - 1.0, 1.0 - ((float)inCard.y + 0.5) / (float)card.w * 2.0);
    float3 q = CardPoint(owner, face, clip, CardDepth.Load(int3(texel, 0)));
    float3 p = ToWorld(owner, q);
    float3 n = ToWorldNormal(owner, CardNormal.Load(int3(texel, 0)).xyz * 2.0 - 1.0);
    float3 direct = DirectIrradiance(p, n);
    float4 indirect = CacheLookup(p + n * 0.1, n, 0u);
    float3 bounce = indirect.w > 0.0 ? indirect.rgb : EvaluateSkyIrradiance(n);
    float3 diffuse = saturate(albedo.rgb * Surface.z);
    float3 radiance = diffuse * (direct + bounce) / PI + CardEmissive.Load(int3(texel, 0)).rgb * asfloat(owner.Cards.w);
    CardRadianceOut[texel] = float4(Sanitize(radiance, MaximumRadiance), 1.0);
}

void RadianceCacheTrace(uint3 groupId, uint3 groupThread, uint groupIndex)
{
    uint4 item = WorkItems[groupId.x];
    uint level = item.x;
    uint probe = item.y;
    int3 local = int3(item.w & 0xffu, (item.w >> 8) & 0xffu, (item.w >> 16) & 0xffu);
    float spacing = CacheScale[level].x;
    float3 p = CacheOrigin[level].xyz + (float3)local * spacing;
    float voxelSize;
    float fieldDistance = SampleGlobalDistance(p, voxelSize);
    bool active = true;
    float clearance = min(0.3 * spacing, voxelSize * 2.0);
    if (fieldDistance < clearance)
    {
        // Move a probe that sits in or against geometry out along the field's gradient (at most half a spacing); one still inside is off.
        p += GlobalNormal(p, voxelSize) * min(clearance - fieldDistance, 0.5 * spacing);
        active = SampleGlobalDistance(p, voxelSize) > 0.0;
    }
    // The cache's probes trace their texel centres every time: a probe's radiance then changes only when the scene does.
    float3 direction = TexelDirection(groupThread.xy, float2(0.5, 0.5), (float)OctahedronTexels);
    float3 radiance = 0.0;
    if (active)
    {
        SurfaceHit hit = TraceSurface(p, direction, voxelSize * 0.25, Trace.x, 96);
        if (hit.Hit)
        {
            bool found;
            radiance = SurfaceRadiance(hit, found);
            if (!found)
            {
                // A surface no card saw: a grey surface under the open sky.
                radiance = 0.18 * EvaluateSkyIrradiance(GlobalNormal(p + direction * hit.Distance, hit.VoxelSize)) / PI;
            }
        }
        else
        {
            radiance = SkyRadianceAt(direction);
        }
    }
    uint row = Cache.z;
    uint2 origin = uint2(probe % row, probe / row) * CacheTile;
    uint2 interior = origin + 1u + groupThread.xy;
    radiance = Sanitize(radiance, MaximumRadiance);
    if (item.z != 0u && active)
    {
        radiance = lerp(Sanitize(CacheRadianceOut[interior].rgb, MaximumRadiance), radiance, Temporal.z);
    }
    CacheRadianceOut[interior] = float4(radiance, 1.0);
    // The octahedral border repeats the mirrored edge, so bilinear reads stay inside the probe.
    uint last = OctahedronTexels - 1u;
    uint2 t = groupThread.xy;
    if (t.y == 0u) { CacheRadianceOut[origin + uint2(1u + last - t.x, 0u)] = float4(radiance, 1.0); }
    if (t.y == last) { CacheRadianceOut[origin + uint2(1u + last - t.x, OctahedronTexels + 1u)] = float4(radiance, 1.0); }
    if (t.x == 0u) { CacheRadianceOut[origin + uint2(0u, 1u + last - t.y)] = float4(radiance, 1.0); }
    if (t.x == last) { CacheRadianceOut[origin + uint2(OctahedronTexels + 1u, 1u + last - t.y)] = float4(radiance, 1.0); }
    if (t.x == 0u && t.y == 0u) { CacheRadianceOut[origin + uint2(OctahedronTexels + 1u, OctahedronTexels + 1u)] = float4(radiance, 1.0); }
    if (t.x == last && t.y == 0u) { CacheRadianceOut[origin + uint2(0u, OctahedronTexels + 1u)] = float4(radiance, 1.0); }
    if (t.x == 0u && t.y == last) { CacheRadianceOut[origin + uint2(OctahedronTexels + 1u, 0u)] = float4(radiance, 1.0); }
    if (t.x == last && t.y == last) { CacheRadianceOut[origin] = float4(radiance, 1.0); }
    SharedRadiance[groupIndex] = radiance;
    SharedWeight[groupIndex] = TexelSolidAngle(groupThread.xy, (float)OctahedronTexels);
    GroupMemoryBarrierWithGroupSync();
    if (groupIndex < 9u)
    {
        CacheIrradianceOut[probe * 9u + groupIndex] = float4(ProjectSharedToIrradiance(groupIndex), active ? 1.0 : 0.0);
    }
}

void ScreenProbeTrace(uint3 groupId, uint3 groupThread, uint groupIndex)
{
    uint2 probe = groupId.xy;
    uint probeIndex = probe.y * ProbeGrid.x + probe.x;
    uint2 pixel = min(probe * ProbeGrid.z + uint2(Jitter.xy), Screen.xy - 1u);
    float distance = SurfaceDistance.Load(int3(pixel, 0));
    float3 n = normalize(SurfaceNormal.Load(int3(pixel, 0)).xyz);
    bool valid = distance > 0.0;
    float3 p = SurfacePosition(pixel, distance);
    if (groupIndex == 0u)
    {
        ProbeInfoOut[probeIndex * 2u] = float4(p, valid ? 1.0 : 0.0);
        ProbeInfoOut[probeIndex * 2u + 1u] = float4(n, distance);
    }
    float3 direction = TexelDirection(groupThread.xy, Jitter.zw, (float)OctahedronTexels);
    float3 radiance = 0.0;
    if (valid)
    {
        if (dot(direction, n) <= 0.0)
        {
            // Below the surface: no trace, only so the probe's harmonics stay smooth.
            float4 cached = CacheLookup(p + n * 0.25, direction, 1u);
            radiance = cached.w > 0.0 ? cached.rgb : 0.0;
        }
        else
        {
            bool done = false;
            float3 origin = p + n * (0.01 + 0.002 * distance);
            if ((Pass.w & FlagGatherScreenTraces) != 0u)
            {
                done = ScreenTrace(origin, direction, Trace.z, 12, Random2(pixel + groupThread.xy * 37u, Pass.z).x, radiance);
            }
            if (!done)
            {
                float voxelSize;
                SampleGlobalDistance(p, voxelSize);
                float3 start = p + n * (voxelSize * 0.75);
                SurfaceHit hit = TraceSurface(start, direction, voxelSize * 0.25, Trace.y, 48);
                if (hit.Hit)
                {
                    bool found;
                    radiance = SurfaceRadiance(hit, found);
                    if (!found)
                    {
                        float4 cached = CacheLookup(start + direction * max(hit.Distance - hit.VoxelSize, 0.0), direction, 1u);
                        radiance = cached.w > 0.0 ? cached.rgb : 0.18 * EvaluateSkyIrradiance(-direction) / PI;
                    }
                }
                else
                {
                    // The near field is clear: the radiance cache at its end sees the rest of the way.
                    float3 end = start + direction * Trace.y;
                    float4 cached = CacheLookup(end, direction, 1u);
                    if (cached.w > 0.0)
                    {
                        radiance = cached.rgb;
                    }
                    else
                    {
                        SurfaceHit far = TraceSurface(end, direction, 0.0, max(Trace.x - Trace.y, 0.0), 64);
                        bool found = false;
                        if (far.Hit)
                        {
                            radiance = SurfaceRadiance(far, found);
                        }
                        if (!found)
                        {
                            radiance = far.Hit ? 0.18 * EvaluateSkyIrradiance(-direction) / PI : SkyRadianceAt(direction);
                        }
                    }
                }
            }
        }
    }
    ProbeRadianceOut[probe * OctahedronTexels + groupThread.xy] = float4(Sanitize(radiance, MaximumRadiance), valid ? 1.0 : 0.0);
}

void ScreenProbeFilter(uint3 groupId, uint3 groupThread, uint groupIndex)
{
    uint2 probe = groupId.xy;
    uint probeIndex = probe.y * ProbeGrid.x + probe.x;
    float4 centre = ProbeInfo[probeIndex * 2u];
    float4 centreNormal = ProbeInfo[probeIndex * 2u + 1u];
    float3 total = 0.0;
    float weightSum = 0.0;
    if (centre.w > 0.0)
    {
        [unroll] for (int dy = -1; dy <= 1; dy++)
        {
            [unroll] for (int dx = -1; dx <= 1; dx++)
            {
                int2 neighbour = int2(probe) + int2(dx, dy);
                if (any(neighbour < 0) || any(neighbour >= int2(ProbeGrid.xy)))
                {
                    continue;
                }
                uint neighbourIndex = (uint)neighbour.y * ProbeGrid.x + (uint)neighbour.x;
                float4 position = ProbeInfo[neighbourIndex * 2u];
                float4 normal = ProbeInfo[neighbourIndex * 2u + 1u];
                if (position.w <= 0.0)
                {
                    continue;
                }
                float weight = 1.0;
                if (dx != 0 || dy != 0)
                {
                    float plane = abs(dot(position.xyz - centre.xyz, centreNormal.xyz));
                    weight = 0.5 * saturate(1.0 - plane / (0.05 * centreNormal.w + 0.1)) * pow(saturate(dot(normal.xyz, centreNormal.xyz)), 4.0);
                }
                total += ProbeRadiance.Load(int3(neighbour * (int)OctahedronTexels + (int2)groupThread.xy, 0)).rgb * weight;
                weightSum += weight;
            }
        }
    }
    SharedRadiance[groupIndex] = weightSum > 0.0 ? total / weightSum : 0.0;
    SharedWeight[groupIndex] = TexelSolidAngle(groupThread.xy, (float)OctahedronTexels);
    GroupMemoryBarrierWithGroupSync();
    if (groupIndex < 9u)
    {
        ProbeShOut[probeIndex * 9u + groupIndex] = float4(ProjectSharedToIrradiance(groupIndex), centre.w);
    }
}

// The screen probes' irradiance toward n at a pixel: the four probes around it, weighted by how well each one's plane and normal fit. w = weight.
float4 ProbeIrradiance(uint2 pixel, float3 p, float3 n, float distance, float3 direction)
{
    float2 f = ((float2)pixel - Jitter.xy) / (float)ProbeGrid.z;
    int2 base = int2(floor(f));
    float2 alpha = saturate(f - (float2)base);
    float3 total = 0.0;
    float weightSum = 0.0;
    [unroll] for (uint corner = 0; corner < 4; corner++)
    {
        int2 offset = int2(corner & 1u, corner >> 1);
        int2 probe = clamp(base + offset, int2(0, 0), int2(ProbeGrid.xy) - 1);
        uint index = (uint)probe.y * ProbeGrid.x + (uint)probe.x;
        float4 position = ProbeInfo[index * 2u];
        if (position.w <= 0.0)
        {
            continue;
        }
        float4 normal = ProbeInfo[index * 2u + 1u];
        float2 bilinear = lerp(1.0 - alpha, alpha, (float2)offset);
        float plane = dot(position.xyz - p, n) / (0.03 * distance + 0.05);
        float weight = bilinear.x * bilinear.y * exp(-plane * plane) * (0.05 + saturate(dot(normal.xyz, n))) + 1e-5;
        float basis[9];
        ShBasis(direction, basis);
        float3 value = 0.0;
        [unroll] for (uint i = 0; i < 9; i++)
        {
            value += ProbeSh[index * 9u + i].rgb * basis[i];
        }
        total += max(value, 0.0) * weight;
        weightSum += weight;
    }
    return weightSum > 1e-3 ? float4(total / weightSum, weightSum) : float4(0.0, 0.0, 0.0, 0.0);
}
// The history at this pixel's surface last frame, or w = 0 when the surface was elsewhere (disocclusion) or off screen.
float4 Reproject(Texture2D<float4> history, float3 p, float3 n)
{
    if ((Pass.w & FlagHistory) == 0u)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float4 clip = mul(float4(p, 1.0), PreviousViewProjection);
    if (clip.w <= 1e-4)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float2 uv = float2(clip.x / clip.w * 0.5 + 0.5, 0.5 - clip.y / clip.w * 0.5);
    if (any(uv <= 0.0) || any(uv >= 1.0))
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    float expected = length(p - PreviousCamera.xyz);
    float previous = PreviousSurfaceDistance.SampleLevel(PointClamp, uv, 0);
    float3 previousNormal = PreviousSurfaceNormal.SampleLevel(PointClamp, uv, 0).xyz;
    if (abs(previous - expected) > 0.03 * expected + 0.05 || dot(previousNormal, n) < 0.8)
    {
        return float4(0.0, 0.0, 0.0, 0.0);
    }
    return float4(history.SampleLevel(LinearClamp, uv, 0).rgb, 1.0);
}

void Integrate(uint2 pixel)
{
    if (any(pixel >= Screen.xy))
    {
        return;
    }
    float distance = SurfaceDistance.Load(int3(pixel, 0));
    if (distance <= 0.0)
    {
        DiffuseOut[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    float3 n = normalize(SurfaceNormal.Load(int3(pixel, 0)).xyz);
    float3 p = SurfacePosition(pixel, distance);
    float4 probes = ProbeIrradiance(pixel, p, n, distance, n);
    float3 irradiance;
    if (probes.w > 0.0)
    {
        irradiance = probes.rgb;
    }
    else
    {
        float4 cached = CacheLookup(p + n * 0.1, n, 0u);
        irradiance = cached.w > 0.0 ? cached.rgb : EvaluateSkyIrradiance(n);
    }
    float4 history = Reproject(DiffuseHistory, p, n);
    if (history.w > 0.0)
    {
        irradiance = lerp(history.rgb, irradiance, Temporal.x);
    }
    // Unreal's Skylight Leaking: at least that fraction of the open sky's irradiance.
    irradiance = max(irradiance, Surface.w * EvaluateSkyIrradiance(n));
    DiffuseOut[pixel] = float4(Sanitize(irradiance, 1e9), distance);
}

// GGX visible normal sampling (Heitz 2018) in the tangent frame where the normal is +Z.
float3 SampleGgxVisibleNormal(float3 view, float alpha, float2 u)
{
    float3 vh = normalize(float3(alpha * view.x, alpha * view.y, view.z));
    float lengthSquared = vh.x * vh.x + vh.y * vh.y;
    float3 t1 = lengthSquared > 0.0 ? float3(-vh.y, vh.x, 0.0) * rsqrt(lengthSquared) : float3(1.0, 0.0, 0.0);
    float3 t2 = cross(vh, t1);
    float r = sqrt(u.x);
    float phi = 2.0 * PI * u.y;
    float a = r * cos(phi);
    float b = r * sin(phi);
    float s = 0.5 * (1.0 + vh.z);
    b = (1.0 - s) * sqrt(saturate(1.0 - a * a)) + s * b;
    float3 nh = a * t1 + b * t2 + sqrt(saturate(1.0 - a * a - b * b)) * vh;
    return normalize(float3(alpha * nh.x, alpha * nh.y, max(nh.z, 1e-6)));
}
// The specular light a rough lobe sees: the screen probes' irradiance around the reflection direction as an average radiance.
float3 RoughSpecular(uint2 pixel, float3 p, float3 n, float distance, float3 reflected)
{
    float4 probes = ProbeIrradiance(pixel, p, n, distance, reflected);
    if (probes.w > 0.0)
    {
        return probes.rgb / PI;
    }
    float4 cached = CacheLookup(p + n * 0.1, reflected, 0u);
    return (cached.w > 0.0 ? cached.rgb : EvaluateSkyIrradiance(reflected)) / PI;
}

void ReflectionTraceMain(uint2 tracePixel)
{
    if (any(tracePixel >= Screen.zw))
    {
        return;
    }
    uint downsample = ProbeGrid.w;
    static const uint2 phases[4] = { uint2(0, 0), uint2(1, 1), uint2(1, 0), uint2(0, 1) };
    uint2 pixel = min(tracePixel * downsample + (downsample > 1u ? phases[Pass.z & 3u] : uint2(0, 0)), Screen.xy - 1u);
    float distance = SurfaceDistance.Load(int3(pixel, 0));
    if (distance <= 0.0)
    {
        ReflectionTraceOut[tracePixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    float4 surface = SurfaceNormal.Load(int3(pixel, 0));
    float3 n = normalize(surface.xyz);
    float roughness = surface.w;
    float3 p = SurfacePosition(pixel, distance);
    float3 v = normalize(Camera.xyz - p);
    float3 mirror = reflect(-v, n);
    float maximumRoughness = Trace.w;
    float3 rough = RoughSpecular(pixel, p, n, distance, mirror);
    if (roughness >= maximumRoughness)
    {
        ReflectionTraceOut[tracePixel] = float4(rough, distance);
        return;
    }
    float3 t;
    float3 b;
    TangentFrame(n, t, b);
    float alpha = roughness * roughness;
    float3 localView = float3(dot(v, t), dot(v, b), max(dot(v, n), 1e-4));
    float3 h = SampleGgxVisibleNormal(localView, alpha, Random2(pixel, Pass.z));
    float3 halfVector = normalize(t * h.x + b * h.y + n * h.z);
    float3 direction = reflect(-v, halfVector);
    if (dot(direction, n) <= 0.0)
    {
        direction = mirror;
    }
    float3 radiance = 0.0;
    bool done = false;
    float3 origin = p + n * (0.01 + 0.002 * distance);
    if ((Pass.w & FlagReflectionScreenTraces) != 0u)
    {
        done = ScreenTrace(origin, direction, Trace.z * 4.0, 24, Random2(pixel, Pass.z + 7u).x, radiance);
    }
    if (!done)
    {
        float voxelSize;
        SampleGlobalDistance(p, voxelSize);
        float3 start = p + n * (voxelSize * 0.75);
        SurfaceHit hit = TraceSurface(start, direction, voxelSize * 0.25, Trace.x, 96);
        if (hit.Hit)
        {
            bool found;
            radiance = SurfaceRadiance(hit, found);
            if (!found)
            {
                float4 cached = CacheLookup(start + direction * max(hit.Distance - hit.VoxelSize, 0.0), direction, 1u);
                radiance = cached.w > 0.0 ? cached.rgb : 0.18 * EvaluateSkyIrradiance(-direction) / PI;
            }
        }
        else
        {
            radiance = SkyRadianceAt(direction);
        }
    }
    // Lumen fades the traced lobe into the rough one as the roughness nears the limit.
    radiance = lerp(radiance, rough, saturate((roughness - 0.75 * maximumRoughness) / max(0.25 * maximumRoughness, 1e-4)));
    ReflectionTraceOut[tracePixel] = float4(Sanitize(radiance, MaximumRadiance), distance);
}

void ReflectionResolve(uint2 pixel)
{
    if (any(pixel >= Screen.xy))
    {
        return;
    }
    float distance = SurfaceDistance.Load(int3(pixel, 0));
    if (distance <= 0.0)
    {
        ReflectionOut[pixel] = float4(0.0, 0.0, 0.0, 0.0);
        return;
    }
    float4 surface = SurfaceNormal.Load(int3(pixel, 0));
    float3 n = normalize(surface.xyz);
    float roughness = surface.w;
    float3 p = SurfacePosition(pixel, distance);
    uint downsample = ProbeGrid.w;
    // A rough surface gathers a wider footprint of traces; a mirror keeps its own.
    int radius = roughness > 0.2 ? 2 : (downsample > 1u || roughness > 0.05 ? 1 : 0);
    int2 centre = int2(pixel / downsample);
    float3 total = 0.0;
    float weightSum = 0.0;
    float3 lowest = 1e30;
    float3 highest = 0.0;
    [loop] for (int dy = -radius; dy <= radius; dy++)
    {
        [loop] for (int dx = -radius; dx <= radius; dx++)
        {
            int2 tap = clamp(centre + int2(dx, dy), int2(0, 0), int2(Screen.zw) - 1);
            float4 trace = ReflectionTrace.Load(int3(tap, 0));
            if (trace.w <= 0.0)
            {
                continue;
            }
            uint2 tapPixel = min(uint2(tap) * downsample, Screen.xy - 1u);
            float3 tapNormal = SurfaceNormal.Load(int3(tapPixel, 0)).xyz;
            float depthWeight = saturate(1.0 - abs(trace.w - distance) / (0.03 * distance + 0.05));
            float weight = depthWeight * pow(saturate(dot(tapNormal, n)), 8.0) * exp(-(float)(dx * dx + dy * dy) * 0.25);
            if (weight <= 1e-4)
            {
                continue;
            }
            total += trace.rgb * weight;
            weightSum += weight;
            lowest = min(lowest, trace.rgb);
            highest = max(highest, trace.rgb);
        }
    }
    float3 radiance;
    if (weightSum > 0.0)
    {
        radiance = total / weightSum;
    }
    else
    {
        radiance = RoughSpecular(pixel, p, n, distance, reflect(-normalize(Camera.xyz - p), n));
        lowest = radiance;
        highest = radiance;
    }
    float4 history = Reproject(ReflectionHistory, p, n);
    if (history.w > 0.0)
    {
        // Clamp the history to what this frame's traces around the pixel saw (with some slack), so a moving reflection does not smear.
        float3 slack = (highest - lowest) * 0.5 + 0.05 * radiance;
        float3 clamped = clamp(history.rgb, lowest - slack, highest + slack);
        float weight = lerp(0.6, Temporal.y, saturate(roughness / max(Trace.w, 0.01)));
        radiance = lerp(clamped, radiance, weight);
    }
    ReflectionOut[pixel] = float4(Sanitize(radiance, MaximumRadiance), 1.0);
}

// Unreal's r.Lumen.Visualize: Pass.y 1 = the diffuse irradiance on a white surface, 2 = the reflections, 3 = the surface cache where the pixel's
// surface is (black where no card saw it), all through this frame's exposure.
void Visualize(uint2 pixel)
{
    if (any(pixel >= Screen.xy))
    {
        return;
    }
    float distance = SurfaceDistance.Load(int3(pixel, 0));
    if (distance <= 0.0)
    {
        return;
    }
    float3 value = 0.0;
    if (Pass.y == 1u)
    {
        value = DiffuseHistory.Load(int3(pixel, 0)).rgb / PI;
    }
    else if (Pass.y == 2u)
    {
        value = ReflectionHistory.Load(int3(pixel, 0)).rgb;
    }
    else
    {
        float3 n = normalize(SurfaceNormal.Load(int3(pixel, 0)).xyz);
        float3 p = SurfacePosition(pixel, distance);
        uint index;
        float3 q;
        if (FindObject(p, 0.3, index, q))
        {
            LumenObject item = Objects[index];
            value = SampleCards(item, q, ToLocalNormal(item, n)).rgb;
        }
    }
    float exposure = (Pass.w & FlagAutoExposure) != 0u ? ExposureState[0].y : Temporal.w;
    VisualizeOut[pixel] = float4(value * exposure, 1.0);
}

[numthreads(8, 8, 1)]
void CSMain(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID, uint3 dispatchThreadId : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    uint pass = Pass.x;
    if (pass == PassCardLighting)
    {
        CardLighting(groupId, groupThread);
    }
    else if (pass == PassRadianceCache)
    {
        RadianceCacheTrace(groupId, groupThread, groupIndex);
    }
    else if (pass == PassScreenProbeTrace)
    {
        ScreenProbeTrace(groupId, groupThread, groupIndex);
    }
    else if (pass == PassScreenProbeFilter)
    {
        ScreenProbeFilter(groupId, groupThread, groupIndex);
    }
    else if (pass == PassIntegrate)
    {
        Integrate(dispatchThreadId.xy);
    }
    else if (pass == PassReflectionTrace)
    {
        ReflectionTraceMain(dispatchThreadId.xy);
    }
    else if (pass == PassReflectionResolve)
    {
        ReflectionResolve(dispatchThreadId.xy);
    }
    else if (pass == PassVisualize)
    {
        Visualize(dispatchThreadId.xy);
    }
}
