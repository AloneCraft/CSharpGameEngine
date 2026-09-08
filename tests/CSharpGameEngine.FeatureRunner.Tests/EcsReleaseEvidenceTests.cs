using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class EcsReleaseEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeEcsReleaseTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunPublishesOneSelfValidatingEcsClosure(){
        string ReportPath=await this.CreateEvidenceAsync();
        EcsReleaseReport Report=ReadReport(ReportPath);

        Assert.Empty(await EcsReleaseEvidence.ValidateAsync(
            this.Root,ReportPath));
        Assert.Equal("Debug",Report.Configuration);
        Assert.Equal(
            ["foundation.deterministic-jobs","ecs.schedule-diagnostics",
                "ecs.snapshot","ecs.performance"],
            Report.Stages.Select(static Stage=>Stage.StageId));
        Assert.Equal([1,64,65,128],
            Report.RawReports.Select(static Report=>Report.ComponentTypeCount));
        Assert.Equal([1,64,65,128],
            Report.PerformanceReports.Select(
                static Report=>Report.ComponentTypeCount));
        Assert.Equal(
            EcsReleaseEvidence.RequiredRepositoryInputPaths.Count(),
            Report.Inputs.Count);
        Directory.Delete(Path.Combine(
            this.Root,"tools","CSharpGameEngine.Ecs.Benchmarks","obj"),true);
        Assert.Empty(await EcsReleaseEvidence.ValidateAsync(
            this.Root,ReportPath));
    }

    [Fact]
    public async Task ChangedInputRawPerformanceOrStageEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        EcsReleaseReport Report=ReadReport(ReportPath);
        string Input=Resolve(this.Root,Report.Inputs[^1].Path);
        string Raw=Resolve(this.Root,Report.RawReports[0].ReportPath);
        string Performance=Resolve(
            this.Root,Report.PerformanceReports[0].ReportPath);
        string Stage=Resolve(this.Root,Report.Stages[0].ReportPath);
        await File.AppendAllTextAsync(Input,"changed",Encoding.UTF8);
        await File.AppendAllTextAsync(Raw," ",Encoding.UTF8);
        await File.AppendAllTextAsync(Performance," ",Encoding.UTF8);
        await File.AppendAllTextAsync(Stage," ",Encoding.UTF8);

        IReadOnlyList<string>Errors=await EcsReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "input changed",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "raw report",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "performance report",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "result file",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonCanonicalOrExtraEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        await File.AppendAllTextAsync(ReportPath," ",Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(ReportPath)!,"extra.txt"),
            "extra");

        IReadOnlyList<string>Errors=await EcsReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "not canonically encoded",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingOrOrdinaryProfilePrerequisitesFailWithoutPartialEvidence(){
        this.CreateInputs();
        this.CreateGeneratedSources();
        await this.CreatePrerequisitesAsync(
            "full","full","full","full");
        var Error=new StringWriter();

        int ExitCode=await EcsReleaseEvidence.RunAsync(
            this.Root,"Debug",TextWriter.Null,Error,
            CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains("ecs-release",Error.ToString(),
            StringComparison.Ordinal);
        string EvidenceRoot=Path.Combine(
            this.Root,".artifacts","EcsRelease");
        Assert.False(Directory.Exists(EvidenceRoot)
            &&Directory.EnumerateFileSystemEntries(EvidenceRoot).Any());
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputs();
        this.CreateGeneratedSources();
        await this.CreatePrerequisitesAsync(
            EcsReleaseEvidence.ReplayProfile,
            EcsReleaseEvidence.ScheduleProfile,
            EcsReleaseEvidence.SnapshotProfile,
            EcsReleaseEvidence.PerformanceProfile);
        var Error=new StringWriter();

        int ExitCode=await EcsReleaseEvidence.RunAsync(
            this.Root,"Debug",TextWriter.Null,Error,
            CancellationToken.None);

        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(
            Path.Combine(this.Root,".artifacts","EcsRelease"),
            EcsReleaseEvidence.ReportFileName,
            SearchOption.AllDirectories));
    }

    private async Task CreatePrerequisitesAsync(
        string ReplayProfile,string ScheduleProfile,
        string SnapshotProfile,string PerformanceProfile){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        DateTimeOffset PerformanceStartedUtc=DateTimeOffset.UtcNow;
        var RawPaths=new List<string>();
        var PerformancePaths=new List<string>();
        foreach(int Count in new[]{1,64,65,128}){
            (string RawPath,EcsRawPerformanceReport Raw,string RawHash)=
                await this.CreateRawReportAsync(Count);
            RawPaths.Add(RawPath);
            PerformancePaths.Add(await this.CreatePerformanceReportAsync(
                Raw,RawHash));
        }
        string PerformanceOutput=string.Join(Environment.NewLine,[
            .. RawPaths.Select((PathValue,Index)=>
                $"ECS performance raw report: count={new[]{1,64,65,128}[Index]} path={PathValue}"),
            .. PerformancePaths.Select(PathValue=>
                $"fixture report={PathValue}"),
        ]);
        await this.SaveStageAsync(
            Catalog.GetStage("foundation.deterministic-jobs"),
            ReplayProfile,string.Empty);
        await this.SaveStageAsync(
            Catalog.GetStage("ecs.schedule-diagnostics"),
            ScheduleProfile,string.Empty);
        await this.SaveStageAsync(
            Catalog.GetStage("ecs.snapshot"),
            SnapshotProfile,string.Empty);
        await this.SaveStageAsync(
            Catalog.GetStage("ecs.performance"),
            PerformanceProfile,PerformanceOutput,PerformanceStartedUtc);
    }

    private void CreateGeneratedSources(){
        foreach(int Count in new[]{1,64,65,128}){
            string DirectoryPath=Path.Combine(
                this.Root,"tools","CSharpGameEngine.Ecs.Benchmarks",
                "obj","Debug","net10.0","generated",
                $"ecs-{Count}","fixture");
            Directory.CreateDirectory(DirectoryPath);
            string Shape=Count>64
                ?"internal ulong ComponentEnabledHigh;\n"
                :"internal ulong ComponentEnabledLow;\n";
            File.WriteAllText(
                Path.Combine(DirectoryPath,"GameSystem.Types.g.cs"),
                $"// count={Count}\n{Shape}",new UTF8Encoding(false));
        }
    }

    private async Task<(string Path,EcsRawPerformanceReport Report,string Hash)>
        CreateRawReportAsync(int ComponentTypeCount){
        (string SourceSha,bool ContainsHigh)=
            EcsPerformanceProbe.ReadGeneratedSourceEvidence(
                this.Root,"Debug",ComponentTypeCount);
        PerformanceSample[]Samples=[.. Enumerable.Range(0,30)
            .Select(Index=>new PerformanceSample(
                Index,100_000+Index,0))];
        var Report=new EcsRawPerformanceReport(
            EcsPerformanceProbe.RawSchema,"Debug",
            EcsPerformanceProbe.RequiredRuntimeMode,
            ComponentTypeCount,$"C{ComponentTypeCount-1}",100_000,1,
            SourceSha,
            EcsPerformanceProbe.ComputeExpectedStateSha256(
                ComponentTypeCount),
            ComponentTypeCount<=64?1:2,ContainsHigh,
            new EcsRawWorkload(
                $"ecs.generated-query-{ComponentTypeCount}",5,Samples));
        string DirectoryPath=Path.Combine(
            this.Root,".artifacts","EcsPerformance");
        Directory.CreateDirectory(DirectoryPath);
        string PathValue=Path.Combine(
            DirectoryPath,$"fixture-{ComponentTypeCount}.raw.json");
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(
            Report,new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true,
            });
        byte[]Canonical=new byte[Json.Length+1];
        Json.CopyTo(Canonical,0);
        Canonical[^1]=(byte)'\n';
        await File.WriteAllBytesAsync(PathValue,Canonical);
        return (
            PathValue,Report,
            Convert.ToHexString(SHA256.HashData(Canonical))
                .ToLowerInvariant());
    }

    private async Task<string>CreatePerformanceReportAsync(
        EcsRawPerformanceReport Raw,string RawHash){
        PerformanceBaseline?Baseline=Raw.ComponentTypeCount==64
            ?new PerformanceBaseline(
                100_015,100_028,0,2000)
            :null;
        PerformanceMeasurementPlan Plan=
            EcsPerformanceProbe.CreatePlan(Raw,RawHash,Baseline);
        PerformanceAnalysis Analysis=
            PerformanceMeasurementFramework.Analyze(
                Plan,Raw.Workload.Samples);
        Assert.True(Analysis.Passed);
        var Payload=new PerformanceReportPayload(
            PerformanceFrameworkProbe.ReportSchema,
            DateTimeOffset.UtcNow,Plan,Raw.Workload.Samples,Analysis,
            PerformanceMeasurementFramework.CaptureMachineFingerprint(),
            "unknown",true);
        return await PerformanceReportStore.SaveAsync(
            this.Root,Payload,CancellationToken.None);
    }

    private async Task SaveStageAsync(
        FeatureStageDefinition Stage,string Profile,
        string StandardOutput,DateTimeOffset?StartedUtc=null){
        DateTimeOffset Started=StartedUtc??DateTimeOffset.UtcNow;
        DateTimeOffset Finished=DateTimeOffset.UtcNow;
        if(Finished<Started){ Finished=Started; }
        var Options=new FeatureStageExecutionOptions(
            this.Root,"Debug",Profile,FeatureAdapter.Warp,
            Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select(
            (Command,Index)=>new FeatureStageAssertion(
                $"command:{Index}",Command.FileName,true))];
        FeatureStageRunResult Result=FeatureStageRunResult.Create(
            Stage,Options,FeatureStageRunStatus.Passed,
            Stage.Oracle,Assertions,null,null,
            StandardOutput:StandardOutput,StartedUtc:Started,
            FinishedUtc:Finished);
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(
            FeatureRunResultDocument.Create(Profile,[Result]),
            CancellationToken.None);
    }

    private void CreateInputs(){
        foreach(string Relative in
            EcsReleaseEvidence.RequiredRepositoryInputPaths){
            string Destination=Resolve(this.Root,Relative);
            Directory.CreateDirectory(
                Path.GetDirectoryName(Destination)!);
            File.WriteAllText(
                Destination,"fixture:"+Relative,
                new UTF8Encoding(false));
        }
    }

    private static EcsReleaseReport ReadReport(string PathValue)=>
        JsonSerializer.Deserialize<EcsReleaseReport>(
            File.ReadAllBytes(PathValue),
            new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            })
        ??throw new InvalidDataException(
            "ECS release report is empty.");

    private static string Resolve(
        string Root,string Relative)=>Path.Combine(
            Root,Relative.Replace(
                '/',Path.DirectorySeparatorChar));
}
