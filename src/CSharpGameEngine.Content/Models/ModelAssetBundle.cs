using System.Collections.Immutable;
using CSharpGameEngine.Contracts;
namespace CSharpGameEngine.Content.Models;
public sealed class ModelAssetBundle:IDisposable,IAsyncDisposable {
    private readonly IContentArtifactReader Reader;
    private readonly RuntimeAssetStore Store;
    private readonly List<IDisposable>Leases=[];
    private int Disposed;
    private ModelAssetBundle(IContentArtifactReader Reader,ModelContentIndex Index,ShippingContentManifest Manifest,RuntimeAssetStore Store){
        this.Reader=Reader;this.Index=Index;this.Manifest=Manifest;this.Store=Store;
    }
    public ModelContentIndex Index{get;}
    public ShippingContentManifest Manifest{get;}
    public ModelData Model{get;private set;}=null!;
    public ImmutableArray<ModelMeshData>Meshes{get;private set;}=[];
    public ImmutableArray<ModelSkeletonData>Skeletons{get;private set;}=[];
    public ImmutableArray<ModelAnimationClipData>Clips{get;private set;}=[];
    public static async Task<ModelAssetBundle>OpenAsync(string ContentDirectory,string SourceUri,CancellationToken CancellationToken=default){
        var Root=Path.GetFullPath(ContentDirectory);var IndexPath=Path.Combine(Root,"content-index.json");
        if(new FileInfo(IndexPath).Length>16*1024*1024){ throw new InvalidDataException("Model content index exceeds its bound."); }
        var Index=ModelContentIndex.Read(await File.ReadAllBytesAsync(IndexPath,CancellationToken).ConfigureAwait(false));
        AssetArtifactReference ModelReference;
        try { ModelReference=Index.Resolve(SourceUri,ContentArtifactKind.Model); }
        catch(KeyNotFoundException Error){ throw new FileNotFoundException("Published model was not found: "+SourceUri,SourceUri,Error); }
        var Reader=new ModelArtifactReader(new ContentAddressedStore(Root),Index);
        var Manifest=await ShippingContentManifestBuilder.BuildAsync(Reader,Index,[ModelReference],CancellationToken).ConfigureAwait(false);
        var Store=new RuntimeAssetStore(Reader,Manifest,ModelRuntimeCodecs.Create());
        var Bundle=new ModelAssetBundle(Reader,Index,Manifest,Store);
        try {
            Bundle.Model=await Bundle.RetainAsync<ModelData>(ModelReference,CancellationToken).ConfigureAwait(false);
            var Meshes=ImmutableArray.CreateBuilder<ModelMeshData>();var Skeletons=ImmutableArray.CreateBuilder<ModelSkeletonData>();var Clips=ImmutableArray.CreateBuilder<ModelAnimationClipData>();
            foreach(var Mesh in Bundle.Model.Meshes){ Meshes.Add(await Bundle.RetainAsync<ModelMeshData>(Mesh.ToReference(),CancellationToken).ConfigureAwait(false)); }
            foreach(var Skeleton in Bundle.Model.Skeletons){ Skeletons.Add(await Bundle.RetainAsync<ModelSkeletonData>(Skeleton.ToReference(),CancellationToken).ConfigureAwait(false)); }
            foreach(var Clip in Index.Entries.Where(Entry=>Entry.SourceUri==SourceUri&&Entry.Kind==ContentArtifactKind.AnimationClip)){
                Clips.Add(await Bundle.RetainAsync<ModelAnimationClipData>(Reference(Clip),CancellationToken).ConfigureAwait(false));
            }
            foreach(var Material in Bundle.Model.Materials){ Bundle.RequireReference(Material.ToReference()); }
            Bundle.Meshes=Meshes.ToImmutable();Bundle.Skeletons=Skeletons.ToImmutable();Bundle.Clips=ModelClipOrder.Apply(Bundle.Model,Clips.ToImmutable());
            return Bundle;
        }catch{ await Bundle.DisposeAsync().ConfigureAwait(false);throw; }
    }
    public async ValueTask<ReadOnlyMemory<byte>>ReadEnvelopeAsync(ModelReference Reference,CancellationToken CancellationToken=default){
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.Disposed)!=0,this);
        var Entry=this.RequireReference(Reference.ToReference());
        var Bytes=await this.Reader.ReadAsync(Entry.Address,CancellationToken).ConfigureAwait(false);
        if(!ManifestArtifactValidator.TryValidate(Bytes,Entry,out _,out var Error)){ throw new InvalidDataException("Model resource differs from its shipping manifest: "+Error); }
        ObjectDisposedException.ThrowIf(Volatile.Read(ref this.Disposed)!=0,this);
        return Bytes;
    }
    private ShippingContentManifestEntry RequireReference(AssetArtifactReference Reference){
        if(!this.Manifest.TryResolve(Reference.Key,out var Entry)||Entry is null||Entry.Reference!=Reference){ throw new InvalidDataException("Model payload reference differs from its declared dependency closure."); }
        return Entry;
    }
    private async Task<T>RetainAsync<T>(AssetArtifactReference Reference,CancellationToken CancellationToken)where T:notnull{
        this.RequireReference(Reference);
        var Handle=await this.Store.RetainAsync<T>(Reference.Key,CancellationToken).ConfigureAwait(false);this.Leases.Add(Handle);return Handle.Value;
    }
    private static AssetArtifactReference Reference(ModelContentEntry Entry){
        _=AssetUri.TryParse(Entry.Uri,out var Uri,out _);_=Hash256.TryParse(Entry.Hash,out var Hash);return new(Uri.Key,Hash);
    }
    public void Dispose()=>this.DisposeAsync().AsTask().GetAwaiter().GetResult();
    public async ValueTask DisposeAsync(){
        if(Interlocked.Exchange(ref this.Disposed,1)!=0){ return; }
        foreach(var Lease in this.Leases.AsEnumerable().Reverse()){ Lease.Dispose(); }
        this.Leases.Clear();await this.Store.DisposeAsync().ConfigureAwait(false);
    }
}
