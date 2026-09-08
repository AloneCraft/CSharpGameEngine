using System.Text.Json.Nodes;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ReleaseCompatibilityMatrixValidatorTests {
    [Fact]
    public void RepositoryScopeAggregatesExactRequiredAndOptionalCompatibilityReferences(){
        string Root=FeatureStageRepositoryLocator.Find();

        Assert.Empty(ReleaseCompatibilityMatrixValidator.Validate(
            Root,FeatureStageCatalog.CreateDefault(),
            new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero)));
    }

    [Fact]
    public void MissingDuplicateAndPromotedHardwareReferencesFailClosed(){
        string Root=CopyContractFiles();
        try {
            string MatrixPath=Path.Combine(
                Root,"Docs","Verification","Release-Compatibility-Matrix.json");
            JsonNode Matrix=JsonNode.Parse(File.ReadAllText(MatrixPath))!;
            JsonArray RequiredCi=Matrix["requiredCiCells"]!.AsArray();
            RequiredCi.Remove(RequiredCi.Single(static Cell=>
                Cell!.GetValue<string>()=="warp-release"));
            RequiredCi.Add(RequiredCi[0]!.DeepClone());
            Matrix["requiredPlatformCells"]!.AsArray().Add("platform-hardware-audio");
            Matrix["optionalHardwareCells"]!.AsArray().Remove(
                Matrix["optionalHardwareCells"]!.AsArray().Single(static Cell=>
                    Cell!.GetValue<string>()=="platform-hardware-audio"));
            File.WriteAllText(MatrixPath,Matrix.ToJsonString());

            IReadOnlyList<string>Errors=ReleaseCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),
                new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,static Error=>Error.Contains(
                "Missing required CI cell: warp-release",StringComparison.Ordinal));
            Assert.Contains(Errors,static Error=>Error.Contains(
                "Duplicate required CI cell: solution-debug",StringComparison.Ordinal));
            Assert.Contains(Errors,static Error=>Error.Contains(
                "Unregistered required Platform cell: platform-hardware-audio",
                StringComparison.Ordinal));
            Assert.Contains(Errors,static Error=>Error.Contains(
                "Missing optional hardware cell: platform-hardware-audio",
                StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void SourceMatrixDriftCannotBeHiddenByAnUnchangedAggregate(){
        string Root=CopyContractFiles();
        try {
            string CiPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
            JsonNode Ci=JsonNode.Parse(File.ReadAllText(CiPath))!;
            Ci["cells"]!.AsArray().Remove(Ci["cells"]!.AsArray().Single(static Cell=>
                Cell!["id"]!.GetValue<string>()=="warp-release"));
            File.WriteAllText(CiPath,Ci.ToJsonString());
            string VsixPath=Path.Combine(
                Root,"Docs","Verification","VSIX-Compatibility-Matrix.json");
            JsonNode Vsix=JsonNode.Parse(File.ReadAllText(VsixPath))!;
            Vsix["automation"]!["checks"]!.AsArray().Single(static Check=>
                Check!["id"]!.GetValue<string>()=="dpi-scaling")!["requiredForRelease"]=false;
            File.WriteAllText(VsixPath,Vsix.ToJsonString());

            IReadOnlyList<string>Errors=ReleaseCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),
                new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,static Error=>Error.Contains(
                "CI source is missing required aggregate cell warp-release",
                StringComparison.Ordinal));
            Assert.Contains(Errors,static Error=>Error.Contains(
                "VSIX source no longer marks aggregate check dpi-scaling as required",
                StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void AggregateContainsReferencesOnlyAndDoesNotCopyEvidenceStatus(){
        string Root=FeatureStageRepositoryLocator.Find();
        string Json=File.ReadAllText(Path.Combine(
            Root,"Docs","Verification","Release-Compatibility-Matrix.json"));

        Assert.DoesNotContain("\"status\"",Json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"passed\"",Json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"evidencePath\"",Json,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"expiresUtc\"",Json,StringComparison.OrdinalIgnoreCase);
    }

    private static string CopyContractFiles(){
        string Source=FeatureStageRepositoryLocator.Find();
        string Root=Path.Combine(
            Path.GetTempPath(),"cge-release-compatibility-tests",
            Guid.NewGuid().ToString("N"));
        foreach(string Relative in new[]{
            "Docs/Verification/Release-Compatibility-Matrix.json",
            "Docs/Verification/Platform-Compatibility-Matrix.json",
            "Docs/Verification/VSIX-Compatibility-Matrix.json",
            "Docs/Verification/VSIX-Experimental-Baseline.json",
            "Docs/Verification/CI-Matrix.json",
            "Docs/Verification/Manual-Evidence.json",
            ".github/workflows/ci.yml",
            "tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj",
            "tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest",
            "tools/CSharpGameEngine.Vsix.Automation/CSharpGameEngine.Vsix.Automation.csproj",
        }){
            string Destination=Path.Combine(
                Root,Relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.Copy(Path.Combine(
                Source,Relative.Replace('/',Path.DirectorySeparatorChar)),Destination);
        }
        return Root;
    }
}
