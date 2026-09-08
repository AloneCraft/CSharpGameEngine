using System.Text.Json;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class RegressionBaselineTests {
    [Fact]
    public void M0064BaselinePreservesTheMeasuredExternalOracle(){
        var RepositoryRoot=CSharpGameEngine.FeatureStages.FeatureStageRepositoryLocator.Find();
        var PathValue=Path.Combine(RepositoryRoot,"Docs","Verification","M0064-Regression-Baseline.json");
        using var Document=JsonDocument.Parse(File.ReadAllText(PathValue));
        var Root=Document.RootElement;
        Assert.Equal("csharp-game-engine-regression-baseline-v1",Root.GetProperty("schemaVersion").GetString());
        Assert.Equal("M0064",Root.GetProperty("baselineId").GetString());
        Assert.Equal("UserEcsProject/EngineShowcaseScene.cs",Root.GetProperty("authoringEntry").GetString());
        Assert.Matches("^[0-9A-F]{64}$",Root.GetProperty("showcaseManifestSha256").GetString()!);
        var Verification=Root.GetProperty("verification").EnumerateArray().ToArray();
        Assert.Equal(new[]{"Debug","Release"},Verification.Select(static Entry=>Entry.GetProperty("configuration").GetString()!).ToArray());
        Assert.All(Verification,static Entry=>{
            Assert.Equal(0,Entry.GetProperty("failed").GetInt32());
            Assert.True(Entry.GetProperty("passed").GetInt32()>0);
            Assert.Contains("dotnet test CSharpGameEngine.slnx",Entry.GetProperty("command").GetString(),StringComparison.Ordinal);
        });
        Assert.True(Root.GetProperty("observableOracles").GetArrayLength()>=9);
        var VsixLedger=Path.Combine(RepositoryRoot,Root.GetProperty("vsixManualLedger").GetString()!.Replace('/',Path.DirectorySeparatorChar));
        Assert.True(File.Exists(VsixLedger));
    }
}
