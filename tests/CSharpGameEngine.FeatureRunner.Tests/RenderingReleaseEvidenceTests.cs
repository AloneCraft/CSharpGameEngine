using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class RenderingReleaseEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeRenderingReleaseTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunPublishesOneSelfValidatingRenderingClosure(){
        string ReportPath=await this.CreateEvidenceAsync();
        RenderingReleaseReport Report=ReadReport(ReportPath);

        Assert.Empty(await RenderingReleaseEvidence.ValidateAsync(this.Root,ReportPath));
        Assert.Equal("Debug",Report.Configuration);
        Assert.Equal("rendering.performance",Report.PerformanceStage.StageId);
        Assert.Equal("vsix.rendering-preview",Report.PreviewStage.StageId);
        Assert.Equal(new string('c',64),Report.RawReport.ShaderArtifactSha256);
        Assert.Equal(
            ["rendering.visibility-100k","rendering.warp-frame"],
            Report.PerformanceReports.Select(static Evidence=>Evidence.PlanId));
        Assert.All(Report.PerformanceReports,
            static Evidence=>Assert.Equal(new string('c',64),Evidence.ShaderArtifactSha256));
        Assert.Equal(
            RenderingReleaseEvidence.RequiredRepositoryInputPaths.Count(),Report.Inputs.Count);
    }

    [Fact]
    public async Task ChangedInputPerformanceOrStageEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        RenderingReleaseReport Report=ReadReport(ReportPath);
        string Input=Resolve(this.Root,Report.Inputs[^1].Path);
        string Performance=Resolve(this.Root,Report.PerformanceReports[0].ReportPath);
        string Raw=Resolve(this.Root,Report.RawReport.ReportPath);
        string Stage=Resolve(this.Root,Report.PreviewStage.ReportPath);
        await File.AppendAllTextAsync(Input,"changed",Encoding.UTF8);
        await File.AppendAllTextAsync(Performance," ",Encoding.UTF8);
        await File.AppendAllTextAsync(Raw," ",Encoding.UTF8);
        await File.AppendAllTextAsync(Stage," ",Encoding.UTF8);

        IReadOnlyList<string>Errors=await RenderingReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("input changed",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("performance report file",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("raw report",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("preview result file",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonCanonicalOrExtraEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        await File.AppendAllTextAsync(ReportPath," ",Encoding.UTF8);
        await File.WriteAllTextAsync(
            Path.Combine(Path.GetDirectoryName(ReportPath)!,"extra.txt"),"extra");

        IReadOnlyList<string>Errors=await RenderingReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("not canonically encoded",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingOrOrdinaryProfilePrerequisiteFailsWithoutPartialEvidence(){
        this.CreateInputs();
        await this.CreatePrerequisitesAsync("full","full");
        var Error=new StringWriter();

        int ExitCode=await RenderingReleaseEvidence.RunAsync(
            this.Root,"Debug",TextWriter.Null,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains("rendering-release",Error.ToString(),StringComparison.Ordinal);
        string EvidenceRoot=Path.Combine(this.Root,".artifacts","RenderingRelease");
        Assert.False(Directory.Exists(EvidenceRoot)
            &&Directory.EnumerateFileSystemEntries(EvidenceRoot).Any());
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputs();
        await this.CreatePrerequisitesAsync(
            RenderingReleaseEvidence.PerformanceProfile,
            RenderingReleaseEvidence.PreviewProfile);
        var Error=new StringWriter();
        int ExitCode=await RenderingReleaseEvidence.RunAsync(
            this.Root,"Debug",TextWriter.Null,Error,CancellationToken.None);
        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(
            Path.Combine(this.Root,".artifacts","RenderingRelease"),
            RenderingReleaseEvidence.ReportFileName,SearchOption.AllDirectories));
    }

    private async Task CreatePrerequisitesAsync(
        string PerformanceProfile,string PreviewProfile){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        (string RawPath,string RawHash)=await this.CreateRawReportAsync();
        string[]PerformancePaths=[
            await this.CreatePerformanceReportAsync("rendering.warp-frame",RawHash),
            await this.CreatePerformanceReportAsync("rendering.visibility-100k",RawHash),
        ];
        string StandardOutput=string.Join(Environment.NewLine,[
            $"Rendering performance raw report: {RawPath}",
            .. PerformancePaths.Select(Path=>$"probe report={Path}"),
        ]);
        await this.SaveStageAsync(
            Catalog.GetStage("rendering.performance"),PerformanceProfile,StandardOutput);
        await this.SaveStageAsync(
            Catalog.GetStage("vsix.rendering-preview"),PreviewProfile,string.Empty);
    }

    private async Task<string>CreatePerformanceReportAsync(string Id,string RawHash){
        var Metadata=new SortedDictionary<string,string>(StringComparer.Ordinal){
            ["adapter"]="warp",["adapterDescription"]="fixture-adapter",
            ["debugLayer"]="enabled",["pixelOracle"]="triangle-readback",
            ["rawReportSha256"]=RawHash,["resolution"]="1280x720",
            ["runtimeMode"]=RenderingPerformanceProbe.RequiredRuntimeMode,
            ["shaderArtifactSha256"]=new string('c',64),
            ["timingGate"]="correctness-only",
        };
        if(Id=="rendering.visibility-100k"){
            Metadata["activeInstances"]="100000";
            Metadata["expectedBatches"]="1";
        }
        var Plan=new PerformanceMeasurementPlan(
            Id,"Debug","nanoseconds",Metadata,
            PerformanceMeasurementFramework.RequiredWarmupCount,
            PerformanceMeasurementFramework.RequiredSampleCount,
            new PerformanceAbsoluteBudget(long.MaxValue,long.MaxValue,
                Id=="rendering.visibility-100k"?0:1_000_000),null);
        PerformanceSample[]Samples=[.. Enumerable.Range(0,30)
            .Select(Index=>new PerformanceSample(Index,100_000+Index,0))];
        PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);
        var Payload=new PerformanceReportPayload(
            PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.UtcNow,Plan,Samples,Analysis,
            PerformanceMeasurementFramework.CaptureMachineFingerprint(),"unknown",true);
        return await PerformanceReportStore.SaveAsync(
            this.Root,Payload,CancellationToken.None);
    }

    private async Task<(string Path,string Hash)>CreateRawReportAsync(){
        PerformanceSample[]Samples=[.. Enumerable.Range(0,30)
            .Select(Index=>new PerformanceSample(Index,100_000+Index,0))];
        var Report=new RenderingRawPerformanceReport(
            RenderingPerformanceProbe.RawSchema,"Debug","warp","fixture-adapter",
            RenderingPerformanceProbe.RequiredRuntimeMode,new string('c',64),
            1280,720,0,true,[
                new RenderingRawWorkload("rendering.frame-1280x720",5,Samples),
                new RenderingRawWorkload("rendering.visibility-100k",5,Samples),
            ]);
        string DirectoryPath=Path.Combine(
            this.Root,".artifacts","RenderingPerformance");
        Directory.CreateDirectory(DirectoryPath);
        string PathValue=Path.Combine(DirectoryPath,"fixture.raw.json");
        byte[]Json=JsonSerializer.SerializeToUtf8Bytes(Report,new JsonSerializerOptions {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true,
        });
        byte[]Canonical=new byte[Json.Length+1];
        Json.CopyTo(Canonical,0);Canonical[^1]=(byte)'\n';
        await File.WriteAllBytesAsync(PathValue,Canonical);
        return (PathValue,Convert.ToHexString(SHA256.HashData(Canonical)).ToLowerInvariant());
    }

    private async Task SaveStageAsync(
        FeatureStageDefinition Stage,string Profile,string StandardOutput){
        var Options=new FeatureStageExecutionOptions(
            this.Root,"Debug",Profile,FeatureAdapter.Warp,
            Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select(
            (Command,Index)=>new FeatureStageAssertion($"command:{Index}",Command.FileName,true))];
        FeatureStageRunResult Result=FeatureStageRunResult.Create(
            Stage,Options,FeatureStageRunStatus.Passed,Stage.Oracle,
            Assertions,null,null,StandardOutput:StandardOutput);
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(
            FeatureRunResultDocument.Create(Profile,[Result]),CancellationToken.None);
    }

    private void CreateInputs(){
        foreach(string Relative in RenderingReleaseEvidence.RequiredRepositoryInputPaths){
            string Destination=Resolve(this.Root,Relative);
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            File.WriteAllText(Destination,"fixture:"+Relative,new UTF8Encoding(false));
        }
    }

    private static RenderingReleaseReport ReadReport(string PathValue)=>
        JsonSerializer.Deserialize<RenderingReleaseReport>(
            File.ReadAllBytes(PathValue),new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
            })
        ??throw new InvalidDataException("Rendering release report is empty.");

    private static string Resolve(string Root,string Relative)=>Path.Combine(
        Root,Relative.Replace('/',Path.DirectorySeparatorChar));
}
