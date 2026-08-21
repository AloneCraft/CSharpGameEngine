using CSharpGameEngine.Entities;

[assembly: TransformPrecision(
    GameEnginePrecision.TotalBits,
    GameEnginePrecision.FractionalBits)]

/// <summary>
/// Source-of-truth fixed-point precision for this Scene project.
///
/// Change these constants through source control (or the future project-aware Visual Studio
/// options page). The TransformPrecision incremental generator consumes the assembly attribute
/// above and emits the matching fixed-limb storage into the normal generated-source output.
/// </summary>
public static class GameEnginePrecision {
    /// <summary>Total signed fixed-point storage width.</summary>
    public const int TotalBits=384;

    /// <summary>Low-order fractional precision. The remaining 211 bits carry sign/integer range.</summary>
    public const int FractionalBits=173;
}
