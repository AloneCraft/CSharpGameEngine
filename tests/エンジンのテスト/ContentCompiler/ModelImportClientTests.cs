using Xunit;
using System.Collections.Immutable;
using System.Numerics;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler.Tests;
public sealed class MetadataChangingResourcesCooker:IAssetCooker {
    public string Name=>"ModelResources";
    public IReadOnlyList<string>InputExtensions=>[".cgemodelresources"];
    public CookResult Cook(CookRequest Request){
        var Result=new CSharpGameEngine.Import.Models.ModelResourcesCooker().Cook(Request);
        if(Result.Succeeded){ File.AppendAllText(Path.Combine(Request.ProjectDirectory,"Box.glb.meta")," "); }
        return Result;
    }
}
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
    [Fact]
    public async Task BuiltInGltfTravelsThroughTheDedicatedWorkerAndCommonInterchange(){
        var Bytes=new byte[42];BitConverter.GetBytes(1f).CopyTo(Bytes,12);BitConverter.GetBytes(1f).CopyTo(Bytes,28);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(38),1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(Bytes.AsSpan(40),2);
        var Json=System.Text.Json.JsonSerializer.Serialize(new {
            asset=new{version="2.0"},scene=0,scenes=new[]{new{nodes=new[]{0}}},nodes=new[]{new{name="Triangle",mesh=0}},
            meshes=new[]{new{name="Triangle",primitives=new[]{new{attributes=new{POSITION=0},indices=1}}}},
            buffers=new[]{new{byteLength=42,uri="data:application/octet-stream;base64,"+Convert.ToBase64String(Bytes)}},
            bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=36},new{buffer=0,byteOffset=36,byteLength=6}},
            accessors=new object[]{new{bufferView=0,componentType=5126,count=3,type="VEC3",min=new[]{0,0,0},max=new[]{1,1,0}},new{bufferView=1,componentType=5123,count=3,type="SCALAR"}}
        });
        await File.WriteAllTextAsync(Path.Combine(this.Root,"triangle.gltf"),Json);
        var Output=Path.Combine(this.Root,"triangle.cgex");
        var Result=await RunCompilerAsync(["import-ir","triangle.gltf","--project",this.Root,"--cook",Tool,
            "--module",typeof(CSharpGameEngine.Import.Gltf.GltfImporter).Assembly.Location,"--out",Output,"--verify"]);
        Assert.True(Result.Exit==0,Result.Error);
        var Document=InterchangeCodec.Read(await File.ReadAllBytesAsync(Output));
        Assert.Equal(InterchangeSpaceState.Engine,Document.Space.State);
        Assert.Equal(3,Assert.Single(Document.Meshes).Vertices.Count);
        Assert.Equal(3,Document.Meshes[0].Indices.Length);
        Assert.Empty(ModelImportPipeline.ValidateEngine(Document).Where(static Diagnostic=>Diagnostic.Severity==ImportSeverity.Error));
    }
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
            return Path.Combine(Directory!.FullName,"tools","Cook.Cli","bin",Configuration,"net10.0","Cook.Cli.dll");
        }
    }
    private async Task<ImporterRegistry>RegistryAsync(string?ToolPath=null)=>await ImporterRegistry.DiscoverAsync(ToolPath??Tool,[(typeof(RegistryFixtureImporter).Assembly.Location,true)],Path.Combine(this.Cache,"registry"));
    [Fact]
    public async Task MetadataChangedByTheResourceWorkerCannotReplaceTheLastSuccessfulOutput(){
        var Repository=new DirectoryInfo(AppContext.BaseDirectory);
        while(Repository is not null&&!File.Exists(Path.Combine(Repository.FullName,"CSharpGameEngine.slnx"))){ Repository=Repository.Parent; }
        var Source=Path.Combine(this.Root,"Box.glb");File.Copy(Path.Combine(Repository!.FullName,"assets","TestFixtures","Gltf","Box.glb"),Source);
        await File.WriteAllBytesAsync(Source+".meta",CSharpGameEngine.Content.AssetMetadata.Create(Asset,"Box.glb","cge.gltf").ToCanonicalUtf8());
        var Content=Path.Combine(this.Root,"published");string[]Arguments=["compile","Box.glb","--project",this.Root,"--cook",Tool,"--module",typeof(CSharpGameEngine.Import.Gltf.GltfImporter).Assembly.Location,"--out",Content];
        var First=await RunCompilerAsync(Arguments);Assert.True(First.Exit==0,First.Error);var Previous=await File.ReadAllBytesAsync(Path.Combine(Content,"content-index.json"));
        var Changed=await RunCompilerAsync([..Arguments,"--resources-module",typeof(MetadataChangingResourcesCooker).Assembly.Location]);
        Assert.NotEqual(0,Changed.Exit);Assert.Contains("IMP901",Changed.Error);Assert.Equal(Previous,await File.ReadAllBytesAsync(Path.Combine(Content,"content-index.json")));
    }
    [Fact]
    public async Task ProjectCliKeepsTheWholeInputSetThroughFailureRecoveryRemovalAndAnEmptyProject(){
        await File.WriteAllTextAsync(Path.Combine(this.Root,"other.model"),"triangle");
        var Inputs=Path.Combine(this.Root,"inputs.txt");var Content=Path.Combine(this.Root,"project-content");var Runtime=Path.Combine(this.Root,"runtime");var FileList=Path.Combine(this.Root,"runtime-files.txt");
        await File.WriteAllLinesAsync(Inputs,["input.model","other.model"]);
        string[]Arguments=["compile-project","--project",this.Root,"--cook",Tool,"--module",typeof(RegistryFixtureImporter).Assembly.Location,"--inputs",Inputs,"--out",Content,"--copy-to",Runtime,"--file-list",FileList,"--verify"];
        async Task<CSharpGameEngine.Content.Models.ModelContentIndex>Index()=>CSharpGameEngine.Content.Models.ModelContentIndex.Read(await File.ReadAllBytesAsync(Path.Combine(Content,"content-index.json")));
        var First=await RunCompilerAsync(Arguments);Assert.True(First.Exit==0,First.Error);
        var Before=await Index();Assert.Equal(6,Before.Entries.Length);
        Assert.Equal(7,(await File.ReadAllLinesAsync(FileList)).Length);Assert.All(await File.ReadAllLinesAsync(FileList),static PathValue=>Assert.True(File.Exists(PathValue)));
        var Stamp=new DateTime(2020,1,1,0,0,0,DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(Content,"content-index.json"),Stamp);File.SetLastWriteTimeUtc(Path.Combine(Runtime,"content-index.json"),Stamp);
        var Warm=await RunCompilerAsync(Arguments);Assert.True(Warm.Exit==0,Warm.Error);
        Assert.Equal(Stamp,File.GetLastWriteTimeUtc(Path.Combine(Content,"content-index.json")));Assert.Equal(Stamp,File.GetLastWriteTimeUtc(Path.Combine(Runtime,"content-index.json")));
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"malformed");
        await File.WriteAllTextAsync(Path.Combine(this.Root,"optional.model"),"1");
        var Failed=await RunCompilerAsync(Arguments);Assert.Equal(1,Failed.Exit);
        var Mixed=await Index();Assert.Equal(6,Mixed.Entries.Length);
        Assert.Equal(Before.Write(),await File.ReadAllBytesAsync(Path.Combine(Runtime,"content-index.json")));
        var Store=new CSharpGameEngine.Content.ContentAddressedStore(Content);
        foreach(var Entry in Mixed.Entries){
            Assert.True(Hash256.TryParse(Entry.Address,out var Address));
            Assert.Equal(Entry.EnvelopeBytes,(await Store.ReadAsync(Address)).Length);
        }
        Assert.All(Mixed.Entries.Where(static Entry=>Entry.SourceUri=="input.model"),static Entry=>Assert.True(Entry.LastKnownGood));
        Assert.All(Mixed.Entries.Where(static Entry=>Entry.SourceUri=="other.model"),static Entry=>Assert.False(Entry.LastKnownGood));
        Assert.Equal(Before.Resolve("input.model",CSharpGameEngine.Content.ContentArtifactKind.Model),Mixed.Resolve("input.model",CSharpGameEngine.Content.ContentArtifactKind.Model));
        Assert.NotEqual(Before.Resolve("other.model",CSharpGameEngine.Content.ContentArtifactKind.Model),Mixed.Resolve("other.model",CSharpGameEngine.Content.ContentArtifactKind.Model));
        await File.WriteAllTextAsync(Path.Combine(this.Root,"input.model"),"triangle");
        var Recovered=await RunCompilerAsync(Arguments);Assert.True(Recovered.Exit==0,Recovered.Error);
        Assert.All((await Index()).Entries,static Entry=>Assert.False(Entry.LastKnownGood));
        await File.WriteAllLinesAsync(Inputs,["other.model"]);
        var Removed=await RunCompilerAsync(Arguments);Assert.True(Removed.Exit==0,Removed.Error);
        Assert.Equal(3,(await Index()).Entries.Length);Assert.All((await Index()).Entries,static Entry=>Assert.Equal("other.model",Entry.SourceUri));
        await File.WriteAllTextAsync(Inputs,string.Empty);
        var Empty=await RunCompilerAsync(Arguments);Assert.True(Empty.Exit==0,Empty.Error);Assert.Empty((await Index()).Entries);
        Assert.Single(await File.ReadAllLinesAsync(FileList));Assert.Empty(CSharpGameEngine.Content.Models.ModelContentIndex.Read(await File.ReadAllBytesAsync(Path.Combine(Runtime,"content-index.json"))).Entries);
    }
    [Theory]
    [InlineData("assets/TestFixtures/Gltf/BoxTextured.glb")]
    [InlineData("Templates/FirstPerson/Assets/Stage/Lvl_FirstPerson/Meshes/SM_Template_Map_Floor_78eccfff4a.glb")]
    [InlineData("Templates/FirstPerson/Assets/Characters/Mannequins/SKM_Manny_Simple.glb")]
    [InlineData("Templates/FirstPerson/Assets/Characters/Mannequins/Anims/MF_Unarmed_Walk_Fwd.glb")]
    public async Task CommonGltfPreservesExistingGeometryRestPaletteAndAnimationKeys(string Source){
        var Directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(Directory is not null&&!File.Exists(Path.Combine(Directory.FullName,"CSharpGameEngine.slnx"))){ Directory=Directory.Parent; }
        var Original=Path.Combine(Directory!.FullName,Source);
        var Input=Path.Combine(this.Root,Path.GetFileName(Original));
        File.Copy(Original,Input);
        var Output=Path.Combine(this.Root,"parity.cgex");
        var Result=await RunCompilerAsync(["import-ir",Path.GetFileName(Input),"--project",this.Root,"--cook",Tool,"--module",typeof(CSharpGameEngine.Import.Gltf.GltfImporter).Assembly.Location,"--out",Output,"--verify"]);
        Assert.True(Result.Exit==0,Result.Error);
        var Document=InterchangeCodec.Read(await File.ReadAllBytesAsync(Output));
        var Baseline=CSharpGameEngine.Import.Gltf.GltfImporter.ImportScene(Input);
        Assert.True(Baseline.Success,string.Join("\n",Baseline.Diagnostics));
        Assert.Equal(Baseline.Scene!.Meshes.Count,Document.Meshes.Length);
        for(var Index=0;Index<Document.Meshes.Length;Index++){
            var Before=Baseline.Scene.Meshes[Index];var After=Document.Meshes[Index];
            Assert.Equal(Before.Positions,After.Vertices.Positions);
            Assert.Equal(Before.Indices,After.Indices);
            Assert.Equal(Before.Normals,After.Vertices.Normals);
            Assert.Equal(Before.Uvs,After.Vertices.TexCoords[0]);
        }
        Assert.Equal(Baseline.Scene.Skins.Count,Document.Skins.Length);
        for(var SkinIndex=0;SkinIndex<Document.Skins.Length;SkinIndex++){
            var Before=Baseline.Scene.Skins[SkinIndex];var After=Document.Skins[SkinIndex];var Skeleton=Document.Skeletons[After.SkeletonIndex];
            Assert.Equal(Before.InverseBindMatrices,After.InverseBindMatrices);
            for(var Joint=0;Joint<Before.JointNames.Count;Joint++){ Assert.Equal(Before.JointNames[Joint],Document.Nodes[Skeleton.JointNodes[After.JointMap[Joint]]].Name); }
        }
        var Clips=CSharpGameEngine.Import.Gltf.GltfImporter.ImportAnimations(Input);
        Assert.True(Clips.Success,string.Join("\n",Clips.Diagnostics));
        Assert.Equal(Clips.Clips.Count,Document.Clips.Length);
        for(var Index=0;Index<Document.Clips.Length;Index++){
            var Before=Clips.Clips[Index];var After=Document.Clips[Index];
            Assert.Equal(Before.Name,After.Name);Assert.Equal(Before.DurationSeconds,After.EndSeconds);
            foreach(var Track in Before.Tracks){
                var Candidate=Assert.Single(After.Transforms.Where(Value=>Document.Nodes[Value.NodeIndex].Name==Track.JointName));
                if(Track.Translation is{}Translation){ Assert.Equal(Translation.Times,Candidate.Translation!.Times);Assert.Equal(Translation.Values,Candidate.Translation.Values); }
                if(Track.Rotation is{}Rotation){
                    Assert.Equal(Rotation.Times,Candidate.Rotation!.Times);
                    for(var Key=0;Key<Rotation.Values.Count;Key++){
                        Assert.True(MathF.Abs(Quaternion.Dot(Rotation.Values[Key],Candidate.Rotation.Values[Key]))>1f-1e-5f);
                        if(Key>0){
                            var BeforeMiddle=Quaternion.Slerp(Rotation.Values[Key-1],Rotation.Values[Key],0.5f);
                            var AfterMiddle=Quaternion.Slerp(Candidate.Rotation.Values[Key-1],Candidate.Rotation.Values[Key],0.5f);
                            Assert.True(MathF.Abs(Quaternion.Dot(BeforeMiddle,AfterMiddle))>1f-1e-5f);
                        }
                    }
                }
                if(Track.Scale is{}Scale){ Assert.Equal(Scale.Times,Candidate.Scale!.Times);Assert.Equal(Scale.Values,Candidate.Scale.Values); }
            }
        }
        var Content=Path.Combine(this.Root,"content");
        if(Path.GetFileName(Input)=="BoxTextured.glb"){
            var Added=CSharpGameEngine.Content.AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.SaveMetadata,RepositoryRoot=this.Root,SourcePath=Input,AssetId=Asset.ToString(),Importer="cge.gltf"});
            Assert.True(Added.Success,string.Join("; ",Added.Diagnostics.Select(static Diagnostic=>Diagnostic.Message)));
        }
        string[]CompileArguments=["compile",Path.GetFileName(Input),"--project",this.Root,"--cook",Tool,"--module",typeof(CSharpGameEngine.Import.Gltf.GltfImporter).Assembly.Location,"--out",Content,"--verify"];
        if(Path.GetFileName(Input)!="BoxTextured.glb"){
            // 廃止したインポーター名のメタデータは、ビルドが書き換えずに IMP008 で止め、明示の変換だけが書き換える（ADR-0098 §5）。
            var Retired=CSharpGameEngine.Content.AssetMetadata.Create(Asset,Path.GetFileName(Input),CSharpGameEngine.Content.AssetMetadata.RetiredGltfImporter).ToCanonicalUtf8();
            await File.WriteAllBytesAsync(Input+".meta",Retired);
            var Refused=await RunCompilerAsync(CompileArguments);
            Assert.NotEqual(0,Refused.Exit);
            Assert.Contains("IMP008",Refused.Error+Refused.Output,StringComparison.Ordinal);
            Assert.Equal(Retired,await File.ReadAllBytesAsync(Input+".meta"));
            var Converted=CSharpGameEngine.Content.AssetAuthoringService.Execute(new(){RequestId=Guid.NewGuid(),Command=AssetAuthoringCommand.MigrateLegacyMetadata,RepositoryRoot=this.Root,SourcePath=Input,ExpectedMetadataSha256=CSharpGameEngine.Contracts.Hash256.Compute(Retired).ToString()});
            Assert.True(Converted.Success,string.Join("; ",Converted.Diagnostics.Select(static Diagnostic=>Diagnostic.Message)));
        }
        var Compile=await RunCompilerAsync(CompileArguments);
        Assert.True(Compile.Exit==0,Compile.Error+Compile.Output);
        Assert.True(CSharpGameEngine.Content.AssetMetadata.TryParse(await File.ReadAllBytesAsync(Input+".meta"),out var Migrated,out _));Assert.Equal(Asset,Migrated!.AssetId);Assert.Equal("cge.gltf",Migrated.Importer);
        var Published=CSharpGameEngine.Content.Models.ModelContentIndex.Read(await File.ReadAllBytesAsync(Path.Combine(Content,"content-index.json")));
        Assert.Single(Published.Entries.Where(static Entry=>Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Model));
        Assert.Equal(Document.Materials.Length,Published.Entries.Count(static Entry=>Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Material));
        if(Document.Materials.Length!=0){ Assert.Contains(Published.Entries,static Entry=>Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Texture); }
        else{ Assert.DoesNotContain(Published.Entries,static Entry=>Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Texture); }
        var Store=new CSharpGameEngine.Content.ContentAddressedStore(Content);
        var Catalog=new CSharpGameEngine.Rendering.RenderMaterialCatalog();
        var Materials=new List<CSharpGameEngine.Rendering.RenderMaterialResource>();
        foreach(var Entry in Published.Entries.OrderBy(static Entry=>Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Texture?0:1)){
            if(Entry.Kind is not(CSharpGameEngine.Content.ContentArtifactKind.Texture or CSharpGameEngine.Content.ContentArtifactKind.Material)){ continue; }
            Assert.True(CSharpGameEngine.Content.AssetUri.TryParse(Entry.Uri,out var Uri,out _));
            Assert.True(Hash256.TryParse(Entry.Address,out var Address));
            var Envelope=await Store.ReadAsync(Address);
            if(Entry.Kind==CSharpGameEngine.Content.ContentArtifactKind.Texture){ Catalog.RegisterTexture(Uri.Key,Envelope.Span); }
            else{ Materials.Add(Catalog.RegisterMaterial(Uri.Key,Envelope.Span)); }
        }
        foreach(var Material in Baseline.Scene.Materials){
            if(Material.WorldGrid is{}Grid){ Assert.Contains(Materials,Value=>Value.WorldGrid==new CSharpGameEngine.Rendering.StageWorldGrid(Grid.GridSizeMeters,Grid.SubGridDivisions,Grid.WireHalfWidth,Grid.GridColor,Grid.SubGridColor)); }
        }
        File.Delete(Input);
        await using var Bundle=await CSharpGameEngine.Content.Models.ModelAssetBundle.OpenAsync(Content,Path.GetFileName(Input));
        Assert.Equal(Document.Meshes.Length,Bundle.Meshes.Length);Assert.Equal(Document.Skeletons.Length,Bundle.Skeletons.Length);Assert.Equal(Document.Clips.Length,Bundle.Clips.Length);
        for(var Index=0;Index<Bundle.Meshes.Length;Index++){ Assert.Equal<Vector3>(Document.Meshes[Index].Vertices.Positions,Bundle.Meshes[Index].Positions); }
        foreach(var Reference in Bundle.Model.Materials){ Assert.NotEmpty((await Bundle.ReadEnvelopeAsync(Reference)).ToArray()); }
    }
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
        Start.ArgumentList.Add("content");
        foreach(var Argument in Arguments){ Start.ArgumentList.Add(Argument); }
        using var Process=System.Diagnostics.Process.Start(Start)!;
        var Output=Process.StandardOutput.ReadToEndAsync();
        var Error=Process.StandardError.ReadToEndAsync();
        // 止まったコンパイラーを検出するための上限で、性能の判定ではない。FirstPerson の SKM_Manny_Simple.glb の compile --verify は
        // 単独実行でも約 28 秒かかり（2026-11-15 計測）、以前の 30 秒では全体の試験の並列実行で CPU が混むと超えていた。
        try { await Process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(180)); }
        catch(TimeoutException){ Process.Kill(true);throw; }
        return (Process.ExitCode,await Output,await Error);
    }
}
