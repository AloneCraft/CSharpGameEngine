// Linear blend skinning（ADR-0066）。各 thread は1つの mesh の1 vertex を skinning します。rest-pose vertex は
// 最大8個の palette matrix の重み付き和（inverse bind * joint model、row-vector 規約）で変換し、mesh の skin 済み vertex buffer に書き込みます。
// その buffer は Stage、shadow、
// GI-capture と AO pass で、ほかの vertex buffer と同様に描画されます。
// Root signature（host が所有する StageSkinning）：b0 4 root constants、t0 root SRV（rest vertices）、
// t1 root SRV（influences）、t2 root SRV（palette rows）、u0 root UAV（skinned vertices）。
cbuffer SkinningCB : register(b0)
{
    uint4 Control; // x = vertex count
};
struct Vertex
{
    float3 Position;
    float3 Normal;
    float4 Tangent;
    float2 Uv;
};
// Joints：16-bit joint index を8個。Weights：16-bit unorm weight を8個（uint ごとに2つ、下位 half が先）。
struct Influence
{
    uint4 Joints;
    uint4 Weights;
};
struct PaletteMatrix
{
    float4 Row0;
    float4 Row1;
    float4 Row2;
    float4 Row3;
};
StructuredBuffer<Vertex> RestVertices : register(t0);
StructuredBuffer<Influence> Influences : register(t1);
StructuredBuffer<PaletteMatrix> Palette : register(t2);
RWStructuredBuffer<Vertex> SkinnedVertices : register(u0);

void Accumulate(uint joint, float weight, inout float4 row0, inout float4 row1, inout float4 row2, inout float4 row3)
{
    PaletteMatrix m = Palette[joint];
    row0 += m.Row0 * weight;
    row1 += m.Row1 * weight;
    row2 += m.Row2 * weight;
    row3 += m.Row3 * weight;
}

[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    uint index = id.x;
    if (index >= Control.x)
    {
        return;
    }
    Vertex rest = RestVertices[index];
    Influence influence = Influences[index];
    float4 row0 = 0.0;
    float4 row1 = 0.0;
    float4 row2 = 0.0;
    float4 row3 = 0.0;
    [unroll]
    for (uint pair = 0; pair < 4; ++pair)
    {
        uint joints = influence.Joints[pair];
        uint weights = influence.Weights[pair];
        Accumulate(joints & 0xFFFFu, (weights & 0xFFFFu) / 65535.0, row0, row1, row2, row3);
        Accumulate(joints >> 16, (weights >> 16) / 65535.0, row0, row1, row2, row3);
    }
    Vertex skinned;
    skinned.Position = rest.Position.x * row0.xyz + rest.Position.y * row1.xyz + rest.Position.z * row2.xyz + row3.xyz;
    float3 normal = rest.Normal.x * row0.xyz + rest.Normal.y * row1.xyz + rest.Normal.z * row2.xyz;
    float3 tangent = rest.Tangent.x * row0.xyz + rest.Tangent.y * row1.xyz + rest.Tangent.z * row2.xyz;
    skinned.Normal = dot(normal, normal) > 1e-12 ? normalize(normal) : rest.Normal;
    skinned.Tangent = float4(dot(tangent, tangent) > 1e-12 ? normalize(tangent) : rest.Tangent.xyz, rest.Tangent.w);
    skinned.Uv = rest.Uv;
    SkinnedVertices[index] = skinned;
}
