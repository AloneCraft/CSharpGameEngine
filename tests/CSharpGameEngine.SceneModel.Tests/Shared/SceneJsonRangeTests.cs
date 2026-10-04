using System.Globalization;
using CSharpGameEngine.SceneAnalysis.Json;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// A number a member's type cannot hold is SCENE203 at its position, the same on .NET (the tools, a <c>dotnet build</c>) and
/// .NET Framework (the VSIX, a build under Visual Studio's MSBuild). This file is in <c>Shared</c>, so the VSIX test
/// project runs it on net472 too. <c>float.TryParse</c> returns an infinity on .NET but fails on .NET Framework, and the
/// float range check once read only the infinity: a LogicalScalar written as 1e39 had no diagnostic in the editor, where
/// every Designer edit then failed on "Infinity has no JSON form", and a build on a .NET Framework host got a compile
/// error in the generated file instead of a diagnostic.
/// </summary>
public sealed class SceneJsonRangeTests {
    private const string Components="""
        namespace CSharpGameEngine.Numerics {
            public readonly struct LogicalScalar {
                public static implicit operator LogicalScalar(float Value)=>default;
            }
        }
        namespace Sample {
            public struct Lamp {
                public CSharpGameEngine.Numerics.LogicalScalar Height;
                public float Scale; public double Far; public decimal Price;
                public sbyte Small; public ulong Big;
            }
        }
        """;

    private const string Scene="""
        {
          "schemaVersion": 1,
          "namespace": "Game",
          "className": "MainScene",
          "root": {
            "id": "7c0c2c9e-6c56-4f46-9d0f-0c6f2c8f0001",
            "name": "Root",
            "components": [
              { "type": "Sample.Lamp", "members": { "Height": 9e18, "Scale": 3e38, "Far": -1e308, "Price": 7.9e28, "Small": -128, "Big": 18446744073709551615 } }
            ],
            "children": []
          }
        }
        """;

    private static (string Id,string Message)[]Problems(string Json)=>
        SceneJsonSceneAnalyzer.Analyze(Json,SceneAnalysisTestHelper.CreateCompilation(Components)).Problems
            .Select(static Problem=>(Problem.Descriptor.Id,string.Format(CultureInfo.InvariantCulture,Problem.Descriptor.MessageFormat.ToString(CultureInfo.InvariantCulture),Problem.Arguments))).ToArray();

    [Fact]
    public void TheLimitsThemselves_AreAccepted()=>Assert.Empty(Problems(Scene));

    [Theory]
    [InlineData("\"Height\": 9e18","\"Height\": 1e19","beyond the Q64.64 range")]
    [InlineData("\"Height\": 9e18","\"Height\": 1e39","beyond the range of float")]
    [InlineData("\"Height\": 9e18","\"Height\": -1e39","beyond the range of float")]
    [InlineData("\"Height\": 9e18","\"Height\": 9.9999999999999994E+38","beyond the range of float")]
    [InlineData("\"Height\": 9e18","\"Height\": 1e400","beyond the range of float")]
    [InlineData("\"Scale\": 3e38","\"Scale\": 1e39","beyond the range of float")]
    [InlineData("\"Scale\": 3e38","\"Scale\": -3.5e38","beyond the range of float")]
    [InlineData("\"Far\": -1e308","\"Far\": -1e309","beyond the range of double")]
    [InlineData("\"Price\": 7.9e28","\"Price\": 7.93e28","beyond the range of decimal")]
    [InlineData("\"Small\": -128","\"Small\": -129","out of range")]
    [InlineData("\"Big\": 18446744073709551615","\"Big\": 18446744073709551616","out of range")]
    public void ANumberBeyondItsType_IsSCENE203AtItsPosition(string Original,string Replacement,string Reason){
        Assert.Contains(Original,Scene);
        var Problem=Assert.Single(Problems(Scene.Replace(Original,Replacement)));
        Assert.Equal("SCENE203",Problem.Id);
        Assert.Contains(Reason,Problem.Message,StringComparison.Ordinal);
    }
}
