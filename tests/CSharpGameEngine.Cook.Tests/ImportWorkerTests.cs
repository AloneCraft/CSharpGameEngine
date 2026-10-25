using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.Json;
using CSharpGameEngine.Cook.Cli;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.Cook.Tests;
public sealed class FixtureImporter:IAssetImporter {
    public ImporterDescriptor Descriptor=>new("test.fixture",1,"Fixture",[".model"],100,"test",ImporterIsolation.PerImport,ImporterDeterminism.Deterministic,ImportCapabilities.Mesh,[],[],[]);
    public ImportSniffResult Sniff(ImportSniffRequest Request)=>ImportSniffResult.Definite;
    public ValueTask<ImportResult>ImportAsync(ImportRequest Request,IImportContext Context,CancellationToken CancellationToken){
        using var Source=Context.OpenRead(Request.SourceUri);
        using var Reader=new StreamReader(Source);
        if(Reader.ReadToEnd()!="triangle"){ throw new InvalidDataException("IMP101: malformed fixture."); }
        Context.TryOpenRead("optional.model",out var Optional);
        Optional?.Dispose();
        return ValueTask.FromResult(new ImportResult(InterchangeTests.Triangle()));
    }
}
public sealed class FixtureProcessor:IModelProcessor {
    public ModelProcessorDescriptor Descriptor=>new("test.corrupt",1,ModelProcessorStage.BeforeWrite,0,ImporterIsolation.Shared,[new("test.corrupt","false","Test adversarial postprocessor",["false","true"])]);
    public bool AppliesTo(InterchangeDocument Document,IProcessContext Context)=>Context.Options.TryGetValue("test.corrupt",out var Value)&&Value=="true";
    public ValueTask<InterchangeDocument>ProcessAsync(InterchangeDocument Document,IProcessContext Context,CancellationToken CancellationToken)=>ValueTask.FromResult(Document with { Meshes=[Document.Meshes[0] with { Indices=[0,1,999] }] });
}
public sealed class ImportWorkerTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-import-テスト #;"+Guid.NewGuid().ToString("N"));
    public ImportWorkerTests()=>Directory.CreateDirectory(this.Root);
    public void Dispose()=>Directory.Delete(this.Root,true);
    [Fact]
    public void Describe_DiscoversOnlyImporterContracts(){
        using var Json=JsonDocument.Parse(ImportWorker.Describe(typeof(FixtureImporter).Assembly.Location));
        Assert.Equal("cge-import-module/1",Json.RootElement.GetProperty("Schema").GetString());
        Assert.Equal("test.fixture",Assert.Single(Json.RootElement.GetProperty("Importers").EnumerateArray()).GetProperty("Identity").GetString());
    }
    [Fact]
    public async Task SeparateWorkers_ProduceIdenticalIrAndReadSets(){
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"triangle");
        var First=await this.RunAsync("first");
        var Second=await this.RunAsync("second");
        Assert.Equal(0,First.ExitCode);
        Assert.Equal(0,Second.ExitCode);
        Assert.Equal(await File.ReadAllBytesAsync(First.Output),await File.ReadAllBytesAsync(Second.Output));
        var Document=InterchangeCodec.Read(await File.ReadAllBytesAsync(First.Output));
        Assert.Equal(InterchangeSpaceState.Engine,Document.Space.State);
        Assert.Equal(new[]{"input.model","optional.model"},Document.Provenance.ReadSet.ToArray());
        Assert.Equal("test.fixture",Document.Provenance.ImporterIdentity);
    }
    [Fact]
    public async Task MalformedInputAndUnknownOptions_FailWithoutPublishingIr(){
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"bad");
        var Bad=await this.RunAsync("bad");
        Assert.Equal(1,Bad.ExitCode);
        Assert.False(File.Exists(Bad.Output));
        Assert.Contains("IMP101",await File.ReadAllTextAsync(Bad.Result));
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"triangle");
        var Unknown=await this.RunAsync("unknown",new() { ["test.unknown"]="1" });
        Assert.Equal(1,Unknown.ExitCode);
        Assert.False(File.Exists(Unknown.Output));
        Assert.Contains("IMP004",await File.ReadAllTextAsync(Unknown.Result));
    }
    [Fact]
    public void ReadContext_RejectsEscapeAndRecordsAbsentDependency(){
        var Context=new ImportFileContext(this.Root,new Dictionary<string,string>(),[]);
        Assert.Throws<InvalidDataException>(()=>Context.OpenRead("../escape.model"));
        Assert.False(Context.TryOpenRead("missing.model",out var Stream));
        Assert.Null(Stream);
        Assert.Contains("missing.model",Context.ReadSet);
    }
    [Fact]
    public async Task PostProcessorCannotPublishInvalidNormalizedGeometry(){
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"triangle");
        var Result=await this.RunAsync("corrupt",new() { ["test.corrupt"]="true" });
        Assert.Equal(1,Result.ExitCode);
        Assert.False(File.Exists(Result.Output));
        Assert.Contains("IMP101",await File.ReadAllTextAsync(Result.Result));
    }
    [Fact]
    public async Task Cancellation_PublishesAnExplicitFailureWithoutIr(){
        var Request=Path.Combine(this.Root,"request.json");
        var Result=Path.Combine(this.Root,"result.json");
        await File.WriteAllTextAsync(Request,"{}");
        using var Cancelled=new CancellationTokenSource();
        Cancelled.Cancel();
        var Exit=await ImportWorker.RunAsync(typeof(FixtureImporter).Assembly.Location,Request,Result,Cancelled.Token).WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1,Exit);
        Assert.Contains("IMP802",await File.ReadAllTextAsync(Result));
    }
    [Fact]
    public void DiagnosticCap_CannotDiscardAnErrorBehindWarnings(){
        var Diagnostics=new List<ImportDiagnostic>();
        var Context=new ImportFileContext(this.Root,new Dictionary<string,string>(),Diagnostics);
        for(var Index=0;Index<300;Index++){ Context.Report(new(ImportSeverity.Warning,"IMP150","test",ImportPhase.Read,"warning",new("input.model"))); }
        Context.Report(new(ImportSeverity.Error,"IMP101","test",ImportPhase.Read,"bad",new("input.model")));
        Assert.Equal(256,Diagnostics.Count);
        Assert.Contains(Diagnostics,D=>D.Severity==ImportSeverity.Error);
    }
    [Fact]
    public async Task FailedReimport_KeepsThePreviouslyWrittenIr(){
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"triangle");
        var Good=await this.RunAsync("retained");
        Assert.Equal(0,Good.ExitCode);
        var Original=await File.ReadAllBytesAsync(Good.Output);
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"broken");
        var Bad=await this.RunAsync("retained");
        Assert.Equal(1,Bad.ExitCode);
        Assert.Equal(Original,await File.ReadAllBytesAsync(Good.Output));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task WorkerOutputAndResult_CannotOverwriteTheSource(bool ResultCollides){
        var Source=Path.Combine(this.Root,"input.model");
        await File.WriteAllTextAsync(Source,"triangle");
        var Request=new ImportWorkerRequest("test.fixture","00000000-0000-5000-8000-000000000001","input.model",this.Root,this.Root,ResultCollides?Path.Combine(this.Root,"output.cgex"):Source,new Dictionary<string,string>());
        var RequestPath=Path.Combine(this.Root,"request.json");
        await File.WriteAllBytesAsync(RequestPath,JsonSerializer.SerializeToUtf8Bytes(Request));
        var Exit=await ImportWorker.RunAsync(typeof(FixtureImporter).Assembly.Location,RequestPath,ResultCollides?Source:Path.Combine(this.Root,"result.json"),CancellationToken.None);
        Assert.Equal(1,Exit);
        Assert.Equal("triangle",await File.ReadAllTextAsync(Source));
    }
    [Fact]
    public void Options_DefaultsAreFrozenAndValuesAreValidated(){
        var Importer=new FixtureImporter().Descriptor;
        var Processor=new FixtureProcessor().Descriptor;
        var Options=ImportOptions.Resolve(Importer,[Processor],new Dictionary<string,string>());
        Assert.Equal("false",Options["test.corrupt"]);
        Assert.Throws<NotSupportedException>(()=>((IDictionary<string,string>)Options).Add("mutated","true"));
        Assert.Throws<InvalidDataException>(()=>ImportOptions.Resolve(Importer,[Processor],new Dictionary<string,string> { ["test.corrupt"]="invalid" }));
    }
    private async Task<(int ExitCode,string Output,string Result)>RunAsync(string Name,SortedDictionary<string,string>?Options=null){
        var Scratch=Path.Combine(this.Root,Name);
        Directory.CreateDirectory(Scratch);
        var Output=Path.Combine(Scratch,"output.cgex");
        var Result=Path.Combine(Scratch,"result.json");
        var Request=new ImportWorkerRequest("test.fixture","00000000-0000-5000-8000-000000000001","input.model",this.Root,Scratch,Output,Options??new(StringComparer.Ordinal));
        var RequestPath=Path.Combine(Scratch,"request.json");
        await File.WriteAllBytesAsync(RequestPath,JsonSerializer.SerializeToUtf8Bytes(Request));
        var Start=new ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        Start.ArgumentList.Add(typeof(CookerHost).Assembly.Location);
        Start.ArgumentList.Add("import-worker");
        Start.ArgumentList.Add(typeof(FixtureImporter).Assembly.Location);
        Start.ArgumentList.Add(RequestPath);
        Start.ArgumentList.Add(Result);
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Stdout=Process.StandardOutput.ReadToEndAsync();
        var Stderr=Process.StandardError.ReadToEndAsync();
        using var Timeout=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await Process.WaitForExitAsync(Timeout.Token); }
        catch(OperationCanceledException){ Process.Kill(true);throw; }
        var Log=await Stdout+await Stderr;
        Assert.True(File.Exists(Result),Log);
        return (Process.ExitCode,Output,Result);
    }
}
