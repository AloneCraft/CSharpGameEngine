using System.Text.Json;

namespace UserGameProject.Tests;

/// <summary>Parses the exact JSON shape tools/UnrealExport/export_lvl_firstperson.py writes (a real captured sample, trimmed to its parsed fields plus the export-time-only fields SceneMetaDocument deliberately leaves unmapped).</summary>
public class SceneDocumentTests
{
    private const string RealSampleJson = """
        {
          "version": 1,
          "conventions": { "units": "meters", "handedness": "right", "up": "+Y" },
          "ambient": { "colorRgb": [1.0, 1.0, 1.0], "intensity": 1.0 },
          "fog": { "density": 0.0436, "heightFalloff": 0.2, "colorRgb": [0.6, 0.65, 0.7] },
          "playerStart": { "translation": [0.0, 3.0201261220702875, -0.0], "yawRadians": -0.0 },
          "mobilityOverrides": { "SM_Cube16": "movable", "SM_Ramp5": "static" },
          "clearColorRgb": [0.35, 0.55, 0.85],
          "actorCounts": { "staticMeshActors": 55, "directionalLights": 1 }
        }
        """;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void Deserialize_RealExportSample_ParsesAmbientFogPlayerStartAndMobility()
    {
        SceneMetaDocument? Document = JsonSerializer.Deserialize<SceneMetaDocument>(RealSampleJson, Options);

        Assert.NotNull(Document);
        Assert.NotNull(Document.Ambient);
        Assert.Equal([1.0f, 1.0f, 1.0f], Document.Ambient!.ColorRgb);
        Assert.Equal(1.0f, Document.Ambient.Intensity);

        Assert.NotNull(Document.Fog);
        Assert.Equal(0.0436f, Document.Fog!.Density, precision: 4);
        Assert.Equal(0.2f, Document.Fog.HeightFalloff);

        Assert.NotNull(Document.PlayerStart);
        Assert.Equal(3.0201261f, Document.PlayerStart!.Translation[1], precision: 3);

        Assert.Equal("movable", Document.MobilityOverrides["SM_Cube16"]);
        Assert.Equal("static", Document.MobilityOverrides["SM_Ramp5"]);
    }

    [Fact]
    public void Deserialize_MissingOptionalSections_LeavesThemNull()
    {
        const string Json = """{ "mobilityOverrides": {} }""";

        SceneMetaDocument? Document = JsonSerializer.Deserialize<SceneMetaDocument>(Json, Options);

        Assert.NotNull(Document);
        Assert.Null(Document.Ambient);
        Assert.Null(Document.Fog);
        Assert.Null(Document.PlayerStart);
        Assert.Empty(Document.MobilityOverrides);
    }
}
