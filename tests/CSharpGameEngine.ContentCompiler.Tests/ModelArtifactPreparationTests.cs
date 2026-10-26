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
    [Fact]
    public async Task SourceFailure_RetainsItsLastGoodEntriesWhileOtherSourcesPublish(){
        var Store=new ContentAddressedStore(this.Root);
        var IndexPath=Path.Combine(this.Root,"content-index.json");
        var Compiler=new ModelContentCompiler(Store,Path.Combine(this.Root,"state.json"),IndexPath);
        var First=ModelArtifactPreparation.Prepare(Asset,Triangle(),Source,Options,"test@1");
        var Initial=await Compiler.CompileAsync([new(Asset,First.SourceUri,_=>Task.FromResult(First))]);
        Assert.True(Initial.Succeeded);
        var OtherAsset=AssetId.Parse("00000000-0000-5000-8000-000000000002");
        var OtherDocument=Triangle() with { Provenance=Triangle().Provenance with { SourceUri="Assets/other.glb" } };
        var Other=ModelArtifactPreparation.Prepare(OtherAsset,OtherDocument,Source,Options,"test@1");
        var Result=await Compiler.CompileAsync([
            new(Asset,First.SourceUri,_=>throw new InvalidDataException("malformed source")),
            new(OtherAsset,Other.SourceUri,_=>Task.FromResult(Other))
        ]);
        Assert.False(Result.Succeeded);
        Assert.Single(Result.Diagnostics);
        Assert.Equal(6,Result.Index.Entries.Length);
        Assert.All(Result.Index.Entries.Where(E=>E.Asset==Asset.ToString()),E=>Assert.True(E.LastKnownGood));
        Assert.All(Result.Index.Entries.Where(E=>E.Asset==OtherAsset.ToString()),E=>Assert.False(E.LastKnownGood));
        Assert.Equal(Initial.Index.Resolve(First.SourceUri,ContentArtifactKind.Model),Result.Index.Resolve(First.SourceUri,ContentArtifactKind.Model));
        Assert.Equal(Result.Index.Write(),ModelContentIndex.Read(await File.ReadAllBytesAsync(IndexPath)).Write());
        var Restored=await Compiler.CompileAsync([new(Asset,First.SourceUri,_=>Task.FromResult(First)),new(OtherAsset,Other.SourceUri,_=>Task.FromResult(Other))]);
        Assert.True(Restored.Succeeded);
        Assert.Equal(6,Restored.Compilation!.Reused.Length);
        Assert.All(Restored.Index.Entries,E=>Assert.False(E.LastKnownGood));
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
        Assert.Throws<InvalidDataException>(()=>Index.Resolve(Prepared.SourceUri,ContentArtifactKind.Mesh));
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
