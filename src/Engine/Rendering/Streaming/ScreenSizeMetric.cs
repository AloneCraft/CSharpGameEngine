using System.Numerics;
namespace CSharpGameEngine.Rendering.Streaming;
/// <summary>
/// 画面上の大きさの式（ADR-0100 決定 1、M0129 単位 6）。物の大きさが画面で何画素になるかを求める。仮想ジオメトリの塊の誤差、地形のマス、VirtualObject の段、
/// テクスチャの細かさ、三角形の下限が、すべてこの 1 つの式を使う。シェーダーは <see cref="Hlsl"/> の同じ式を使い、CPU と GPU の結果が一致することを試験で固定する。
/// 位置はカメラから見た相対の位置（メートル）で渡す。描画空間の原点はカメラなので、遠い世界でも float の桁が足りる。
/// </summary>
public readonly record struct ScreenSizeMetric {
    private ScreenSizeMetric(float PixelsPerMeter,bool IsOrthographic,float MinimumDistanceMeters){
        this.PixelsPerMeter=PixelsPerMeter;
        this.IsOrthographic=IsOrthographic;
        this.MinimumDistanceMeters=MinimumDistanceMeters;
    }
    /// <summary>透視なら「1 メートル先にある 1 メートルの物」の画素数。正射影なら 1 メートルの画素数。</summary>
    public float PixelsPerMeter { get; }
    /// <summary>正射影なら true。距離で大きさが変わらない。</summary>
    public bool IsOrthographic { get; }
    /// <summary>透視で、これより近い距離はこの距離として扱う（カメラの中に入った物の画素数が無限大にならないように）。</summary>
    public float MinimumDistanceMeters { get; }
    /// <summary>透視のカメラの式。</summary>
    /// <param name="VerticalFieldOfViewRadians">縦の画角（0 より大きく π より小さい）。</param>
    /// <param name="ViewportHeightPixels">描く先の高さ（画素）。</param>
    /// <param name="MinimumDistanceMeters">これより近い距離はこの距離として扱う（0 より大きい）。普通は近くの切り取り面の距離。</param>
    public static ScreenSizeMetric Perspective(float VerticalFieldOfViewRadians,int ViewportHeightPixels,float MinimumDistanceMeters){
        if(!(VerticalFieldOfViewRadians>0f&&VerticalFieldOfViewRadians<MathF.PI)){ throw new ArgumentOutOfRangeException(nameof(VerticalFieldOfViewRadians),"The vertical field of view must lie strictly between zero and pi."); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ViewportHeightPixels);
        if(!(MinimumDistanceMeters>0f)||!float.IsFinite(MinimumDistanceMeters)){ throw new ArgumentOutOfRangeException(nameof(MinimumDistanceMeters),"The minimum distance must be finite and greater than zero."); }
        return new(ViewportHeightPixels*0.5f/MathF.Tan(VerticalFieldOfViewRadians*0.5f),IsOrthographic:false,MinimumDistanceMeters);
    }
    /// <summary>正射影のカメラの式。</summary>
    /// <param name="VerticalExtentMeters">画面の縦が写す長さ（メートル、0 より大きい）。</param>
    /// <param name="ViewportHeightPixels">描く先の高さ（画素）。</param>
    public static ScreenSizeMetric Orthographic(float VerticalExtentMeters,int ViewportHeightPixels){
        if(!(VerticalExtentMeters>0f)||!float.IsFinite(VerticalExtentMeters)){ throw new ArgumentOutOfRangeException(nameof(VerticalExtentMeters),"The vertical extent must be finite and greater than zero."); }
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ViewportHeightPixels);
        return new(ViewportHeightPixels/VerticalExtentMeters,IsOrthographic:true,1f);
    }
    /// <summary>
    /// 距離 <paramref name="DistanceMeters"/> にある長さ <paramref name="SizeMeters"/> の物が、画面で何画素になるか。
    /// 仮想ジオメトリの塊の誤差（メートル）を画素の誤差にするときにも使う。
    /// </summary>
    public float ProjectedPixels(float SizeMeters,float DistanceMeters)=>
        this.IsOrthographic?SizeMeters*this.PixelsPerMeter:SizeMeters*this.PixelsPerMeter/MathF.Max(DistanceMeters,this.MinimumDistanceMeters);
    /// <summary>
    /// 球（中心はカメラから見た相対の位置）の直径が、画面で何画素になるか。距離は球の表面のいちばん近い所までで測るので、どの向きから見ても小さく見積もらない。
    /// </summary>
    public float SphereProjectedPixels(Vector3 CentreRelativeToCamera,float RadiusMeters)=>
        this.ProjectedPixels(2f*RadiusMeters,MathF.Max(CentreRelativeToCamera.Length()-RadiusMeters,this.MinimumDistanceMeters));
    /// <summary>シェーダーへ渡す 4 つの値（x 画素の係数、y 正射影なら 1、z 最小の距離、w 0）。<see cref="Hlsl"/> の <c>metric</c> 引数。</summary>
    public Vector4 ShaderParameters=>new(this.PixelsPerMeter,this.IsOrthographic?1f:0f,this.MinimumDistanceMeters,0f);
    /// <summary>
    /// シェーダー側の同じ式。計算シェーダーは取り込み（#include）を使えないので、使うシェーダーはこの文字列を先頭に付けて組み立てる
    ///（付け方は M0130 で決める。今は試験だけが使う）。<see cref="ProjectedPixels"/> と <see cref="SphereProjectedPixels"/> と同じ順で計算する。
    /// </summary>
    public const string Hlsl="""
float ScreenSizeProjectedPixels(float sizeMeters, float distanceMeters, float4 metric)
{
    return metric.y > 0.5 ? sizeMeters * metric.x : sizeMeters * metric.x / max(distanceMeters, metric.z);
}
float ScreenSizeSphereProjectedPixels(float3 centreRelativeToCamera, float radiusMeters, float4 metric)
{
    return ScreenSizeProjectedPixels(2.0 * radiusMeters, max(length(centreRelativeToCamera) - radiusMeters, metric.z), metric);
}
""";
}
