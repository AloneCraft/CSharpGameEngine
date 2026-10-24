// Global distance field (ADR-0078/M0110): clipmaps of the scene's signed distance around the camera, the tracing structure of the Lumen-like
// lighting (M0111). Each clipmap level is a Texture3D of Resolution^3 voxels addressed toroidally: the voxel at global integer coordinate g lives
// in texel g mod Resolution, so a clipmap that follows the camera rewrites only the bricks it newly covers. The field is made from the mesh
// distance fields the build wrote for every glTF (geometry only; no lighting is baked) and is truncated at four voxels of its level.
// Host-owned root signature (StageGlobalDistanceField): 0 = root CBV b0, 1-4 = root SRVs t0-t3 (bricks, brick objects, objects, meshes),
// 5 = root SRV t4 (mesh distances, half floats), 6 = SRV table t5-t8 (the clipmap levels), 7 = UAV table u0 (the level a brick update writes),
// 8 = root UAV u1 (trace results), 9 = root SRV t9 (trace rays), s0 = linear wrap.
cbuffer GlobalDistanceFieldCB : register(b0)
{
    float4 Level;                 // update: x voxel size m, y truncation m, z resolution, w level index
    float4 LevelRemainder;        // update: xyz render-space offset of the level's origin voxel (the field's voxel g sits at (g - O + 0.5) s - remainder)
    uint4 Pass;                   // x pass (0 = update bricks, 1 = trace rays), y first brick, z ray count, w level count
    float4 ClipmapScale[4];       // x voxel size, y truncation, z resolution, w 1 / resolution
    float4 ClipmapOrigin[4];      // xyz render-space corner of the level's clip (its lowest voxel's corner)
    float4 ClipmapTexel[4];       // xyz texel of the clip's lowest voxel (its global coordinate mod resolution)
};

struct BrickRecord
{
    int3 VoxelOffset;   // the brick's first voxel, relative to the level's origin voxel
    uint ObjectStart;
    uint3 TexelStart;   // the brick's first voxel mod resolution
    uint ObjectCount;
};

struct ObjectRecord
{
    float4 WorldToLocal0;  // local = (dot(row0.xyz, p) + row0.w, ...): the inverse of the object's render-space matrix
    float4 WorldToLocal1;
    float4 WorldToLocal2;
    uint Mesh;
    float DistanceScale;   // the object's smallest scale: local distances times this never overstate the world distance
    float2 Padding;
};

struct MeshRecord
{
    float3 BoundsMin;
    float VoxelSize;
    uint3 Resolution;
    uint DataOffset;       // in half floats
};

StructuredBuffer<BrickRecord> Bricks : register(t0);
StructuredBuffer<uint> BrickObjects : register(t1);
StructuredBuffer<ObjectRecord> Objects : register(t2);
StructuredBuffer<MeshRecord> Meshes : register(t3);
ByteAddressBuffer MeshDistances : register(t4);
Texture3D<float> ClipmapLevels[4] : register(t5);
RWTexture3D<float> LevelOutput : register(u0);
RWStructuredBuffer<float4> TraceResults : register(u1);
StructuredBuffer<float4> TraceRays : register(t9);
SamplerState LinearWrap : register(s0);

static const uint PassUpdateBricks = 0;
static const uint PassTraceRays = 1;
static const uint BrickSize = 8;
static const uint MaximumTraceSteps = 192;

float LoadMeshDistance(MeshRecord mesh, uint3 voxel)
{
    uint index = mesh.DataOffset + (voxel.z * mesh.Resolution.y + voxel.y) * mesh.Resolution.x + voxel.x;
    uint word = MeshDistances.Load((index >> 1) << 2);
    return f16tof32((index & 1u) != 0u ? (word >> 16) : (word & 0xffffu));
}

// The mesh's distance at a point of its space: trilinear between voxel centres (clamped to the volume) plus, outside the volume, the distance to
// its box. MeshDistanceField.Sample on the CPU is the same function.
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

// The field at a render-space point from the finest level whose clip holds it (one voxel of margin for the trilinear read); the coarsest level's
// truncation when no level does. Writes the voxel size of the level used. This is what the M0111 tracing reads.
float SampleGlobalDistance(float3 position, uint levelCount, out float voxelSize)
{
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

// Sphere tracing through the field: x = distance to the hit (or maxT), y = steps, z = 1 on a hit.
float4 TraceGlobalDistanceField(float3 origin, float3 direction, float maxT, uint levelCount)
{
    float t = 0.0;
    [loop] for (uint stepIndex = 0; stepIndex < MaximumTraceSteps; stepIndex++)
    {
        float voxelSize;
        float fieldDistance = SampleGlobalDistance(origin + direction * t, levelCount, voxelSize);
        if (fieldDistance < voxelSize * 0.25)
        {
            return float4(t, (float)stepIndex, 1.0, 0.0);
        }
        t += max(fieldDistance, voxelSize * 0.25);
        if (t >= maxT)
        {
            break;
        }
    }
    return float4(maxT, (float)MaximumTraceSteps, 0.0, 0.0);
}

[numthreads(8, 8, 8)]
void CSMain(uint3 groupId : SV_GroupID, uint3 groupThread : SV_GroupThreadID, uint3 dispatchThreadId : SV_DispatchThreadID)
{
    if (Pass.x == PassTraceRays)
    {
        uint ray = dispatchThreadId.x + dispatchThreadId.y * 8 + dispatchThreadId.z * 64;
        if (groupId.x != 0 || ray >= Pass.z)
        {
            return;
        }
        float4 origin = TraceRays[ray * 2];
        float4 direction = TraceRays[ray * 2 + 1];
        TraceResults[ray] = TraceGlobalDistanceField(origin.xyz, normalize(direction.xyz), origin.w, Pass.w);
        return;
    }
    BrickRecord brick = Bricks[Pass.y + groupId.x];
    int3 voxel = brick.VoxelOffset + int3(groupThread);
    float3 position = (float3(voxel) + 0.5) * Level.x - LevelRemainder.xyz;
    float truncation = Level.y;
    float fieldDistance = truncation;
    [loop] for (uint i = 0; i < brick.ObjectCount; i++)
    {
        ObjectRecord item = Objects[BrickObjects[brick.ObjectStart + i]];
        float3 local = float3(
            dot(item.WorldToLocal0.xyz, position) + item.WorldToLocal0.w,
            dot(item.WorldToLocal1.xyz, position) + item.WorldToLocal1.w,
            dot(item.WorldToLocal2.xyz, position) + item.WorldToLocal2.w);
        fieldDistance = min(fieldDistance, MeshDistance(Meshes[item.Mesh], local) * item.DistanceScale);
    }
    uint resolution = (uint)Level.z;
    LevelOutput[(brick.TexelStart + groupThread) % resolution] = clamp(fieldDistance, -truncation, truncation);
}
