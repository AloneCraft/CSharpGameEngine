// 仮想ジオメトリの塊の選択(ADR-0100 決定 3、M0130)と、主パスの 2 パスの遮蔽(M0131 単位 2)。
// 1 回の実行は物体 1 つ分。印(Flags)で 2 つの働きを切り替える。
//
// 選ぶ(印 2 が無いとき): 塊を 1 スレッドずつ調べ、描く塊の番号を詰めて、描く数を間接描画の引数に足す。
// 選び方: まとまりが「十分」とは、誤差の画素数がしきい値以下か、三角形の大きさの画素数が下限以下(下限が 0 なら使わない)のこと。
// 自分を作ったまとまりが十分で、自分を使って次の段を作ったまとまり(根なら無い)が十分でないなら描く。
// 親のまとまりの球は子の球を包み、誤差は子の 1.25 倍以上、三角形の大きさも子以上なので、「十分」は親で成り立てば子でも成り立つ。
// だから、どの塊も「描く」と「親が描く」が重ならず、穴も開かない(M0130 単位 6)。
// 遮蔽を使うとき(印 1)は、選んだ塊のうち前のフレームで見えた印のある塊を、1 パス目の一覧にも詰める。
//
// 調べる(印 2): 選んだ一覧の塊を 1 スレッドずつ、1 パス目までの距離から作った Hi-Z で調べる。包む球のいちばん近い点が、球が画面で覆う範囲の
// いちばん遠い距離より遠ければ隠れている。範囲は球を包む箱の 8 隅を写して求め、範囲が 4×4 画素以内に収まる段の画素をすべて見る
// (2×2 だと粗い段の画素が範囲の外の空まで含み、壁の後ろの塊を見えるとしやすかった)。見えた印を書き直し、1 パス目で描かなかった見える塊を 2 パス目の一覧に詰める。
// 隠れていると捨てた塊は、描いても手前の物に負けるので、遮蔽を切った絵と同じ絵になる。画面の外の塊も捨てる(写らない)。
//
// 引数の InstanceCount(4 バイト目)は、描画器が 0 にしてからこのシェーダーを動かす。
// ルート署名(描画器が持つ): b0 = ルート定数 SelectionCB、b1 = ルート定数 OcclusionCB、t0 = 塊、t1 = まとまり、t2 = 塊の包む球、
// u0 = 描く塊の番号、u1 = 間接描画の引数、u2 = 遮蔽の一覧(見えた印、1 パス目、2 パス目が塊の数ずつ並ぶ)、u3 = 1 パス目と 2 パス目の引数、
// t3 = Hi-Z(段の重なり、記述子の表)。
cbuffer SelectionCB : register(b0)
{
    row_major float4x4 World;
    float4 CameraAndScale;
    float4 Metric;
    uint ClusterCount;
    float ThresholdPixels;
    float TriangleFloorPixels;
    uint Flags;
}
cbuffer OcclusionCB : register(b1)
{
    row_major float4x4 ViewProjection;
    uint2 ScreenSize;
    uint2 HiZSize;
    uint HiZLevelCount;
}
struct ClusterData
{
    uint FirstIndex;
    uint IndexCount;
    uint Group;
    int ParentGroup;
};
struct GroupData
{
    float3 BoundsCentre;
    float BoundsRadius;
    float Error;
    float TriangleSize;
    float2 Reserved;
};
StructuredBuffer<ClusterData> Clusters : register(t0);
StructuredBuffer<GroupData> Groups : register(t1);
StructuredBuffer<float4> ClusterBounds : register(t2);
Texture2D<float> HiZ : register(t3);
RWStructuredBuffer<uint> VisibleClusters : register(u0);
RWByteAddressBuffer DrawArguments : register(u1);
RWStructuredBuffer<uint> Occlusion : register(u2);
RWByteAddressBuffer PassArguments : register(u3);
static const uint FlagOcclusion = 1u;
static const uint FlagTest = 2u;
// ---- ScreenSizeMetric.Hlsl の写し(計算シェーダーは取り込みを使えない)。文字列が完全に同じことを ScreenSizeMetricGpuTests が確かめる。----
float ScreenSizeProjectedPixels(float sizeMeters, float distanceMeters, float4 metric)
{
    return metric.y > 0.5 ? sizeMeters * metric.x : sizeMeters * metric.x / max(distanceMeters, metric.z);
}
float ScreenSizeSphereProjectedPixels(float3 centreRelativeToCamera, float radiusMeters, float4 metric)
{
    return ScreenSizeProjectedPixels(2.0 * radiusMeters, max(length(centreRelativeToCamera) - radiusMeters, metric.z), metric);
}
// ---- 写しの終わり ----
// まとまりが十分か。誤差と三角形の大きさを画面の画素数にする。距離は包む球のいちばん近い所まで(誤差を小さく見積もらない)。
bool GroupIsEnough(uint index)
{
    GroupData group = Groups[index];
    float3 centre = mul(float4(group.BoundsCentre, 1.0), World).xyz - CameraAndScale.xyz;
    float radius = group.BoundsRadius * CameraAndScale.w;
    float distance = max(length(centre) - radius, Metric.z);
    if (ScreenSizeProjectedPixels(group.Error * CameraAndScale.w, distance, Metric) <= ThresholdPixels)
    {
        return true;
    }
    return TriangleFloorPixels > 0.0 && ScreenSizeProjectedPixels(group.TriangleSize * CameraAndScale.w, distance, Metric) <= TriangleFloorPixels;
}
void Select(uint index)
{
    ClusterData cluster = Clusters[index];
    if (!GroupIsEnough(cluster.Group))
    {
        return;
    }
    if (cluster.ParentGroup >= 0 && GroupIsEnough((uint)cluster.ParentGroup))
    {
        return;
    }
    uint slot;
    DrawArguments.InterlockedAdd(4, 1, slot);
    VisibleClusters[slot] = index;
    if ((Flags & FlagOcclusion) != 0 && Occlusion[index] != 0)
    {
        uint first;
        PassArguments.InterlockedAdd(4, 1, first);
        Occlusion[ClusterCount + first] = index;
    }
}
// 塊 index が、Hi-Z に写った物の後ろに隠れているか。画面の外も隠れているとする。球がカメラを含むか、カメラの後ろへはみ出すなら隠れていない。
bool IsOccluded(uint index)
{
    float4 bounds = ClusterBounds[index];
    float3 centre = mul(float4(bounds.xyz, 1.0), World).xyz;
    float radius = bounds.w * CameraAndScale.w;
    float nearest = length(centre - CameraAndScale.xyz) - radius;
    if (nearest <= 0.0)
    {
        return false;
    }
    float2 low = float2(1.0e30, 1.0e30);
    float2 high = float2(-1.0e30, -1.0e30);
    [unroll]
    for (uint corner = 0; corner < 8; corner++)
    {
        float3 offset = float3((corner & 1) != 0 ? radius : -radius, (corner & 2) != 0 ? radius : -radius, (corner & 4) != 0 ? radius : -radius);
        float4 clip = mul(float4(centre + offset, 1.0), ViewProjection);
        if (clip.w <= 1.0e-6)
        {
            return false;
        }
        float2 ndc = clip.xy / clip.w;
        low = min(low, ndc);
        high = max(high, ndc);
    }
    float2 screen = float2(ScreenSize);
    float2 pixelLow = float2((low.x * 0.5 + 0.5) * screen.x, (0.5 - high.y * 0.5) * screen.y);
    float2 pixelHigh = float2((high.x * 0.5 + 0.5) * screen.x, (0.5 - low.y * 0.5) * screen.y);
    if (pixelHigh.x < 0.0 || pixelHigh.y < 0.0 || pixelLow.x >= screen.x || pixelLow.y >= screen.y)
    {
        return true;
    }
    uint2 first = (uint2)clamp(pixelLow, 0.0, screen - 1.0) / 2;
    uint2 last = (uint2)clamp(pixelHigh, 0.0, screen - 1.0) / 2;
    uint level = 0;
    while (level + 1 < HiZLevelCount && ((last.x >> level) - (first.x >> level) > 3 || (last.y >> level) - (first.y >> level) > 3))
    {
        level++;
    }
    uint2 a = first >> level;
    uint2 b = last >> level;
    float farthest = 0.0;
    for (uint y = a.y; y <= b.y; y++)
    {
        for (uint x = a.x; x <= b.x; x++)
        {
            farthest = max(farthest, HiZ.Load(int3(x, y, level)));
        }
    }
    return nearest > farthest * 1.00001;
}
void Test(uint slot)
{
    if (slot >= DrawArguments.Load(4))
    {
        return;
    }
    uint index = VisibleClusters[slot];
    bool drawnInFirstPass = Occlusion[index] != 0;
    bool occluded = IsOccluded(index);
    Occlusion[index] = occluded ? 0u : 1u;
    if (!occluded && !drawnInFirstPass)
    {
        uint second;
        PassArguments.InterlockedAdd(16 + 4, 1, second);
        Occlusion[ClusterCount * 2 + second] = index;
    }
}
[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ClusterCount)
    {
        return;
    }
    if ((Flags & FlagTest) != 0)
    {
        Test(id.x);
    }
    else
    {
        Select(id.x);
    }
}
