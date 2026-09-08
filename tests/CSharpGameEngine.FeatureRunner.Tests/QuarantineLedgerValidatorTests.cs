using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class QuarantineLedgerValidatorTests {
    private static readonly DateTimeOffset Now=DateTimeOffset.Parse("2026-08-31T12:00:00Z");

    [Fact]
    public void RepositoryLedgerHasNoImplicitQuarantine(){
        string RepositoryRoot=FeatureStageRepositoryLocator.Find();

        Assert.Empty(QuarantineLedgerValidator.Validate(RepositoryRoot,FeatureStageCatalog.CreateDefault(),Now));
    }

    [Fact]
    public void ScopedOwnedLinkedEntryWithinFourteenDaysIsValid(){
        string Root=CreateRoot();
        try {
            WriteLedger(Root,[CreateEntry(Now.AddDays(-1),Now.AddDays(5))]);

            Assert.Empty(QuarantineLedgerValidator.Validate(Root,FeatureStageCatalog.CreateDefault(),Now));
        } finally { Directory.Delete(Root,true); }
    }

    [Fact]
    public void ExpiredOrOverlongQuarantineFailsNormally(){
        string ExpiredRoot=CreateRoot();
        string OverlongRoot=CreateRoot();
        try {
            WriteLedger(ExpiredRoot,[CreateEntry(Now.AddDays(-2),Now.AddSeconds(-1))]);
            WriteLedger(OverlongRoot,[CreateEntry(Now.AddDays(-1),Now.AddDays(14))]);

            Assert.Contains(QuarantineLedgerValidator.Validate(ExpiredRoot,FeatureStageCatalog.CreateDefault(),Now),Error=>Error.Contains("expired",StringComparison.Ordinal));
            Assert.Contains(QuarantineLedgerValidator.Validate(OverlongRoot,FeatureStageCatalog.CreateDefault(),Now),Error=>Error.Contains("fourteen-day maximum",StringComparison.Ordinal));
        } finally { Directory.Delete(ExpiredRoot,true);Directory.Delete(OverlongRoot,true); }
    }

    [Fact]
    public void MissingOwnershipEvidenceSignatureOrKnownStageIsRejected(){
        string Root=CreateRoot();
        try {
            WriteLedger(Root,[new {
                id="invalid",stageId="missing.stage",configuration="all",adapter="gpu",owner="",issueOrEvidence="",reason="",failureSignature="",createdUtc=Now,expiresUtc=Now.AddDays(1),
            }]);

            IReadOnlyList<string>Errors=QuarantineLedgerValidator.Validate(Root,FeatureStageCatalog.CreateDefault(),Now);

            Assert.Contains(Errors,Error=>Error.Contains("owner is required",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("unknown Stage",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("configuration must be",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("adapter must be",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("issueOrEvidence is required",StringComparison.Ordinal));
            Assert.Contains(Errors,Error=>Error.Contains("failureSignature is required",StringComparison.Ordinal));
        } finally { Directory.Delete(Root,true); }
    }

    private static object CreateEntry(DateTimeOffset CreatedUtc,DateTimeOffset ExpiresUtc)=>new {
        id="runner-transient",stageId="runner.contract",configuration="Debug",adapter="Warp",owner="build-infrastructure",issueOrEvidence="https://example.invalid/issues/1",reason="Scoped contract fixture",failureSignature="process-start-transient",createdUtc=CreatedUtc,expiresUtc=ExpiresUtc,
    };
    private static string CreateRoot(){
        string Root=Path.Combine(Path.GetTempPath(),"cge-quarantine-tests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        return Root;
    }
    private static void WriteLedger(string Root,IReadOnlyList<object>Entries){
        string DirectoryPath=Path.Combine(Root,"Docs","Verification");
        Directory.CreateDirectory(DirectoryPath);
        File.WriteAllText(Path.Combine(DirectoryPath,"Quarantines.json"),JsonSerializer.Serialize(new {schema=QuarantineLedgerValidator.Schema,entries=Entries}));
    }
}
