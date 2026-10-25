using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler.Tests;
public sealed class RegistryFixtureImporter:IAssetImporter {
    public ImporterDescriptor Descriptor=>new("test.registry",1,"Fixture",[".model"],100,"test",ImporterIsolation.PerImport,ImporterDeterminism.Deterministic,ImportCapabilities.Mesh,[new("test.random","false","Randomize for determinism rejection",["false","true"]),new("test.wait","false","Wait for cancellation",["false","true"]),new("test.ignore","false","Ignore cancellation for kill testing",["false","true"])],[],[]);
    public ImportSniffResult Sniff(ImportSniffRequest Request)=>ImportSniffResult.Definite;
    public async ValueTask<ImportResult>ImportAsync(ImportRequest Request,IImportContext Context,CancellationToken CancellationToken){
        if(Context.Options.TryGetValue("test.ignore",out var Ignore)&&Ignore=="true"){ await Task.Delay(Timeout.Infinite,CancellationToken.None); }
        if(Context.Options.TryGetValue("test.wait",out var Wait)&&Wait=="true"){ await Task.Delay(Timeout.Infinite,CancellationToken); }
        using var Source=Context.OpenRead(Request.SourceUri);
        using var Reader=new StreamReader(Source);
        if(await Reader.ReadToEndAsync(CancellationToken)!="triangle"){ throw new InvalidDataException("IMP101: malformed fixture."); }
        var Height=0f;
        if(Context.TryOpenRead("optional.model",out var Optional)){
            using(Optional){ using var Text=new StreamReader(Optional!);Height=float.Parse(await Text.ReadToEndAsync(CancellationToken),System.Globalization.CultureInfo.InvariantCulture); }
        }
        var Name=Context.Options.TryGetValue("test.random",out var Random)&&Random=="true"?Guid.NewGuid().ToString("N"):"triangle";
        Context.Report(new(ImportSeverity.Warning,"IMP140","test.registry",ImportPhase.Read,"Fixture warning",new(Request.SourceUri)));
        return new(new InterchangeDocument {
            Nodes=[new("root",-1,InterchangeTransform.Identity,0,[],-1,-1,-1,InterchangeNodeFlags.None,[],[])],
            Meshes=[new(Name,new(3,[new(0f,0f,Height),Vector3.UnitX,Vector3.UnitY],[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],[],[],[],null,[]),[0,1,2],[new(0,3,-1)],[],[],new(Vector3.Zero,Vector3.One))]
        });
    }
}
public sealed class ModelImportClientTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-import-client-テスト #;"+Guid.NewGuid().ToString("N"));
    private static readonly AssetId Asset=AssetId.Parse("00000000-0000-5000-8000-000000000001");
    public ModelImportClientTests(){ Directory.CreateDirectory(this.Root);File.WriteAllText(Path.Combine(this.Root,"input.model"),"triangle"); }
    public void Dispose()=>Directory.Delete(this.Root,true);
    private string Cache=>Path.Combine(this.Root,"cache");
    private static string Tool{
        get {
            var Directory=new DirectoryInfo(AppContext.BaseDirectory);
            var Configuration=Directory.Parent!.Name;
            while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
            return Path.Combine(Directory!.FullName,"tools","CSharpGameEngine.Cook.Cli","bin",Configuration,"net10.0","CSharpGameEngine.Cook.Cli.dll");
        }
    }
    private async Task<ImporterRegistry>RegistryAsync(string?ToolPath=null)=>await ImporterRegistry.DiscoverAsync(ToolPath??Tool,[(typeof(RegistryFixtureImporter).Assembly.Location,true)],Path.Combine(this.Cache,"registry"));
    [Fact]
    public async Task DescribeCacheAndWarmIr_DoNotRequireAnotherWorker(){
        var Registry=await this.RegistryAsync();
        Assert.True(Registry.IsValid("test.registry",".MODEL"));
        var Cached=await this.RegistryAsync("missing-worker.dll");
        Assert.Equal("test.registry",Assert.Single(Cached.Candidates(".model")).Identity);
        var Registration=Registry.Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        var First=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        Assert.NotNull(First.Document);
        Assert.False(First.Reused);
        var Offline=new ModelImportClient("missing-worker.dll",this.Cache);
        var Warm=await Offline.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        Assert.True(Warm.Reused);
        Assert.Equal(First.SourceHash,Warm.SourceHash);
        Assert.Contains(Warm.Diagnostics,D=>D.Code=="IMP140");
        Assert.Equal(InterchangeCodec.Write(First.Document!),InterchangeCodec.Write(Warm.Document!));
    }
    [Fact]
    public async Task AbsentDependencyBecomingPresent_InvalidatesWarmIr(){
        var Registry=await this.RegistryAsync();
        var Registration=Registry.Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        var Before=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        await File.WriteAllTextAsync(Path.Combine(this.Root,"optional.model"),"2");
        var After=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        Assert.False(After.Reused);
        Assert.NotEqual(Before.SourceHash,After.SourceHash);
        Assert.Equal(-2f,After.Document!.Meshes[0].Vertices.Positions[0].Z);
    }
    [Fact]
    public async Task Failure_DoesNotReplaceLastKnownGoodImportCatalog(){
        var Registry=await this.RegistryAsync();
        var Registration=Registry.Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        Assert.NotNull((await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>())).Document);
        var Catalog=Path.Combine(this.Cache,Asset+".import.json");
        var Original=await File.ReadAllBytesAsync(Catalog);
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"broken");
        var Failed=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        Assert.Null(Failed.Document);
        Assert.Contains(Failed.Diagnostics,D=>D.Code=="IMP101");
        Assert.Equal(Original,await File.ReadAllBytesAsync(Catalog));
    }
    [Fact]
    public async Task Verify_RejectsNondeterministicReaders(){
        var Registration=(await this.RegistryAsync()).Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        var Result=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string> { ["test.random"]="true" },true);
        Assert.Null(Result.Document);
        Assert.Contains(Result.Diagnostics,D=>D.Code=="IMP901");
        Assert.False(File.Exists(Path.Combine(this.Cache,Asset+".import.json")));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_StopsTheWaitingWorkerAndDoesNotPublishCatalog(bool IgnoreCancellation){
        var Registration=(await this.RegistryAsync()).Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        using var Stop=new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var Watch=System.Diagnostics.Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string> { ["test.wait"]="true",["test.ignore"]=IgnoreCancellation?"true":"false" },CancellationToken:Stop.Token));
        Assert.InRange(Watch.Elapsed,TimeSpan.Zero,TimeSpan.FromMilliseconds(2500));
        Assert.False(File.Exists(Path.Combine(this.Cache,Asset+".import.json")));
    }
    [Fact]
    public async Task CorruptIrCache_IsRejectedBeforeReuse(){
        var Registration=(await this.RegistryAsync()).Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        _=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        using var Catalog=System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(this.Cache,Asset+".import.json")));
        var Ir=Path.Combine(this.Cache,Catalog.RootElement.GetProperty("IrHash").GetString()+".cgex");
        var Bytes=await File.ReadAllBytesAsync(Ir);
        Bytes[^1]^=1;
        await File.WriteAllBytesAsync(Ir,Bytes);
        await Assert.ThrowsAsync<InvalidDataException>(()=>Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>()));
    }
    [Fact]
    public async Task Cli_ImportsInspectsAndExplainsWithActualWorkerProcesses(){
        var Module=typeof(RegistryFixtureImporter).Assembly.Location;
        var Output=Path.Combine(this.Root,"result.cgex");
        var Import=await RunCompilerAsync(["import-ir","input.model","--project",this.Root,"--cook",Tool,"--module",Module,"--out",Output,"--verify"]);
        Assert.Equal(0,Import.Exit);
        Assert.Contains("IMP001",Import.Error);
        using(var Result=System.Text.Json.JsonDocument.Parse(Import.Output)){ Assert.False(Result.RootElement.GetProperty("Reused").GetBoolean()); }
        var Inspect=await RunCompilerAsync(["inspect",Output]);
        Assert.Equal(0,Inspect.Exit);
        using(var Result=System.Text.Json.JsonDocument.Parse(Inspect.Output)){ Assert.Equal(1,Result.RootElement.GetProperty("Meshes").GetInt32()); }
        var Explain=await RunCompilerAsync(["explain","input.model","--project",this.Root,"--cook",Tool,"--module",Module]);
        Assert.Equal(0,Explain.Exit);
        Assert.Contains("test.registry",Explain.Output);
    }
    [Fact]
    public async Task Cli_CanonicalMetadataPinsIdentityAndRejectsLegacyMetadata(){
        var Module=typeof(RegistryFixtureImporter).Assembly.Location;
        var Output=Path.Combine(this.Root,"result.cgex");
        var Metadata=CSharpGameEngine.Content.AssetMetadata.Create(Asset,"input.model","test.registry",[]);
        await File.WriteAllBytesAsync(Path.Combine(this.Root,"input.model.meta"),Metadata.ToCanonicalUtf8());
        var Import=await RunCompilerAsync(["import-ir","input.model","--project",this.Root,"--cook",Tool,"--module",Module,"--out",Output]);
        Assert.Equal(0,Import.Exit);
        Assert.DoesNotContain("IMP001",Import.Error);
        using(var Result=System.Text.Json.JsonDocument.Parse(Import.Output)){ Assert.Equal(Asset.ToString(),Result.RootElement.GetProperty("AssetId").GetString()); }
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model.meta"),"guid: 00000000000050008000000000000001\n");
        var Invalid=await RunCompilerAsync(["import-ir","input.model","--project",this.Root,"--cook",Tool,"--module",Module,"--out",Path.Combine(this.Root,"invalid.cgex")]);
        Assert.Equal(1,Invalid.Exit);
        Assert.Contains("IMP007",Invalid.Error);
        Assert.False(File.Exists(Path.Combine(this.Root,"invalid.cgex")));
    }
    [Fact]
    public async Task ExplicitDefaultsAndOmittedDefaults_HaveTheSameFingerprint(){
        var Registration=(await this.RegistryAsync()).Resolve("test.registry",".model");
        var Client=new ModelImportClient(Tool,this.Cache);
        var First=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string>());
        var Second=await Client.ImportAsync(Registration,Asset,this.Root,"input.model",new Dictionary<string,string> { ["test.wait"]="false",["test.random"]="false",["test.ignore"]="false" });
        Assert.True(Second.Reused);
        Assert.Equal(First.SourceHash,Second.SourceHash);
    }
    private static async Task<(int Exit,string Output,string Error)>RunCompilerAsync(string[]Arguments){
        var Start=new System.Diagnostics.ProcessStartInfo("dotnet") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        Start.ArgumentList.Add(typeof(ModelImportClient).Assembly.Location);
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Output=Process.StandardOutput.ReadToEndAsync();
        var Error=Process.StandardError.ReadToEndAsync();
        try { await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30)); }
        catch(TimeoutException){ Process.Kill(true);throw; }
        return (Process.ExitCode,await Output,await Error);
    }
}
