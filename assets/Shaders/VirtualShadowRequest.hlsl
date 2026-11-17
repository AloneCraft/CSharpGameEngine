// 仮想シャドウのページの要求(ADR-0100 決定 5、M0132 単位 2)。主パスが書いた物理距離の画像から、画素ごとに要る段とページを選び、ビットの印を付ける。
// 1. 画素の世界の位置を、カメラの位置 + 画素の向き × 距離で戻す(距離 0 は空なので何もしない)。
// 2. 隣の画素との位置の差を光の向きの平面に写した長さを、横と縦で求める(左右・上下のうち短い方。物の縁で奥の画素と比べないため)。
// 3. 足跡(短い方、細長さは 2 倍まで、下限は 距離 × 1 画素の角度 ÷ 2)から段を選ぶ。その段の窓の外か深度の範囲の外なら、粗い段へ進む。
// 4. 段とページの印を付ける。粗いページ(3 段上)は CPU の計画が足す。
// 印は段ごとに 128×128 ビット(窓の中の位置、y は窓の上が 0)。CPU が数フレーム後に読み戻し、そのフレームの窓の位置でページの番号に直す。
// ルート署名(StageVirtualShadowRequests が持つ): b0 = 定数バッファ RequestCB、t0 = 距離の画像(記述子の表)、u0 = 印。
cbuffer RequestCB : register(b0)
{
    row_major float4x4 InverseViewProjection;
    row_major float4x4 LevelProjection[16];
    float4 CameraPosition;
    float4 Screen;
}
Texture2D<float> SceneDistance : register(t0);
RWStructuredBuffer<uint> Requests : register(u0);
// ---- VirtualShadowMapPlanner.Hlsl の写し(計算シェーダーは取り込みを使えない)。文字列が完全に同じことを試験が確かめる。----
float VirtualShadowFootprint(float alongX, float alongY, float lowerBound)
{
    float shortSide = min(alongX, alongY);
    float longSide = max(alongX, alongY);
    float footprint = isfinite(longSide) ? max(shortSide, longSide * 0.5) : shortSide;
    return isfinite(footprint) ? max(footprint, lowerBound) : lowerBound;
}
int VirtualShadowLevelForFootprint(float footprintMeters)
{
    float texels = footprintMeters / (1.33 * 8.0 / 16384.0);
    if (!(texels > 1.0))
    {
        return 0;
    }
    return min((int)floor(log2(texels)), 15);
}
// ---- 写しの終わり ----
// 画素 (x, y) の世界の位置。空(距離 0)か画面の外なら false。
bool PixelPosition(int2 pixel, out float3 position, out float distance)
{
    position = 0.0;
    distance = 0.0;
    if (any(pixel < 0) || pixel.x >= (int)Screen.x || pixel.y >= (int)Screen.y)
    {
        return false;
    }
    distance = SceneDistance.Load(int3(pixel, 0));
    if (!(distance > 0.0))
    {
        return false;
    }
    float2 ndc = float2((pixel.x + 0.5) / Screen.x * 2.0 - 1.0, 1.0 - (pixel.y + 0.5) / Screen.y * 2.0);
    float4 nearPoint = mul(float4(ndc, 0.0, 1.0), InverseViewProjection);
    float4 farPoint = mul(float4(ndc, 1.0, 1.0), InverseViewProjection);
    float3 direction = normalize(farPoint.xyz / farPoint.w - nearPoint.xyz / nearPoint.w);
    position = CameraPosition.xyz + direction * distance;
    return true;
}
// 位置の差を、光の向きの平面での長さ(メートル)にする。段 0 の行列は幅 8 m を -1〜1 に写すので、1 単位は 4 m。
float LightPlaneLength(float3 difference)
{
    return length(mul(float4(difference, 0.0), LevelProjection[0]).xy) * 4.0;
}
// 画素 pixel の横か縦(step)の、隣との差の短い方(どちらの隣も無ければ無限大)。
float AlongAxis(int2 pixel, int2 step, float3 position)
{
    float result = asfloat(0x7f800000u);
    float3 neighbour;
    float neighbourDistance;
    if (PixelPosition(pixel + step, neighbour, neighbourDistance))
    {
        result = LightPlaneLength(neighbour - position);
    }
    if (PixelPosition(pixel - step, neighbour, neighbourDistance))
    {
        result = min(result, LightPlaneLength(neighbour - position));
    }
    return result;
}
[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    int2 pixel = int2(id.xy);
    float3 position;
    float distance;
    if (!PixelPosition(pixel, position, distance))
    {
        return;
    }
    // Screen.z は 1 m 先の 1 m が何画素か。1 画素の角度は その逆数。
    float lowerBound = distance / Screen.z * 0.5;
    float footprint = VirtualShadowFootprint(AlongAxis(pixel, int2(1, 0), position), AlongAxis(pixel, int2(0, 1), position), lowerBound);
    for (int level = VirtualShadowLevelForFootprint(footprint); level < 16; level++)
    {
        float4 clip = mul(float4(position, 1.0), LevelProjection[level]);
        float2 uv = float2(clip.x * 0.5 + 0.5, 0.5 - clip.y * 0.5);
        if (any(uv < 0.0) || any(uv >= 1.0) || clip.z < 0.0 || clip.z > 1.0)
        {
            continue;
        }
        uint2 page = min((uint2)(uv * 128.0), 127u);
        uint index = (uint)level * 16384u + page.y * 128u + page.x;
        InterlockedOr(Requests[index >> 5], 1u << (index & 31u));
        return;
    }
}
