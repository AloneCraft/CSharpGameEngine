using System.Text.Json.Nodes;
using System.Xml.Linq;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class VsixCompatibilityMatrixValidatorTests {
    [Fact]
    public void RepositoryMatrixAgreesWithVsixAutomationEvidenceAndCiContracts(){
        string Root=FeatureStageRepositoryLocator.Find();
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        DateTimeOffset Now=new(2026,9,1,0,0,0,TimeSpan.Zero);

        Assert.Empty(VsixCompatibilityMatrixValidator.Validate(Root,Catalog,"Debug",false,Now));
        Assert.Empty(VsixCompatibilityMatrixValidator.Validate(Root,Catalog,"Release",false,Now));
    }

    [Fact]
    public void ReleaseValidationFailsClosedWhileRequiredExperimentalEvidenceIsPending(){
        string Root=CopyContractFiles();
        try {
            string ExperimentalPath=Path.Combine(
                Root,"Docs","Verification","VSIX-Experimental-Baseline.json");
            JsonNode Experimental=JsonNode.Parse(
                File.ReadAllText(ExperimentalPath))!;
            JsonNode Pending=Experimental["checks"]!.AsArray().Single(Item=>
                Item!["id"]!.GetValue<string>()=="shader-graph-1041")!;
            Pending["status"]="Pending";
            Pending["evidencePath"]=null;
            Pending["observedUtc"]=null;
            Pending["expiresUtc"]=null;
            File.WriteAllText(ExperimentalPath,Experimental.ToJsonString());
            string ManualPath=Path.Combine(
                Root,"Docs","Verification","Manual-Evidence.json");
            JsonNode Manual=JsonNode.Parse(File.ReadAllText(ManualPath))!;
            JsonNode Summary=Manual["entries"]!.AsArray().Single(Entry=>
                Entry!["id"]!.GetValue<string>()=="vsix-experimental")!;
            Summary["status"]="Pending";
            File.WriteAllText(ManualPath,Manual.ToJsonString());

            IReadOnlyList<string>Errors=VsixCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),"Release",true,
                new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,Error=>Error.Contains(
                "requires check shader-graph-1041 to be Passed",
                StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains(
                "manual VSIX summary to be Passed",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void ProjectManifestAndCiDriftAreRejectedBidirectionally(){
        string Root=CopyContractFiles();
        try {
            string ProjectPath=Path.Combine(Root,"tools","CSharpGameEngine.VSExtension","CSharpGameEngine.VSExtension.csproj");
            File.WriteAllText(ProjectPath,File.ReadAllText(ProjectPath).Replace("<TargetFramework>net472</TargetFramework>","<TargetFramework>net471</TargetFramework>",StringComparison.Ordinal));
            string ManifestPath=Path.Combine(Root,"tools","CSharpGameEngine.VSExtension","source.extension.vsixmanifest");
            XDocument Manifest=XDocument.Load(ManifestPath);
            XNamespace VsixNamespace="http://schemas.microsoft.com/developer/vsx-schema/2011";
            Manifest.Root!.Element(VsixNamespace+"Installation")!.Element(VsixNamespace+"InstallationTarget")!.SetAttributeValue("Version","[18.0,)");
            Manifest.Save(ManifestPath);
            string CiPath=Path.Combine(Root,"Docs","Verification","CI-Matrix.json");
            JsonNode Ci=JsonNode.Parse(File.ReadAllText(CiPath))!;
            Ci["cells"]!.AsArray().Single(Cell=>Cell!["id"]!.GetValue<string>()=="vsix-release")!["targetFramework"]="net471";
            File.WriteAllText(CiPath,Ci.ToJsonString());

            IReadOnlyList<string>Errors=VsixCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),"Debug",false,new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,Error=>Error.Contains("VSIX project TargetFramework must be net472",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("installation target version must be [17.14,)",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("CI matrix cell vsix-release",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void DetailedLedgerMustHaveTheExactMatrixChecksAndMatchingSummary(){
        string Root=CopyContractFiles();
        try {
            string ExperimentalPath=Path.Combine(Root,"Docs","Verification","VSIX-Experimental-Baseline.json");
            JsonNode Experimental=JsonNode.Parse(File.ReadAllText(ExperimentalPath))!;
            JsonArray Checks=Experimental["checks"]!.AsArray();
            Checks.Remove(Checks.Single(Check=>Check!["id"]!.GetValue<string>()=="dpi-scaling"));
            Checks.Add(Checks[0]!.DeepClone());
            File.WriteAllText(ExperimentalPath,Experimental.ToJsonString());
            string ManualPath=Path.Combine(Root,"Docs","Verification","Manual-Evidence.json");
            JsonNode Manual=JsonNode.Parse(File.ReadAllText(ManualPath))!;
            Manual["entries"]!.AsArray().Single(Entry=>Entry!["id"]!.GetValue<string>()=="vsix-experimental")!["status"]="Failed";
            Manual["entries"]!.AsArray().Add(Manual["entries"]!.AsArray().Single(Entry=>Entry!["id"]!.GetValue<string>()=="vsix-experimental")!.DeepClone());
            File.WriteAllText(ManualPath,Manual.ToJsonString());

            IReadOnlyList<string>Errors=VsixCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),"Debug",false,new DateTimeOffset(2026,9,1,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,Error=>Error.Contains("missing compatibility check dpi-scaling",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("Duplicate VSIX Experimental ledger check",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("Duplicate manual evidence entry",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("summary status must be Pending",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void PassedAutomatedEvidenceMustProveTheDeclaredHostLocaleAndHashes(){
        string Root=CopyContractFiles();
        try {
            string ExperimentalPath=Path.Combine(Root,"Docs","Verification","VSIX-Experimental-Baseline.json");
            JsonNode Experimental=JsonNode.Parse(File.ReadAllText(ExperimentalPath))!;
            JsonNode Check=Experimental["checks"]!.AsArray().Single(Item=>Item!["id"]!.GetValue<string>()=="shader-graph-1033")!;
            Check["status"]="Passed";
            Check["evidencePath"]="invalid-host.json";
            Check["observedUtc"]="2026-09-01T00:00:00Z";
            Check["expiresUtc"]="2026-09-15T00:00:00Z";
            File.WriteAllText(ExperimentalPath,Experimental.ToJsonString());
            File.WriteAllText(Path.Combine(Root,"Docs","Verification","invalid-host.json"),"""
            {
              "schemaVersion": "cge-vsix-experimental-evidence-v1",
              "configuration": "Debug",
              "locale": "1041",
              "rootSuffix": "CgeFixed",
              "status": "Passed",
              "visualStudioVersion": "17.13",
              "packageSha256": "bad",
              "packageDllSha256": "bad",
              "buildDllSha256": "bad",
              "installedDllSha256": "bad",
              "assertions": []
            }
            """);

            IReadOnlyList<string>Errors=VsixCompatibilityMatrixValidator.Validate(
                Root,FeatureStageCatalog.CreateDefault(),"Debug",false,new DateTimeOffset(2026,9,2,0,0,0,TimeSpan.Zero));

            Assert.Contains(Errors,Error=>Error.Contains("rootSuffix must be CgeAutomation",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("configuration must be Release",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("locale must be 1033",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("does not prove Visual Studio 17.14+",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("package hash is invalid",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("only passing assertions",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static string CopyContractFiles(){
        string SourceRoot=FeatureStageRepositoryLocator.Find();
        string Root=Path.Combine(Path.GetTempPath(),"cge-vsix-compatibility-tests",Guid.NewGuid().ToString("N"));
        foreach(string RelativePath in new[]{
            "Docs/Verification/VSIX-Compatibility-Matrix.json",
            "Docs/Verification/VSIX-Experimental-Baseline.json",
            "Docs/Verification/Manual-Evidence.json",
            "Docs/Verification/CI-Matrix.json",
            "tools/CSharpGameEngine.VSExtension/CSharpGameEngine.VSExtension.csproj",
            "tools/CSharpGameEngine.VSExtension/source.extension.vsixmanifest",
            "tools/CSharpGameEngine.Vsix.Automation/CSharpGameEngine.Vsix.Automation.csproj",
        }){
            string Destination=Path.Combine(Root,RelativePath.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.Copy(Path.Combine(SourceRoot,RelativePath.Replace('/',Path.DirectorySeparatorChar)),Destination);
        }
        return Root;
    }
}
