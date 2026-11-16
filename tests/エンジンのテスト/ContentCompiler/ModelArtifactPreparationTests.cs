using Xunit;
using System.Numerics;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler.Tests;
public sealed class ModelArtifactPreparationTests:IDisposable {
    private readonly string Root=Path.Combine(Path.GetTempPath(),"cge-prepared-model-"+Guid.NewGuid().ToString("N"));
    private static readonly AssetId Asset=AssetId.Parse("00000000-0000-5000-8000-000000000001");
    private static readonly Hash256 Source=Hash256.Compute([1]);
    private static readonly Hash256 Options=Hash256.Compute([2]);
    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }
    [Fact]
    public async Task ConcurrentModelPreparationWaitsForTheSameOutputAndAllowsCancellation(){
        using var Entered=new ManualResetEventSlim();
        using var Release=new ManualResetEventSlim();
        IEnumerable<string>HoldFirst(){ Entered.Set();Release.Wait();yield break; }
        var Output=Path.Combine(this.Root,"content");
        var First=new ModelProjectCompiler(this.Root,Path.Combine(this.Root,"cook.exe"),[],Output).CompileAsync(HoldFirst());
        try {
            Assert.True(await Task.Run(()=>Entered.Wait(TimeSpan.FromSeconds(10))));
            var Second=new ModelProjectCompiler(this.Root,Path.Combine(this.Root,"cook.exe"),[],Output).CompileAsync([]);
            Assert.NotSame(Second,await Task.WhenAny(Second,Task.Delay(200)));
            using var Cancellation=new CancellationTokenSource();
            var Cancelled=new ModelProjectCompiler(this.Root,Path.Combine(this.Root,"cook.exe"),[],Output).CompileAsync([],CancellationToken:Cancellation.Token);
            Cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await Cancelled);
            Release.Set();
            await First;
            await Second;
            Assert.True(File.Exists(Path.Combine(Output,"content-index.json")));
        } finally { Release.Set(); }
    }
    private static InterchangeDocument Triangle()=>new() {
        Space=new(InterchangeSpaceState.Engine,SourceConvention.Unity,1f,AssetFacing.Unknown,ReferencePoseKind.Unknown,UvOrigin.TopLeft,default,"test"),
        Provenance=new("Assets/triangle.glb","test.prepared","test",[],new("CC0",true,string.Empty,string.Empty,[])),
        Nodes=[new("root",-1,InterchangeTransform.Identity,0,[],-1,-1,-1,InterchangeNodeFlags.None,[],[])],
        Meshes=[new("triangle",new(3,[Vector3.Zero,Vector3.UnitX,Vector3.UnitY],[Vector3.UnitZ,Vector3.UnitZ,Vector3.UnitZ],[new(1,0,0,1),new(1,0,0,1),new(1,0,0,1)],[],[],null,[]),[0,1,2],[new(0,3,-1)],[],[],new(Vector3.Zero,Vector3.One))]
    };
    [Fact]
    public async Task PreparedGraph_PublishesExactClosureAndLoadsThroughRuntimeStore(){
        var Prepared=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        var Store=new ContentAddressedStore(this.Root);
        var Compiler=new IncrementalContentCompiler(Store,Path.Combine(this.Root,"state.json"));
        var Cold=await Compiler.CompileAsync(Prepared.Nodes,[Prepared.Root]);
        Assert.True(Cold.Succeeded,Cold.Error);
        Assert.Equal(3,Cold.Rebuilt.Length);
        var Warm=await Compiler.CompileAsync(Prepared.Nodes,[Prepared.Root]);
        Assert.True(Warm.Succeeded,Warm.Error);
        Assert.Equal(3,Warm.Reused.Length);
        var Manifest=new ShippingContentManifest([Prepared.Artifacts.Single(A=>A.Key==Prepared.Root).Reference],Prepared.Artifacts.Select(A=>{
            Assert.True(ContentArtifactCodec.TryRead(A.Envelope,out var Artifact,out _));
            return new ShippingContentManifestEntry(A.Reference,A.Address,A.Kind,Artifact!.Provenance,Artifact.Dependencies,A.Envelope.Length);
        }));
        await using var Runtime=new RuntimeAssetStore(Store,Manifest,ModelRuntimeCodecs.Create());
        await using var Model=await Runtime.RetainAsync<ModelData>(Prepared.Root);
        var MeshReference=Assert.Single(Model.Value.Meshes).ToReference();
        await using var Mesh=await Runtime.RetainAsync<ModelMeshData>(MeshReference.Key);
        Assert.Equal(Vector3.UnitX,Mesh.Value.Positions[1]);
        Assert.Equal(3,Mesh.Value.Indices.Length);
    }
    [Fact]
    public void InvalidSourceAndMissingMaterial_FailBeforeAnyNodesAreReturned(){
        var Document=Triangle();
        Assert.Throws<InvalidDataException>(()=>ModelArtifactPreparation.Prepare(Asset,Document with { Materials=[new() { Name="missing" }] },Source,Options,"test@1"));
        Assert.Throws<InvalidDataException>(()=>ModelArtifactPreparation.Prepare(Asset,Document with { Meshes=[Document.Meshes[0] with { Indices=[99,1,2] }] },Source,Options,"test@1"));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SourceFailure_RetainsItsLastGoodEntriesWhileOtherSourcesPublish(bool IncludeOther){
        var Store=new ContentAddressedStore(this.Root);
        var IndexPath=Path.Combine(this.Root,"content-index.json");
        var Compiler=new ModelContentCompiler(Store,Path.Combine(this.Root,"state.json"),IndexPath);
        var First=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        var Initial=await Compiler.CompileAsync([new(Asset,First.SourceUri,_=>Task.FromResult(First))]);
        Assert.True(Initial.Succeeded);
        var OtherAsset=AssetId.Parse("00000000-0000-5000-8000-000000000002");
        var OtherDocument=Triangle() with { Provenance=Triangle().Provenance with { SourceUri="Assets/other.glb" } };
        var Other=ModelArtifactPreparation.Prepare(OtherAsset,OtherDocument,Source,Options,"test@1");
        var Inputs=new List<ModelCompileInput>{new(Asset,First.SourceUri,_=>throw new InvalidDataException("malformed source"))};
        if(IncludeOther){ Inputs.Add(new(OtherAsset,Other.SourceUri,_=>Task.FromResult(Other))); }
        var Result=await Compiler.CompileAsync(Inputs);
        Assert.False(Result.Succeeded);
        Assert.Single(Result.Diagnostics);
        Assert.Equal(IncludeOther?6:3,Result.Index.Entries.Length);
        Assert.All(Result.Index.Entries.Where(E=>E.Asset==Asset.ToString()),E=>Assert.True(E.LastKnownGood));
        Assert.All(Result.Index.Entries.Where(E=>E.Asset==OtherAsset.ToString()),E=>Assert.False(E.LastKnownGood));
        Assert.Equal(Initial.Index.Resolve(First.SourceUri,ContentArtifactKind.Model),Result.Index.Resolve(First.SourceUri,ContentArtifactKind.Model));
        Assert.Equal(Result.Index.Write(),ModelContentIndex.Read(await File.ReadAllBytesAsync(IndexPath)).Write());
        var Reachable=new List<Hash256>();
        foreach(var Artifact in First.Artifacts.Concat(IncludeOther?Other.Artifacts:[])){
            Assert.True(Result.Compilation!.Index!.TryResolve(Artifact.Reference,out var Address));
            Assert.Equal(Artifact.Address,Address);
            Reachable.Add(Address);
        }
        Store.CollectGarbage(Reachable,ContentGarbageCollectionMode.Quarantine);
        foreach(var Artifact in First.Artifacts){ Assert.Equal(Artifact.Envelope,(await Store.ReadAsync(Artifact.Address)).ToArray()); }
        Inputs=[new(Asset,First.SourceUri,_=>Task.FromResult(First))];
        if(IncludeOther){ Inputs.Add(new(OtherAsset,Other.SourceUri,_=>Task.FromResult(Other))); }
        var Restored=await Compiler.CompileAsync(Inputs);
        Assert.True(Restored.Succeeded);
        Assert.Equal(IncludeOther?6:3,Restored.Compilation!.Reused.Length);
        Assert.All(Restored.Index.Entries,E=>Assert.False(E.LastKnownGood));
    }
    [Fact]
    public async Task EmptyProjectPublishesAnEmptyIndexInsteadOfAnInvalidCompileGraph(){
        var Store=new ContentAddressedStore(this.Root);var IndexPath=Path.Combine(this.Root,"content-index.json");
        var Compiler=new ModelContentCompiler(Store,Path.Combine(this.Root,"state.json"),IndexPath);
        var First=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        Assert.True((await Compiler.CompileAsync([new(Asset,First.SourceUri,_=>Task.FromResult(First))])).Succeeded);
        var Result=await Compiler.CompileAsync([]);
        Assert.True(Result.Succeeded);Assert.Empty(Result.Index.Entries);Assert.Equal(0,Result.Compilation!.Index!.Count);
        Assert.Empty(ModelContentIndex.Read(await File.ReadAllBytesAsync(IndexPath)).Entries);
        await Assert.ThrowsAsync<FileNotFoundException>(()=>ModelAssetBundle.OpenAsync(this.Root,"Assets/Missing.glb"));
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RuntimeBundleRejectsSubstitutedHashesAndMissingDependencyEntries(bool SubstituteHash){
        var Store=new ContentAddressedStore(this.Root);var IndexPath=Path.Combine(this.Root,"content-index.json");
        var Prepared=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        var Result=await new ModelContentCompiler(Store,Path.Combine(this.Root,"state.json"),IndexPath).CompileAsync([new(Asset,Prepared.SourceUri,_=>Task.FromResult(Prepared))]);
        Assert.True(Result.Succeeded);
        var Entries=SubstituteHash?Result.Index.Entries.Select(Entry=>Entry.Kind==ContentArtifactKind.Model?Entry with {Hash=Hash256.Compute("substituted"u8.ToArray()).ToString()}:Entry)
            :Result.Index.Entries.Where(static Entry=>Entry.Kind!=ContentArtifactKind.Mesh);
        await File.WriteAllBytesAsync(IndexPath,new ModelContentIndex(Entries).Write());
        await Assert.ThrowsAsync<InvalidDataException>(()=>ModelAssetBundle.OpenAsync(this.Root,Prepared.SourceUri));
        await File.WriteAllBytesAsync(IndexPath,Result.Index.Write());
        await using var Bundle=await ModelAssetBundle.OpenAsync(this.Root,Prepared.SourceUri);
        Assert.Single(Bundle.Meshes);
    }
    [Fact]
    public async Task RuntimeBundlePreservesAuthoredClipOrderInsteadOfSortingAssetIdentifiers(){
        var Document=Triangle() with {Clips=[new("z-authored-first",0f,1f,0f,InterchangeClipFlags.None,[],[],[],[],[],null),new("a-authored-second",0f,1f,0f,InterchangeClipFlags.None,[],[],[],[],[],null)]};
        var Prepared=ModelArtifactPreparation.Prepare(Asset,Document,Source,Options,ModelArtifactPreparation.CurrentToolVersion);
        var Result=await new ModelContentCompiler(new ContentAddressedStore(this.Root),Path.Combine(this.Root,"state.json"),Path.Combine(this.Root,"content-index.json")).CompileAsync([new(Asset,Prepared.SourceUri,_=>Task.FromResult(Prepared))]);
        Assert.True(Result.Succeeded);
        await using var Bundle=await ModelAssetBundle.OpenAsync(this.Root,Prepared.SourceUri);
        Assert.Equal(new[]{"z-authored-first","a-authored-second"},Bundle.Clips.Select(static Clip=>Clip.Name));
        var WithoutOrder=Bundle.Model with {RuntimeBlocks=[..Bundle.Model.RuntimeBlocks.Where(static Block=>Block.Schema!=ModelClipOrder.Schema)]};
        Assert.Throws<InvalidDataException>(()=>ModelClipOrder.Apply(WithoutOrder,Bundle.Clips));
        var Reversed=ModelClipOrder.Apply(Bundle.Model,[..Bundle.Clips.Reverse()]);
        Assert.Equal(Bundle.Clips.Select(static Clip=>Clip.Name),Reversed.Select(static Clip=>Clip.Name));
    }
    [Fact]
    public void ContentIndex_RejectsDuplicateKeysAndAmbiguousSelectors(){
        var Prepared=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        var Mesh=Prepared.Artifacts.Single(A=>A.Kind==ContentArtifactKind.Mesh);
        var Entry=new ModelContentEntry(Prepared.SourceUri,Asset.ToString(),Mesh.Name,Mesh.Kind,new AssetUri(Mesh.Key).ToString(),Mesh.Reference.ContentHash.ToString(),Mesh.Address.ToString(),Mesh.Envelope.Length,false);
        Assert.Throws<InvalidDataException>(()=>new ModelContentIndex([Entry,Entry]));
        Assert.Throws<InvalidDataException>(()=>new ModelContentIndex([Entry with { SourceUri="../outside.glb" }]));
        var Second=Entry with { Name="second",Uri=new AssetUri(new(Asset,SubAssetIdentity.Derive(SubAssetKind.Mesh,"second"))).ToString() };
        var Index=new ModelContentIndex([Entry,Second]);
        var LegacyWire=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new {Schema="cge-content-index/1",Index.Entries});
        Assert.Equal(LegacyWire,Index.Write());Assert.Equal(LegacyWire,ModelContentIndex.Read(LegacyWire).Write());
        Assert.Throws<InvalidDataException>(()=>Index.Resolve(Prepared.SourceUri,ContentArtifactKind.Mesh));
        Assert.Throws<KeyNotFoundException>(()=>Index.Resolve("Assets/Missing.glb",ContentArtifactKind.Model));
        Assert.Equal(Mesh.Reference,Index.Resolve(Prepared.SourceUri,ContentArtifactKind.Mesh,"triangle"));
    }
    [Fact]
    public async Task PublicEnvelopeMutation_CannotChangeThePreparedCompileCallback(){
        var Prepared=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        foreach(var Artifact in Prepared.Artifacts){ Array.Fill(Artifact.Envelope,(byte)0); }
        var Result=await new IncrementalContentCompiler(new ContentAddressedStore(this.Root),Path.Combine(this.Root,"state.json")).CompileAsync(Prepared.Nodes,[Prepared.Root]);
        Assert.True(Result.Succeeded,Result.Error);
    }
}
