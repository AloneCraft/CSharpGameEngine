using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ReleaseCandidateEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeReleaseCandidateTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public void ScopeContainsOnlyExecutableReachedClosureAndExcludesGateStages(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();

        ReleaseCandidateScope Scope=ReleaseCandidateEvidence.SelectScope(
            Catalog,7,ReleaseCandidateEvidence.ProductTracks);

        Assert.NotEmpty(Scope.RequiredFeatureIds);
        Assert.NotEmpty(Scope.RequiredStageIds);
        Assert.DoesNotContain("release.candidate",Scope.RequiredStageIds);
        Assert.DoesNotContain("release.manifest",Scope.RequiredStageIds);
        Assert.All(Scope.RequiredStageIds,StageId=>
            Assert.Equal(FeatureStageAvailability.Executable,Catalog.GetStage(StageId).Availability));
        Assert.DoesNotContain(Scope.RequiredFeatureIds,FeatureId=>
            Catalog.GetFeature(FeatureId).Status is
                FeatureLifecycleStatus.Specified or FeatureLifecycleStatus.Deferred);
        Assert.Equal(
            Scope.RequiredStageIds.Order(StringComparer.Ordinal),Scope.RequiredStageIds);
        Assert.Equal(
            Scope.RequiredFeatureIds.Order(StringComparer.Ordinal),Scope.RequiredFeatureIds);
    }

    [Fact]
    public void LowerLevelSingleTrackExcludesUnreachedAndUnselectedFeatures(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();

        ReleaseCandidateScope Scope=ReleaseCandidateEvidence.SelectScope(
            Catalog,3,[FeatureRoadmapTrack.Rendering]);

        Assert.All(Scope.RequiredFeatureIds,FeatureId=>
            Assert.InRange(Catalog.GetFeature(FeatureId).DependencyLevel,0,3));
        Assert.Contains("F-L4-004",Scope.RequiredFeatureIds);
        Assert.DoesNotContain("F-L4-006",Scope.RequiredFeatureIds);
        Assert.DoesNotContain("F-L7-045",Scope.RequiredFeatureIds);
        Assert.DoesNotContain("release.packages",Scope.RequiredStageIds);
    }

    [Fact]
    public async Task RunPublishesOneSelfValidatingLevelGatedClosure(){
        string ReportPath=await this.CreateEvidenceAsync();
        ReleaseCandidateReport Report=ReadReport(ReportPath);
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        ReleaseCandidateScope Expected=ReleaseCandidateEvidence.SelectScope(
            Catalog,7,ReleaseCandidateEvidence.ProductTracks);

        Assert.Empty(await ReleaseCandidateEvidence.ValidateAsync(
            this.Root,ReportPath,Catalog));
        Assert.Equal("Release",Report.Configuration);
        Assert.Equal("Warp",Report.Adapter);
        Assert.Equal(7,Report.ReachedLevel);
        Assert.Equal(Expected.Tracks,Report.Tracks);
        Assert.Equal(Expected.RequiredFeatureIds,
            Report.Features.Select(static Feature=>Feature.FeatureId));
        Assert.Equal(Expected.RequiredStageIds,
            Report.Stages.Select(static Stage=>Stage.StageId));
        Assert.Equal(
            ReleaseCandidateEvidence.RequiredRepositoryInputPaths.Count(),
            Report.Inputs.Count);
    }

    [Fact]
    public async Task MissingFailedStaleOrOrdinaryProfileEvidenceFailsClosed(){
        this.CreateInputs();
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        ReleaseCandidateScope Scope=ReleaseCandidateEvidence.SelectScope(
            Catalog,7,ReleaseCandidateEvidence.ProductTracks);
        string Missing=Scope.RequiredStageIds[0];
        string Failed=Scope.RequiredStageIds[1];
        string Stale=Scope.RequiredStageIds[2];
        string Ordinary=Scope.RequiredStageIds[3];
        foreach(string StageId in Scope.RequiredStageIds.Skip(1)){
            string Profile=StageId==Ordinary
                ?"full":ReleaseCandidateEvidence.PrerequisiteProfile;
            FeatureStageRunStatus Status=StageId==Failed
                ?FeatureStageRunStatus.Failed:FeatureStageRunStatus.Passed;
            DateTimeOffset Finished=StageId==Stale
                ?DateTimeOffset.UtcNow-ReleaseCandidateEvidence.MaximumEvidenceAge-TimeSpan.FromMinutes(1)
                :DateTimeOffset.UtcNow;
            await this.SaveStageAsync(Catalog.GetStage(StageId),Profile,Status,Finished);
        }
        var Error=new StringWriter();

        int ExitCode=await ReleaseCandidateEvidence.RunAsync(
            this.Root,Catalog,7,ReleaseCandidateEvidence.ProductTracks,
            TextWriter.Null,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        string Text=Error.ToString();
        Assert.Contains(Missing,Text,StringComparison.Ordinal);
        Assert.Contains(Failed,Text,StringComparison.Ordinal);
        Assert.Contains(Stale,Text,StringComparison.Ordinal);
        Assert.Contains(Ordinary,Text,StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(this.Root,
            ".artifacts","ReleaseCandidates")));
    }

    [Fact]
    public async Task ChangedInputResultOrExtraEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        ReleaseCandidateReport Report=ReadReport(ReportPath);
        await File.AppendAllTextAsync(
            Resolve(this.Root,Report.Inputs[0].Path),"changed",Encoding.UTF8);
        await File.AppendAllTextAsync(
            Resolve(this.Root,Report.Stages[0].ResultPath)," ",Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(ReportPath)!,"extra.txt"),"extra");

        IReadOnlyList<string>Errors=await ReleaseCandidateEvidence.ValidateAsync(
            this.Root,ReportPath,FeatureStageCatalog.CreateDefault());

        Assert.Contains(Errors,Error=>Error.Contains(
            "input changed",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "result file",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputs();
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        ReleaseCandidateScope Scope=ReleaseCandidateEvidence.SelectScope(
            Catalog,7,ReleaseCandidateEvidence.ProductTracks);
        foreach(string StageId in Scope.RequiredStageIds){
            await this.SaveStageAsync(
                Catalog.GetStage(StageId),ReleaseCandidateEvidence.PrerequisiteProfile,
                FeatureStageRunStatus.Passed,DateTimeOffset.UtcNow);
        }
        var Error=new StringWriter();
        int ExitCode=await ReleaseCandidateEvidence.RunAsync(
            this.Root,Catalog,7,ReleaseCandidateEvidence.ProductTracks,
            TextWriter.Null,Error,CancellationToken.None);
        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(
            Path.Combine(this.Root,".artifacts","ReleaseCandidates"),
            ReleaseCandidateEvidence.ReportFileName,SearchOption.AllDirectories));
    }

    private async Task SaveStageAsync(
        FeatureStageDefinition Stage,string Profile,FeatureStageRunStatus Status,
        DateTimeOffset FinishedUtc){
        var Options=new FeatureStageExecutionOptions(
            this.Root,"Release",Profile,FeatureAdapter.Warp,
            Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select(
            (Command,Index)=>new FeatureStageAssertion(
                $"command:{Index}",Command.FileName,Status==FeatureStageRunStatus.Passed))];
        FeatureStageRunResult Result=FeatureStageRunResult.Create(
            Stage,Options,Status,Stage.Oracle,Assertions,null,null,
            Status==FeatureStageRunStatus.Passed?[]:["fixture failure"],
            StartedUtc:FinishedUtc-TimeSpan.FromSeconds(1),FinishedUtc:FinishedUtc);
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(
            FeatureRunResultDocument.Create(Profile,[Result]),CancellationToken.None);
    }

    private void CreateInputs(){
        foreach(string Relative in ReleaseCandidateEvidence.RequiredRepositoryInputPaths){
            string Destination=Resolve(this.Root,Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.WriteAllText(
                Destination,"fixture:"+Relative,new UTF8Encoding(false));
        }
    }

    private static ReleaseCandidateReport ReadReport(string PathValue)=>
        JsonSerializer.Deserialize<ReleaseCandidateReport>(
            File.ReadAllBytes(PathValue),new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            })
        ??throw new InvalidDataException("Release candidate report is empty.");

    private static string Resolve(string Root,string Relative)=>Path.Combine(
        Root,Relative.Replace('/',Path.DirectorySeparatorChar));
}
