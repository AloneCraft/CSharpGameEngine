using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.World;

/// <summary>公開された1つのインデックススナップショットで解決した、不変CASオブジェクトに対する保持中のワールドセルリース。</summary>
public sealed class ContentAddressedWorldCellArtifactSource:IWorldCellArtifactSource {
    private readonly object Gate=new();
    private readonly Dictionary<AssetArtifactReference,CacheEntry>Cache=[];
    private readonly ContentAddressedStore Store;
    private readonly IContentArtifactLocator Locator;

    public ContentAddressedWorldCellArtifactSource(ContentAddressedStore Store,IContentArtifactLocator Locator){
        this.Store=Store??throw new ArgumentNullException(nameof(Store));
        this.Locator=Locator??throw new ArgumentNullException(nameof(Locator));
    }

    public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(AssetArtifactReference Reference,CancellationToken CancellationToken){
        if(!Reference.IsValid){ throw new ArgumentException("A valid artifact reference is required.",nameof(Reference)); }
        CacheEntry Entry;
        lock(this.Gate){
            if(this.Cache.TryGetValue(Reference,out Entry!)){ Entry.RetainCount++; }
            else {
                if(!this.Locator.TryResolve(Reference,out var Address)){
                    throw new FileNotFoundException($"Artifact reference is absent from the published CAS index: {Reference.Key}.");
                }
                Entry=new CacheEntry(this.Store.ReadAsync(Address,CancellationToken).AsTask());
                this.Cache.Add(Reference,Entry);
            }
        }
        try { return await Entry.Load.ConfigureAwait(false); }
        catch {
            this.ReleaseCore(Reference,Entry);
            throw;
        }
    }

    public ValueTask ReleaseAsync(AssetArtifactReference Reference){
        lock(this.Gate){
            if(!this.Cache.TryGetValue(Reference,out var Entry)){
                throw new InvalidOperationException($"Artifact reference has no retained CAS lease: {Reference.Key}.");
            }
            if(--Entry.RetainCount==0){ this.Cache.Remove(Reference); }
        }
        return ValueTask.CompletedTask;
    }

    private void ReleaseCore(AssetArtifactReference Reference,CacheEntry Entry){
        lock(this.Gate){
            if(!this.Cache.TryGetValue(Reference,out var Current)||!ReferenceEquals(Current,Entry)){ return; }
            if(--Entry.RetainCount==0){ this.Cache.Remove(Reference); }
        }
    }

    private sealed class CacheEntry(Task<ReadOnlyMemory<byte>>Load){
        internal readonly Task<ReadOnlyMemory<byte>>Load=Load;
        internal int RetainCount=1;
    }
}
