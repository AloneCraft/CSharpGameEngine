namespace CSharpGameEngine.Spatial.Abstractions;

/// <summary>
/// Type-based entry point for one selected <typeparamref name="TConvention"/> - every member
/// resolves to <typeparamref name="TConvention"/>'s static members at compile time (no runtime
/// preset dispatch). A game project that has already picked exactly one preset (see
/// "コンパイル時プリセット選択" in Docs/Specifications/Spatial-RUF.md) should expose its own
/// non-generic facade (e.g. <c>ProjectSpatial</c>) that forwards to <c>Spatial&lt;ThatPreset&gt;</c>,
/// so application code never has to spell out <typeparamref name="TConvention"/> itself.
/// </summary>
public static class Spatial<TConvention>
    where TConvention:struct,ICoordinateConvention {
    public static XyzVector ToXyz(in RufVector Value)=>TConvention.ToXyz(Value);

    public static RufVector FromXyz(in XyzVector Value)=>TConvention.FromXyz(Value);

    /// <summary>
    /// Physically correct cross product of two RUF-space vectors: converts both operands to this
    /// convention's physical XYZ storage, applies the one true (right-hand-rule) physical cross
    /// product, then converts the result back to RUF. See
    /// Docs/Specifications/Spatial-RUF.md "Handedness の定義" for why this - and not a naive
    /// formula on R/U/F components - is the only way to correctly reflect a convention's
    /// Handedness, and for the verified relationship: for a LeftHanded convention,
    /// <c>Cross(Right, Up) == Forward</c>; for a RightHanded convention,
    /// <c>Cross(Right, Up) == Backward</c>.
    /// </summary>
    public static RufVector Cross(in RufVector Left,in RufVector Right){
        XyzVector CrossXyz=XyzVector.Cross(TConvention.ToXyz(Left),TConvention.ToXyz(Right));
        return TConvention.FromXyz(CrossXyz);
    }

    /// <summary>
    /// Given Up and Forward, returns the Right direction consistent with this convention's actual
    /// Handedness (unlike <see cref="Cross"/>, the sign is corrected so the result always means
    /// "Right", for both LeftHanded and RightHanded conventions). A scope-limited alternative to
    /// exposing a raw cross product everywhere - see Docs/Specifications/Spatial-RUF.md
    /// "Cross Product仕様".
    /// </summary>
    public static RufVector RightFromUpForward(in RufVector Up,in RufVector Forward){
        RufVector RawCross=Cross(Up,Forward);
        return TConvention.Handedness==CoordinateHandedness.LeftHanded?RawCross:-RawCross;
    }
}
