// Global distance field（ADR-0078/M0110）：カメラ周囲にあるシーンの signed distance を clipmap 化したもの。Lumen 風ライティング（M0111）で使う
// tracing 構造です。各 clipmap level は Resolution^3 voxel の Texture3D で、toroidal にアドレス指定します。global integer coordinate g の voxel は、
// texel g mod Resolution に格納されます。そのため、カメラに追従する clipmap は新たに覆う brick だけを書き換えます。field は、ビルド時に各 glTF 用に生成された mesh
// distance field（geometry のみで、ライティングはベイクしない）から作成し、その level の4 voxel の距離で打ち切ります。
// host が所有する root signature（StageGlobalDistanceField）：0 = root CBV b0、1-4 = root SRV t0-t3（bricks、brick objects、objects、meshes）、
// 5 = root SRV t4（mesh distances、half float）、6 = SRV table t5-t8（clipmap level）、7 = UAV table u0（brick update が書き込む level）、
// 8 = root UAV u1（trace 結果）、9 = root SRV t9（trace ray）、s0 = linear wrap。
cbuffer GlobalDistanceFieldCB : register(b0)
{
    float4 Level;                 // update：x = voxel size m、y = truncation m、z = resolution、w = level index
    float4 LevelRemainder;        // update：xyz = level の原点 voxel の render-space offset（field の voxel g は (g - O + 0.5) s - remainder に位置します）
    uint4 Pass;                   // x = pass（0 = brick 更新、1 = ray tracing）、y = 最初の brick、z = ray 数、w = level 数
    float4 ClipmapScale[4];       // x = voxel size、y = truncation、z = resolution、w = 1 / resolution
    float4 ClipmapOrigin[4];      // xyz = level clip の render-space corner（最下位 voxel の corner）
    float4 ClipmapTexel[4];       // xyz = clip の最下位 voxel の texel（global coordinate mod resolution）
};

struct BrickRecord
{
    int3 VoxelOffset;   // level の原点 voxel を基準とした、brick の最初の voxel
    uint ObjectStart;
    uint3 TexelStart;   // brick の最初の voxel の mod resolution
    uint ObjectCount;
};

struct ObjectRecord
{
    float4 WorldToLocal0;  // local = (dot(row0.xyz, p) + row0.w, ...)：object の render-space matrix の逆変換
    float4 WorldToLocal1;
    float4 WorldToLocal2;
    uint Mesh;
    float DistanceScale;   // object の最小スケール。この値を local distance に乗算すれば、world distance を過大評価しません。
    float2 Padding;
};

struct MeshRecord
{
    float3 BoundsMin;
    float VoxelSize;
    uint3 Resolution;
    uint DataOffset;       // half float 表現
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

// mesh 空間の点における距離。voxel 中心間を trilinear 補間し（volume 内に clamp）、volume 外では mesh の box までの距離を加えます。
// CPU 上の MeshDistanceField.Sample も同じ関数です。
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

// render-space の点における field。trilinear read 用に1 voxel の余裕を持って点を含む最も細かい level を使い、どの level にも含まれない場合は最も粗い level の
// truncation を使います。使用した level の voxel size も書き込みます。M0111 の tracing が参照する値です。
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

// field 内の sphere tracing：x = hit までの距離（または maxT）、y = step 数、z = hit 時に1。
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
