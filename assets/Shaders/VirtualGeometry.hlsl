// 仮想ジオメトリの塊の選択(ADR-0100 決定 3、M0130)。物体 1 つ分の塊を 1 スレッドずつ調べ、描く塊の番号を詰めて、描く数を間接描画の引数に足す。
// 選び方: まとまりが「十分」とは、誤差の画素数がしきい値以下か、三角形の大きさの画素数が下限以下(下限が 0 なら使わない)のこと。
// 自分を作ったまとまりが十分で、自分を使って次の段を作ったまとまり(根なら無い)が十分でないなら描く。
// 親のまとまりの球は子の球を包み、誤差は子の 1.25 倍以上、三角形の大きさも子以上なので、「十分」は親で成り立てば子でも成り立つ。
// だから、どの塊も「描く」と「親が描く」が重ならず、穴も開かない(M0130 単位 6)。
// 引数の InstanceCount(4 バイト目)は、描画器が 0 にしてからこのシェーダーを動かす。
// ルート署名(描画器が持つ): b0 = ルート定数 SelectionCB、t0 = 塊、t1 = まとまり、u0 = 描く塊の番号、u1 = 間接描画の引数。
cbuffer SelectionCB : register(b0)
{
    row_major float4x4 World;
    float4 CameraAndScale;
    float4 Metric;
    uint ClusterCount;
    float ThresholdPixels;
    float TriangleFloorPixels;
    uint Reserved1;
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
RWStructuredBuffer<uint> VisibleClusters : register(u0);
RWByteAddressBuffer DrawArguments : register(u1);
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
[numthreads(64, 1, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= ClusterCount)
    {
        return;
    }
    ClusterData cluster = Clusters[id.x];
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
    VisibleClusters[slot] = id.x;
}
