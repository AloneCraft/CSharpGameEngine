using System.Numerics;
namespace CSharpGameEngine.Rendering;
public static class StageRenderLimits {
    public const int DynamicObjectHeadroom=4096;
    public const int DynamicMaterialHeadroom=256;
}
public static class StageRenderDefaults {
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
