namespace UserGameProject;

/// <summary>
/// Deserialization shape for scene_meta.json (see tools/UnrealExport/export_lvl_firstperson.py,
/// ADR-0017): only the fields <see cref="SceneLoader"/> actually consumes are declared here - the
/// export script also writes version/conventions/actorCounts, which are export-time diagnostics,
/// not runtime inputs, so they are simply left unmapped (System.Text.Json ignores unrecognized
/// JSON properties by default) rather than modeled as unused C# properties.
/// </summary>
public sealed class SceneMetaDocument
{
    public SceneMetaAmbient? Ambient { get; init; }

    public SceneMetaFog? Fog { get; init; }

    public SceneMetaPlayerStart? PlayerStart { get; init; }

    /// <summary>Actor label (glTF node name) -> "static" or "movable". Missing entries default to "static" - see <see cref="SceneLoader"/>.</summary>
    public Dictionary<string, string> MobilityOverrides { get; init; } = [];
}

public sealed class SceneMetaAmbient
{
    public required float[] ColorRgb { get; init; }

    public required float Intensity { get; init; }
}

public sealed class SceneMetaFog
{
    public required float Density { get; init; }

    public required float HeightFalloff { get; init; }

    public required float[] ColorRgb { get; init; }
}

public sealed class SceneMetaPlayerStart
{
    public required float[] Translation { get; init; }

    public required float YawRadians { get; init; }
}
