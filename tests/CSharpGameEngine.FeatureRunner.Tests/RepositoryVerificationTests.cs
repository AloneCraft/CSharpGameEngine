using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class RepositoryVerificationTests {
    [Fact]
    public void RepositoryFeatureCoverageIsConsistent(){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Errors=FeatureCoverageValidator.Validate(RepositoryRoot,FeatureStageCatalog.CreateDefault());
        Assert.Empty(Errors);
    }
    [Fact]
    public void FeatureCoverageRejectsMissingLedgerFeatureAndVisualScene(){
        using var Repository=new TemporaryRepository();
        Repository.Write("Docs/Implementation-Status.md","# Status\n\n## F-L1-001 — Only one\n\n- Status: Implemented\n- Stage: `missing.stage`\n");
        var Errors=FeatureCoverageValidator.Validate(Repository.Root,FeatureStageCatalog.CreateDefault());
        Assert.Contains(Errors,Error=>Error.Contains("F-L1-002",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("missing.stage",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("Scene project",StringComparison.Ordinal));
    }
    [Fact]
    public void ManualEvidenceLedgerAcceptsExplicitPendingOutsideRelease(){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Errors=ManualEvidenceLedgerValidator.Validate(RepositoryRoot,false,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.Zero));
        Assert.Empty(Errors);
    }
    [Fact]
    public void ManualEvidenceLedgerRejectsPendingAndExpiredEvidenceForRelease(){
        using var Repository=new TemporaryRepository();
        Repository.Write("Docs/Verification/Manual-Evidence.json","""
        {
          "schemaVersion": "csharp-game-engine-manual-evidence-v1",
          "entries": [
            { "id": "vsix-experimental", "requirement": "VSIX", "owner": "Editor", "status": "Passed", "evidencePath": "evidence.json", "expiresUtc": "2026-08-30T00:00:00Z" },
            { "id": "hardware-d3d12", "requirement": "GPU", "owner": "Rendering", "status": "Pending", "reason": "Not run" },
            { "id": "hardware-audio", "requirement": "Audio", "owner": "Audio", "status": "Pending", "reason": "Not run" },
            { "id": "release-package", "requirement": "Package", "owner": "Release", "status": "Pending", "reason": "Not run" }
          ]
        }
        """);
        Repository.Write("evidence.json","{}");
        var Errors=ManualEvidenceLedgerValidator.Validate(Repository.Root,true,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.Zero));
        Assert.Contains(Errors,Error=>Error.Contains("expired",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("hardware-d3d12",StringComparison.Ordinal)&&Error.Contains("release",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void ManualEvidenceLedgerAcceptsExpiredEvidenceOutsideRelease(){
        using var Repository=new TemporaryRepository();
        Repository.Write("Docs/Verification/Manual-Evidence.json","""
        {
          "schemaVersion": "csharp-game-engine-manual-evidence-v1",
          "entries": [
            { "id": "vsix-experimental", "requirement": "VSIX", "owner": "Editor", "status": "Passed", "evidencePath": "evidence.json", "expiresUtc": "2026-08-30T00:00:00Z" },
            { "id": "hardware-d3d12", "requirement": "GPU", "owner": "Rendering", "status": "Pending", "reason": "Not run" },
            { "id": "hardware-audio", "requirement": "Audio", "owner": "Audio", "status": "Pending", "reason": "Not run" },
            { "id": "release-package", "requirement": "Package", "owner": "Release", "status": "Pending", "reason": "Not run" }
          ]
        }
        """);
        Repository.Write("evidence.json","{}");
        var Errors=ManualEvidenceLedgerValidator.Validate(Repository.Root,false,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.Zero));
        Assert.Empty(Errors);
    }
    [Fact]
    public void ManualEvidenceLedgerRejectsDuplicateAndUnknownEntries(){
        using var Repository=new TemporaryRepository();
        Repository.Write("Docs/Verification/Manual-Evidence.json","""
        {
          "schemaVersion": "csharp-game-engine-manual-evidence-v1",
          "entries": [
            { "id": "vsix-experimental", "requirement": "VSIX", "owner": "Editor", "status": "Pending", "reason": "Not run" },
            { "id": "vsix-experimental", "requirement": "VSIX", "owner": "Editor", "status": "Pending", "reason": "Not run" },
            { "id": "unknown", "requirement": "Unknown", "owner": "Nobody", "status": "Pending", "reason": "Not run" }
          ]
        }
        """);
        var Errors=ManualEvidenceLedgerValidator.Validate(Repository.Root,false,new DateTimeOffset(2026,8,31,0,0,0,TimeSpan.Zero));
        Assert.Contains(Errors,Error=>Error.Contains("Duplicate",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("unknown",StringComparison.Ordinal));
        Assert.Contains(Errors,Error=>Error.Contains("release-package",StringComparison.Ordinal));
    }
    private sealed class TemporaryRepository:IDisposable {
        internal string Root{get;}=Path.Combine(Path.GetTempPath(),$"CSharpGameEngine-FeatureRunner-{Guid.NewGuid():N}");
        internal TemporaryRepository(){ Directory.CreateDirectory(this.Root); }
        internal void Write(string RelativePath,string Content){
            var FullPath=Path.Combine(this.Root,RelativePath.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
            File.WriteAllText(FullPath,Content);
        }
        public void Dispose(){ Directory.Delete(this.Root,true); }
    }
}
