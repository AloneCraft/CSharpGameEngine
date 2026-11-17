// 遮蔽判定に使う Hi-Z(ADR-0100 決定 3、M0131 単位 1)。主パスが書いた物理距離(カメラからの距離、メートル)の画像を、2×2 画素ごとに
// いちばん遠い値を取って半分ずつ縮める。1 回の実行で 1 段を作る。段の大きさの決まりは HiZPyramid(C#)と同じ。
// 段 0 は距離の画像から作り、大きさは画面の半分(切り上げ)を 2 のべき乗へ切り上げたもの。段 n は段 n-1 のちょうど半分(D3D12 のミップと同じ)。
// 先の画素 (x, y) は元の 2x〜2x+1、2y〜2y+1 を覆う。元の外にはみ出した画素は 0 とし、いちばん遠い値には効かない。
// 距離の画像で何も描かれていない画素(空、値 0)は、無限に遠いとする(その後ろに物があっても隠れていない)。
// ルート署名(StageHiZ が持つ): b0 = ルート定数 HiZCB、t0 = 元の画像(距離の画像か、前の段)、u0 = 書く段。
cbuffer HiZCB : register(b0)
{
    uint2 SourceSize;
    uint2 DestinationSize;
    uint FromDistance;
}
Texture2D<float> Source : register(t0);
RWTexture2D<float> Destination : register(u0);
float LoadSource(uint2 position)
{
    if (any(position >= SourceSize))
    {
        return 0.0;
    }
    float value = Source.Load(int3(position, 0));
    // 空(0)は無限に遠い。縮めた段は既に無限を持つので、変えない。
    return FromDistance != 0 && value <= 0.0 ? asfloat(0x7f800000u) : value;
}
[numthreads(8, 8, 1)]
void CSMain(uint3 id : SV_DispatchThreadID)
{
    if (any(id.xy >= DestinationSize))
    {
        return;
    }
    uint2 first = id.xy * 2;
    float farthest = max(max(LoadSource(first), LoadSource(first + uint2(1, 0))), max(LoadSource(first + uint2(0, 1)), LoadSource(first + uint2(1, 1))));
    Destination[id.xy] = farthest;
}
