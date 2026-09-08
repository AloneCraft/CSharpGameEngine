using System.Text.Json.Nodes;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class PlatformCompatibilityMatrixValidatorTests {
    [Fact]
    public void RepositoryMatrixSeparatesRequiredSyntheticSilentAndWarpFromOptionalHardware(){
        string Root=FeatureStageRepositoryLocator.Find();

        Assert.Empty(PlatformCompatibilityMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault()));
    }

    [Fact]
    public void MissingRequiredCellAndHardwarePromotionFailClosed(){
        string Root=CopyContractFiles();
        try {
            string MatrixPath=Path.Combine(Root,"Docs","Verification","Platform-Compatibility-Matrix.json");
            JsonNode Matrix=JsonNode.Parse(File.ReadAllText(MatrixPath))!;
            JsonArray Required=Matrix["requiredCells"]!.AsArray();
            Required.Remove(Required.Single(Cell=>Cell!["id"]!.GetValue<string>()=="platform-warp-release"));
            JsonNode Hardware=Matrix["optionalCells"]!.AsArray().Single(Cell=>Cell!["id"]!.GetValue<string>()=="platform-hardware-audio")!;
            Matrix["requiredCells"]!.AsArray().Add(Hardware.DeepClone());
            File.WriteAllText(MatrixPath,Matrix.ToJsonString());

            IReadOnlyList<string>Errors=PlatformCompatibilityMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault());

            Assert.Contains(Errors,Error=>Error.Contains("Missing required platform compatibility cell: platform-warp-release",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("Unregistered required platform compatibility cell: platform-hardware-audio",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void CiAdapterStageAndManualEvidenceDriftAreRejected(){
        string Root=CopyContractFiles();
        try {
            string CiPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
            JsonNode Ci=JsonNode.Parse(File.ReadAllText(CiPath))!;
            Ci["cells"]!.AsArray().Single(Cell=>Cell!["id"]!.GetValue<string>()=="warp-debug")!["adapter"]="hardware";
            File.WriteAllText(CiPath,Ci.ToJsonString());
            string MatrixPath=Path.Combine(Root,"Docs","Verification","Platform-Compatibility-Matrix.json");
            JsonNode Matrix=JsonNode.Parse(File.ReadAllText(MatrixPath))!;
            Matrix["requiredStages"]!.AsArray().RemoveAt(0);
            File.WriteAllText(MatrixPath,Matrix.ToJsonString());
            string ManualPath=Path.Combine(Root,"Docs","Verification","Manual-Evidence.json");
            JsonNode Manual=JsonNode.Parse(File.ReadAllText(ManualPath))!;
            Manual["entries"]!.AsArray().Single(Entry=>Entry!["id"]!.GetValue<string>()=="hardware-audio")!["owner"]="TestingCiRelease";
            File.WriteAllText(ManualPath,Manual.ToJsonString());

            IReadOnlyList<string>Errors=PlatformCompatibilityMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault());

            Assert.Contains(Errors,Error=>Error.Contains("adapter must be warp",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("Missing required platform Stage: platform.boundaries",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("hardware-audio has invalid owner",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void DuplicateAndEmptyReferencesReturnErrorsInsteadOfThrowing(){
        string Root=CopyContractFiles();
        try {
            string MatrixPath=Path.Combine(Root,"Docs","Verification","Platform-Compatibility-Matrix.json");
            JsonNode Matrix=JsonNode.Parse(File.ReadAllText(MatrixPath))!;
            JsonArray Required=Matrix["requiredCells"]!.AsArray();
            Required.Add(Required[0]!.DeepClone());
            Required.Single(Cell=>Cell!["id"]!.GetValue<string>()=="platform-warp-debug")!["ciCell"]="";
            Matrix["optionalCells"]!.AsArray().Single(Cell=>Cell!["id"]!.GetValue<string>()=="platform-hardware-audio")!["manualEvidenceId"]="";
            File.WriteAllText(MatrixPath,Matrix.ToJsonString());

            IReadOnlyList<string>Errors=PlatformCompatibilityMatrixValidator.Validate(Root,FeatureStageCatalog.CreateDefault());

            Assert.Contains(Errors,Error=>Error.Contains("Duplicate required platform compatibility cell",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("empty CI cell reference",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("empty manual evidence reference",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static string CopyContractFiles(){
        string Source=FeatureStageRepositoryLocator.Find();
        string Root=Path.Combine(Path.GetTempPath(),"cge-platform-compatibility-tests",Guid.NewGuid().ToString("N"));
        foreach(string Relative in new[]{
            "Docs/Verification/Platform-Compatibility-Matrix.json",
            "Docs/Verification/CI-Matrix.json",
            "Docs/Verification/Manual-Evidence.json",
            ".github/workflows/ci.yml",
        }){
            string Destination=Path.Combine(Root,Relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.Copy(Path.Combine(Source,Relative.Replace('/',Path.DirectorySeparatorChar)),Destination);
        }
        return Root;
    }
}
