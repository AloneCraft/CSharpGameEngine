// histogram auto exposure（ADR-0069）。Unreal の AEM_HISTOGRAM をすべて GPU 上に実装するため、CPU は
// 結果を待ったり map したりしません。ExposureState[0] =（計測した EV100、露出スケール、settled、
// 1）。Stage program と sky composite はスケールを読み取ります。各 frame で HDR target の log2 scene
// luminance（適用済みスケールを除去）を binning し、次の state に適応します。
// next frame.
// Passes（Control.z）：0 = 最小 EV100 で state を初期化、1 = bin を clear、2 = 各 pixel を binning、
// 3 = 計測して適応（thread 1つ）。
// Root signature（host が所有する StageAutoExposure）：b0 16 root constants、t0 table（HDR scene colour）、
// u0 root UAV（RWStructuredBuffer<uint> bins）、u1 root UAV（RWStructuredBuffer<float4> state）。
cbuffer HistogramCB : register(b0)
{
    uint4 Control;       // xy = HDR size、z = pass
    float4 Range;        // x = 最小 log2 luminance、y = 1 / bin width、z = 前回適応からの秒数、w = compensation（stops）
    float4 Metering;     // x = 最小 EV100、y = 最大 EV100、z = speed up、w = speed down（毎秒）
    float4 Percentiles;  // x = low、y = high：平均する pixel の割合
};
Texture2D<float4> SceneColor : register(t0);
RWStructuredBuffer<uint> Bins : register(u0);
RWStructuredBuffer<float4> State : register(u1);

static const uint BinCount = 64;
// log2(100 / 12.5)：K = 12.5 の反射光 meter で計測した輝度の EV100。
static const float Log2LuminanceToEv100 = 3.0;
groupshared uint LocalBins[BinCount];

float ExposureScale(float ev100)
{
    return exp2(Range.w) / (1.2 * exp2(ev100));
}

void Initialize()
{
    State[0] = float4(Metering.x, ExposureScale(Metering.x), 0.0, 1.0);
}

void Accumulate(uint3 dispatchThreadId, uint groupIndex)
{
    if (groupIndex < BinCount)
    {
        LocalBins[groupIndex] = 0u;
    }
    GroupMemoryBarrierWithGroupSync();
    if (all(dispatchThreadId.xy < Control.xy))
    {
        float3 color = SceneColor.Load(int3(dispatchThreadId.xy, 0)).rgb;
        float exposed = dot(color, float3(0.2126, 0.7152, 0.0722));
        float bin = (log2(max(exposed / State[0].y, 1e-10)) - Range.x) * Range.y;
        // RGBA16F の上限に達した（または infinity に overflow した）pixel は輝度の下限しか示さず、
        // 最後の bin が clipping を示します。
        uint index = exposed < 60000.0 ? (uint)clamp(bin, 0.0, (float)(BinCount - 2)) : BinCount - 1;
        InterlockedAdd(LocalBins[index], 1u);
    }
    GroupMemoryBarrierWithGroupSync();
    if (groupIndex < BinCount && LocalBins[groupIndex] != 0u)
    {
        InterlockedAdd(Bins[groupIndex], LocalBins[groupIndex]);
    }
}

void Adapt()
{
    float total = 0.0;
    for (uint bin = 0; bin < BinCount; bin++)
    {
        total += (float)Bins[bin];
    }
    if (total <= 0.0)
    {
        return;
    }
    float low = total * Percentiles.x;
    float high = total * Percentiles.y;
    float accumulated = 0.0;
    float weighted = 0.0;
    float weight = 0.0;
    for (uint index = 0; index < BinCount; index++)
    {
        float start = accumulated;
        float end = accumulated + (float)Bins[index];
        accumulated = end;
        float inside = max(0.0, min(end, high) - max(start, low));
        weighted += inside * (Range.x + ((float)index + 0.5) / Range.y);
        weight += inside;
    }
    if (weight <= 0.0)
    {
        return;
    }
    float target = clamp(weighted / weight + Log2LuminanceToEv100, Metering.x, Metering.y);
    float4 state = State[0];
    float ev100 = target;
    if (state.z > 0.5)
    {
        float speed = target > state.x ? Metering.z : Metering.w;
        ev100 = state.x + (target - state.x) * (1.0 - exp(-speed * Range.z));
    }
    // clipped pixel が破棄対象の最明部割合内に収まる histogram を得るまでは、
    // 各 target に直接ジャンプします。clipped measurement は輝度の下限しか示さないためです。
    float settled = state.z > 0.5 || (float)Bins[BinCount - 1] <= total * (1.0 - Percentiles.y) ? 1.0 : 0.0;
    State[0] = float4(ev100, ExposureScale(ev100), settled, 1.0);
}

[numthreads(16, 16, 1)]
void CSMain(uint3 dispatchThreadId : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    if (Control.z == 0u)
    {
        if (all(dispatchThreadId == 0u))
        {
            Initialize();
        }
    }
    else if (Control.z == 1u)
    {
        if (dispatchThreadId.x < BinCount && dispatchThreadId.y == 0u)
        {
            Bins[dispatchThreadId.x] = 0u;
        }
    }
    else if (Control.z == 2u)
    {
        Accumulate(dispatchThreadId, groupIndex);
    }
    else if (all(dispatchThreadId == 0u))
    {
        Adapt();
    }
}
