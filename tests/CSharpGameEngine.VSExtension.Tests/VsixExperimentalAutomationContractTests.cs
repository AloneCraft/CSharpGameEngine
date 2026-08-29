using System;
using System.Collections.Generic;
using System.IO;
using CSharpGameEngine.Vsix.Automation;
namespace CSharpGameEngine.VSExtension.Tests;
public sealed class VsixExperimentalAutomationContractTests {
    [Fact]
    public void ExitCodes_AreStable() {
        Assert.Equal(0,(int)AutomationExitCode.Success);
        Assert.Equal(1,(int)AutomationExitCode.OracleMismatch);
        Assert.Equal(2,(int)AutomationExitCode.InfrastructureError);
    }
    [Fact]
    public void Parse_RunRequiresTheDedicatedAutomationHive() {
        var Valid=AutomationOptions.Parse(["run","--scenario","shader-graph","--configuration","Debug","--root-suffix","CgeAutomation","--timeout-seconds","3300","--evidence-dir","evidence","--locale","auto"]);
        Assert.True(Valid.Success);
        Assert.Equal(AutomationCommand.Run,Valid.Options!.Command);
        var Invalid=AutomationOptions.Parse(["run","--scenario","shader-graph","--configuration","Debug","--root-suffix","CgeFixed","--timeout-seconds","3300","--evidence-dir","evidence","--locale","auto"]);
        Assert.False(Invalid.Success);
    }
    [Theory]
    [InlineData("29")]
    [InlineData("7201")]
    public void Parse_RejectsTimeoutOutsideTheContract(string TimeoutSeconds) {
        var Result=AutomationOptions.Parse(["run","--scenario","shader-graph","--configuration","Debug","--root-suffix","CgeAutomation","--timeout-seconds",TimeoutSeconds,"--evidence-dir","evidence","--locale","auto"]);
        Assert.False(Result.Success);
    }
    [Fact]
    public void ProcessPolicy_MatchesRootSuffixAsACompleteArgument() {
        Assert.True(RootSuffixProcessPolicy.IsOwnedAutomationHive("devenv.exe /RootSuffix CgeAutomation file.slnx"));
        Assert.True(RootSuffixProcessPolicy.IsOwnedAutomationHive("devenv.exe /rootsuffix CgeAutomation"));
        Assert.False(RootSuffixProcessPolicy.IsOwnedAutomationHive("devenv.exe /RootSuffix CgeAutomationOld"));
        Assert.False(RootSuffixProcessPolicy.IsOwnedAutomationHive("devenv.exe /RootSuffix CgeFixed"));
    }
    [Fact]
    public void ShutdownPolicy_ForcesOnlyTheOwnedPidAfterTheGracePeriod() {
        Assert.True(OwnedProcessShutdownPolicy.CanForceTerminate(4100,4100,true));
        Assert.False(OwnedProcessShutdownPolicy.CanForceTerminate(4100,4200,true));
        Assert.False(OwnedProcessShutdownPolicy.CanForceTerminate(4100,4100,false));
        Assert.False(OwnedProcessShutdownPolicy.CanForceTerminate(0,0,true));
    }
    [Fact]
    public void CandidateSelector_RequiresDevenvAndExcludesSsms() {
        var Candidates=new[]{
            new VisualStudioCandidate("C:/Program Files/Microsoft SQL Server Management Studio 22","C:/Program Files/Microsoft SQL Server Management Studio 22/Common7/IDE/Ssms.exe","22.0"),
            new VisualStudioCandidate("C:/Program Files/Microsoft Visual Studio/18/Community","C:/Program Files/Microsoft Visual Studio/18/Community/Common7/IDE/devenv.exe","18.9")
        };
        var Selected=VisualStudioCandidateSelector.Select(Candidates,PathValue=>PathValue.EndsWith("devenv.exe",StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(Selected);
        Assert.EndsWith("devenv.exe",Selected!.ProductPath,StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void LedgerValidator_DoesNotTreatPendingAsPassedAndRequiresFreshEvidence() {
        using(var Temp=new TemporaryDirectory()) {
            var Evidence=Path.Combine(Temp.Path,"proof.json");
            File.WriteAllText(Evidence,"{}");
            var Manifest=Path.Combine(Temp.Path,"ledger.json");
            File.WriteAllText(Manifest,"""
{"schemaVersion":"cge-vsix-experimental-baseline-v1","checks":[{"id":"pending","owner":"editor","status":"Pending"},{"id":"passed","owner":"automation","status":"Passed","evidencePath":"proof.json","observedUtc":"2026-08-28T00:00:00Z","expiresUtc":"2026-09-28T00:00:00Z"}]}
""");
            var Result=VsixExperimentalLedgerValidator.Validate(Manifest,new DateTimeOffset(2026,8,29,0,0,0,TimeSpan.Zero));
            Assert.True(Result.IsValid);
            Assert.Equal(1,Result.PassedCount);
            Assert.Equal(1,Result.PendingCount);
            File.Delete(Evidence);
            Assert.False(VsixExperimentalLedgerValidator.Validate(Manifest,new DateTimeOffset(2026,8,29,0,0,0,TimeSpan.Zero)).IsValid);
        }
    }
    [Fact]
    public void EvidenceWriter_ProducesStableSha256ForPersistedJson() {
        using(var Temp=new TemporaryDirectory()) {
            var PathValue=Path.Combine(Temp.Path,"evidence.json");
            var Evidence=new VsixExperimentalEvidence { RunId="run-1",Configuration="Debug",Locale="1041",RootSuffix="CgeAutomation",Status="Passed",PackageDllSha256=new string('a',64),Assertions=new List<VsixEvidenceAssertion>{new VsixEvidenceAssertion { Id="package-load",Passed=true,Detail="loaded" }} };
            var Hash=VsixEvidenceWriter.Write(PathValue,Evidence);
            Assert.Equal(Hash,VsixEvidenceWriter.ComputeSha256(PathValue));
            Assert.Equal(64,Hash.Length);
            Assert.Contains("packageDllSha256",File.ReadAllText(PathValue),StringComparison.Ordinal);
        }
    }
    private sealed class TemporaryDirectory:IDisposable {
        internal TemporaryDirectory() { this.Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"CgeVsixAutomationTests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(this.Path); }
        internal string Path { get; }
        public void Dispose() { if(Directory.Exists(this.Path)) { Directory.Delete(this.Path,true); } }
    }
}
