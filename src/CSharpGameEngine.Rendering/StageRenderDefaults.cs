using System.Numerics;
namespace CSharpGameEngine.Rendering;
public static class StageRenderLimits {
    public const int DynamicObjectHeadroom=4096;
    public const int DynamicMaterialHeadroom=256;
}
public static class StageRenderDefaults {
    /// <summary>
    /// 一度もビューを受け取っていない描画面の最初のビュー（M0119・M0120 の起動の契約）。L5 の <c>RenderCamera.CreateDefault(Vector3.Zero)</c>
    /// （原点を 15 度上から 4.5 m 離れて見る、視野 45 度、0.1〜1000 m、露出 0）と同じ値で、L0 が L5 のカメラを使わずに済むよう、
    /// 目の位置と上方向をそのビット列のまま持つ。一致は <c>StageRenderDefaultsTests</c> が L5 の計算と比べて固定する。
    /// </summary>
    public static RenderViewSource InitialViewSource{get;}=CreateInitialViewSource();
    private static RenderViewSource CreateInitialViewSource(){
        var Eye=new Vector3(BitConverter.Int32BitsToSingle(0x00000000),BitConverter.Int32BitsToSingle(0x3F95146D),BitConverter.Int32BitsToSingle(unchecked((int)0xC08B17E4)));
        var Up=new Vector3(BitConverter.Int32BitsToSingle(0x00000000),BitConverter.Int32BitsToSingle(0x3F7746EB),BitConverter.Int32BitsToSingle(0x3E8483F0));
        return RenderViewSource.Perspective(Matrix4x4.CreateLookAt(Eye,Vector3.Zero,Up),Eye,
            new RadialViewParameters(MathF.PI/4f,0.1f,1000f,RadialDepthCurve.DefaultTransitionMeters),0f);
    }
    /// <summary>
    /// 起動直後の既定のビュー（<see cref="InitialViewSource"/>）と既定の方向光から作るカスケード影。L0 が全フレームスロットを初期化するときに書く
    /// （GPU のアップロード用のメモリは 0 で初期化される保証がないため）。カスケードを作る計算は L4 の <c>DirectionalShadowCascades</c>。
    /// </summary>
    public static IReadOnlyList<Pipeline.DirectionalShadowCascade>InitialDirectionalCascades(float AspectRatio,Vector3 DirectionTowardLight){
        var View=InitialViewSource.Resolve(AspectRatio);
        var Invertible=View.Depth.Encoding==RenderDepthEncoding.Logarithmic?View.AuxiliaryViewProjection:View.ViewProjection;
        return Pipeline.DirectionalShadowCascades.Build(Invertible,View.CameraPosition,DirectionTowardLight);
    }
    public static readonly PixelColor ClearColor=new(89,140,217,255);
    public static readonly Vector4 HdrClearColor=new(0.23238164f,0.4095592f,1.2785315f,1.0f);
    /// <summary>
    /// 表示される LDR の色（0..255）から、HDR のクリア値を求める。スワップチェーンは UNORM で、表示値は Narkowicz ACES 近似
    /// <c>f(x)=x(2.51x+0.03)/(x(2.43x+0.59)+0.14)</c> の結果そのものなので、その逆関数を解く。
    /// <see cref="ClearColor"/> に対しては <see cref="HdrClearColor"/> と一致する。1 に近い値は発散するので 0.99 で打ち切る。
    /// </summary>
    public static Vector4 HdrFromDisplay(PixelColor Color)=>new(
        InverseAces(Color.R/255f),InverseAces(Color.G/255f),InverseAces(Color.B/255f),1f);
    private static float InverseAces(float Display){
        var Y=Math.Min(Math.Max(Display,0f),0.99f);
        if(Y<=0f){ return 0f; }
        var A=2.43f*Y-2.51f;
        var B=0.59f*Y-0.03f;
        var C=0.14f*Y;
        var Discriminant=B*B-4f*A*C;
        return (-B-MathF.Sqrt(MathF.Max(Discriminant,0f)))/(2f*A);
    }
}
public static class StageRuntimeCapabilities {
    public static bool ShaderHotReloadEnabled {
        get {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
