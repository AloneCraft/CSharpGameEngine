using System.Numerics;
namespace CSharpGameEngine.Rendering;
public static class StageRenderLimits {
    public const int DynamicObjectHeadroom=4096;
    public const int DynamicMaterialHeadroom=256;
}
public static class StageRenderDefaults {
    public static readonly PixelColor ClearColor=new(89,140,217,255);
    public static readonly Vector4 HdrClearColor=new(0.23238164f,0.4095592f,1.2785315f,1.0f);
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
