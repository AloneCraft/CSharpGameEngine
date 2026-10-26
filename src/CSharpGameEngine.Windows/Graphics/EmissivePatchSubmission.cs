using CSharpGameEngine.Rendering;
using CSharpGameEngine.Rendering.Pipeline;
namespace CSharpGameEngine.Graphics.D3D12;
public static class EmissivePatchSubmission {
    /// <summary>Submits generated patches through the true one-sided rectangle Area-light path.</summary>
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
