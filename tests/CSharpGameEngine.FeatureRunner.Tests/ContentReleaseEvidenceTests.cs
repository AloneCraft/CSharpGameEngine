using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGameEngine.Content;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ContentReleaseEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(
        Path.GetTempPath(),"CgeContentReleaseEvidenceTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Run_PublishesOneSelfValidatingContentClosure(){
        string ReportPath=await this.CreateEvidenceAsync();

        IReadOnlyList<string>Errors=await ContentReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);
        ContentReleaseReport Report=ReadReport(ReportPath);
        ShippingContentPackage Package=await ShippingContentPackage.OpenAsync(
            Path.Combine(Path.GetDirectoryName(ReportPath)!,Report.PackageRelativePath));

        Assert.Empty(Errors);
        Assert.Equal(ContentReleaseEvidence.ReportSchema,Report.Schema);
        Assert.Equal("Debug",Report.Configuration);
        Assert.Equal(7,Package.Manifest.Entries.Length);
        Assert.Equal(7,Package.Manifest.Entries.Select(static Entry=>Entry.Kind).Distinct().Count());
        Assert.Equal(2,Report.Performance.Count);
        Assert.Equal(
            ContentReleaseEvidence.RequiredRepositoryInputPaths.Order(StringComparer.Ordinal),
            Report.Inputs.Select(static Input=>Input.Path).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Validate_RejectsChangedSchemaOrSbomInput(){
        string ReportPath=await this.CreateEvidenceAsync();
        string InputPath=Path.Combine(
            this.Root,ContentReleaseEvidence.RequiredRepositoryInputPaths.First()
                .Replace('/',Path.DirectorySeparatorChar));

        await File.AppendAllTextAsync(InputPath,"changed",Encoding.UTF8);
        IReadOnlyList<string>Errors=await ContentReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "input changed",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_RejectsChangedPerformanceEvidence(){
        string ReportPath=await this.CreateEvidenceAsync();
        ContentReleaseReport Report=ReadReport(ReportPath);
        string PerformancePath=Path.Combine(
            this.Root,Report.Performance[0].ReportPath.Replace('/',Path.DirectorySeparatorChar));

        await File.AppendAllTextAsync(PerformancePath," ",Encoding.UTF8);
        IReadOnlyList<string>Errors=await ContentReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "performance report file SHA-256 mismatch",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_RejectsChangedPackageAndExtraEvidenceFiles(){
        string ReportPath=await this.CreateEvidenceAsync();
        ContentReleaseReport Report=ReadReport(ReportPath);
        string EvidenceRoot=Path.GetDirectoryName(ReportPath)!;
        ContentReleasePackageFile Artifact=Assert.Single(
            Report.PackageFiles.Where(static Entry=>Entry.Path.EndsWith(
                ".cgea",StringComparison.Ordinal))
                .Take(1));
        string ArtifactPath=Path.Combine(
            EvidenceRoot,Report.PackageRelativePath,
            Artifact.Path.Replace('/',Path.DirectorySeparatorChar));
        byte[]Bytes=await File.ReadAllBytesAsync(ArtifactPath);
        Bytes[^1]^=0x01;
        await File.WriteAllBytesAsync(ArtifactPath,Bytes);
        await File.WriteAllTextAsync(Path.Combine(EvidenceRoot,"unexpected.txt"),"extra");

        IReadOnlyList<string>Errors=await ContentReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "package",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains(
            "missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Validate_RejectsNonCanonicalReportBytes(){
        string ReportPath=await this.CreateEvidenceAsync();

        await File.AppendAllTextAsync(ReportPath," ",Encoding.UTF8);
        IReadOnlyList<string>Errors=await ContentReleaseEvidence.ValidateAsync(
            this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains(
            "not canonically encoded",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Run_FailsClosedWhenOneRequiredPerformanceReportIsMissing(){
        this.CreateInputs();
        await this.CreatePerformanceReportAsync("content.cold-compile");
        var Output=new StringWriter();
        var Error=new StringWriter();

        int ExitCode=await ContentReleaseEvidence.RunAsync(
            this.Root,"Debug",Output,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains("content.one-change",Error.ToString(),StringComparison.Ordinal);
        string EvidenceRoot=Path.Combine(this.Root,".artifacts","ContentRelease");
        Assert.False(Directory.Exists(EvidenceRoot)
            &&Directory.EnumerateFileSystemEntries(EvidenceRoot).Any());
    }

    public void Dispose(){
        if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); }
    }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputs();
        await this.CreatePerformanceReportAsync("content.cold-compile");
        await this.CreatePerformanceReportAsync("content.one-change");
        var Output=new StringWriter();
        var Error=new StringWriter();

        int ExitCode=await ContentReleaseEvidence.RunAsync(
            this.Root,"Debug",Output,Error,CancellationToken.None);

        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(
            Path.Combine(this.Root,".artifacts","ContentRelease"),
            ContentReleaseEvidence.ReportFileName,SearchOption.AllDirectories));
    }

    private void CreateInputs(){
        foreach(string RelativePath in ContentReleaseEvidence.RequiredRepositoryInputPaths){
            string PathValue=Path.Combine(
                this.Root,RelativePath.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(PathValue)!);
            File.WriteAllText(PathValue,"fixture:"+RelativePath,new UTF8Encoding(false));
        }
    }

    private async Task CreatePerformanceReportAsync(string Id){
        var Plan=new PerformanceMeasurementPlan(
            Id,"Debug","nanoseconds",
            new SortedDictionary<string,string>(StringComparer.Ordinal){
                ["fixture"]="content-release",
            },PerformanceMeasurementFramework.RequiredWarmupCount,
            PerformanceMeasurementFramework.RequiredSampleCount,
            new PerformanceAbsoluteBudget(1000,1000,1000),null);
        PerformanceSample[]Samples=[.. Enumerable.Range(
            0,PerformanceMeasurementFramework.RequiredSampleCount)
            .Select(static Index=>new PerformanceSample(Index,100+Index,10+Index))];
        PerformanceAnalysis Analysis=PerformanceMeasurementFramework.Analyze(Plan,Samples);
        PerformanceMachineFingerprint Machine=
            PerformanceMeasurementFramework.CaptureMachineFingerprint();
        var Payload=new PerformanceReportPayload(
            PerformanceFrameworkProbe.ReportSchema,DateTimeOffset.UtcNow,Plan,Samples,Analysis,
            Machine,"unknown",true);
        _=await PerformanceReportStore.SaveAsync(
            this.Root,Payload,CancellationToken.None);
    }

    private static ContentReleaseReport ReadReport(string ReportPath)=>
        JsonSerializer.Deserialize<ContentReleaseReport>(
            File.ReadAllBytes(ReportPath),CreateJsonOptions())
        ??throw new InvalidDataException("Content release report is empty.");

    private static JsonSerializerOptions CreateJsonOptions(){
        var Options=new JsonSerializerOptions {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return Options;
    }
}
