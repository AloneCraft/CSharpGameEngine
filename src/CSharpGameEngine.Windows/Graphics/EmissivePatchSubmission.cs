using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
public static class EmissivePatchSubmission {
    /// <summary>生成したパッチを、片面長方形の Area Light 経路で送信する。</summary>
    public static void SubmitDirectLighting(
        this StageRenderer Renderer,
        IReadOnlyList<EmissivePatch> Patches,
        float RangeMeters,
        bool CastsShadows,
        int ShadowPriority,
        float ShadowBiasMeters){
        ArgumentNullException.ThrowIfNull(Renderer);
        ArgumentNullException.ThrowIfNull(Patches);
        foreach(EmissivePatch Patch in Patches){
            Renderer.AddAreaLight(
                Patch.Center,Patch.Forward,Patch.Right,Patch.Up,Patch.LinearRgb,Patch.LuminanceNits,
                Patch.WidthMeters,Patch.HeightMeters,RangeMeters,
                ShadowPriority,CastsShadows,ShadowBiasMeters,PhysicalLightKind.Emissive);
        }
    }

}
