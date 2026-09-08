using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class PlatformReleaseEvidenceTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"CgePlatformReleaseTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task RunPublishesOneSelfValidatingScopedClosure(){
        string ReportPath=await this.CreateEvidenceAsync();
        PlatformReleaseReport Report=ReadReport(ReportPath);

        Assert.Empty(await PlatformReleaseEvidence.ValidateAsync(this.Root,ReportPath));
        Assert.Equal("Debug",Report.Configuration);
        Assert.Equal("Warp",Report.Compatibility.Adapter);
        Assert.Equal(18,Report.Inputs.Count);
        Assert.Equal(["hardware-audio","hardware-d3d12"],Report.OptionalHardware.Select(static Entry=>Entry.Id));
        Assert.Equal("Silent",Report.RequiredModes.Audio);
    }

    [Fact]
    public async Task ChangedInputOrCompatibilityResultFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        PlatformReleaseReport Report=ReadReport(ReportPath);
        string InputPath=Path.Combine(this.Root,Report.Inputs.Last().Path.Replace('/',Path.DirectorySeparatorChar));
        await File.AppendAllTextAsync(InputPath,"changed",Encoding.UTF8);
        string StagePath=Path.Combine(this.Root,Report.Compatibility.ReportPath.Replace('/',Path.DirectorySeparatorChar));
        await File.AppendAllTextAsync(StagePath," ",Encoding.UTF8);

        IReadOnlyList<string>Errors=await PlatformReleaseEvidence.ValidateAsync(this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("input changed",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("result file SHA-256 mismatch",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task NonCanonicalOrExtraEvidenceFailsValidation(){
        string ReportPath=await this.CreateEvidenceAsync();
        await File.AppendAllTextAsync(ReportPath," ",Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(ReportPath)!,"extra.txt"),"extra");

        IReadOnlyList<string>Errors=await PlatformReleaseEvidence.ValidateAsync(this.Root,ReportPath);

        Assert.Contains(Errors,Error=>Error.Contains("not canonically encoded",StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Errors,Error=>Error.Contains("missing or extra",StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task MissingPassingCompatibilityResultFailsWithoutPartialEvidence(){
        this.CreateInputs();
        var Output=new StringWriter();
        var Error=new StringWriter();

        int ExitCode=await PlatformReleaseEvidence.RunAsync(this.Root,"Debug",Output,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains("recent passing platform.compatibility",Error.ToString(),StringComparison.Ordinal);
        string EvidenceRoot=Path.Combine(this.Root,".artifacts","PlatformRelease");
        Assert.False(Directory.Exists(EvidenceRoot)&&Directory.EnumerateFileSystemEntries(EvidenceRoot).Any());
    }

    [Fact]
    public async Task OrdinaryCompatibilityProfileCannotSubstituteForReleasePrerequisite(){
        this.CreateInputs();
        FeatureStageDefinition Stage=FeatureStageCatalog.CreateDefault().GetStage("platform.compatibility");
        var Options=new FeatureStageExecutionOptions(this.Root,"Debug","full",FeatureAdapter.Warp,Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select((Command,Index)=>new FeatureStageAssertion($"command:{Index}",Command.FileName,true))];
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(FeatureRunResultDocument.Create(Options.Profile,[FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.Passed,Stage.Oracle,Assertions,null,null)]),CancellationToken.None);
        var Error=new StringWriter();

        int ExitCode=await PlatformReleaseEvidence.RunAsync(this.Root,"Debug",TextWriter.Null,Error,CancellationToken.None);

        Assert.Equal(1,ExitCode);
        Assert.Contains("platform-release-prerequisite",Error.ToString(),StringComparison.Ordinal);
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private async Task<string>CreateEvidenceAsync(){
        this.CreateInputs();
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();
        FeatureStageDefinition Stage=Catalog.GetStage("platform.compatibility");
        var Options=new FeatureStageExecutionOptions(this.Root,"Debug","platform-release-prerequisite",FeatureAdapter.Warp,Stage.Seed,Stage.Ticks,"unknown",true);
        FeatureStageAssertion[]Assertions=[.. Stage.Commands.Select((Command,Index)=>new FeatureStageAssertion($"command:{Index}",Command.FileName,true))];
        FeatureStageRunResult Result=FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.Passed,Stage.Oracle,Assertions,null,null);
        var Store=new FeatureRunResultStore(this.Root);
        _=await Store.SaveAsync(FeatureRunResultDocument.Create(Options.Profile,[Result]),CancellationToken.None);
        var Output=new StringWriter();
        var Error=new StringWriter();

        int ExitCode=await PlatformReleaseEvidence.RunAsync(this.Root,"Debug",Output,Error,CancellationToken.None);

        Assert.True(ExitCode==0,Error.ToString());
        return Assert.Single(Directory.EnumerateFiles(Path.Combine(this.Root,".artifacts","PlatformRelease"),PlatformReleaseEvidence.ReportFileName,SearchOption.AllDirectories));
    }

    private void CreateInputs(){
        string Source=FeatureStageRepositoryLocator.Find();
        foreach(string Relative in PlatformReleaseEvidence.RequiredRepositoryInputPaths){
            string Destination=Path.Combine(this.Root,Relative.Replace('/',Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            string SourcePath=Path.Combine(Source,Relative.Replace('/',Path.DirectorySeparatorChar));
            if(Relative is "Docs/Verification/Platform-Compatibility-Matrix.json" or "Docs/Verification/CI-Matrix.json" or "Docs/Verification/Manual-Evidence.json" or ".github/workflows/ci.yml"){
                File.Copy(SourcePath,Destination,true);
            } else { File.WriteAllText(Destination,"fixture:"+Relative,new UTF8Encoding(false)); }
        }
    }

    private static PlatformReleaseReport ReadReport(string PathValue)=>
        JsonSerializer.Deserialize<PlatformReleaseReport>(File.ReadAllBytes(PathValue),CreateJsonOptions())
        ??throw new InvalidDataException("Platform release report is empty.");

    private static JsonSerializerOptions CreateJsonOptions(){
        var Options=new JsonSerializerOptions {
            PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
        };
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return Options;
    }
}
