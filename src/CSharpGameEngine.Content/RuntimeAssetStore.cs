using System.Collections.Immutable;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public enum RuntimeAssetState:byte { Loading,Ready,Faulted }
public enum RuntimeAssetCancellationSource:byte { None,Caller,ManifestUpdate,StoreDisposal }

public readonly record struct RuntimeAssetDiagnostics(
    AssetKey AssetKey,AssetArtifactReference Reference,Type AssetType,
    int RetainCount,RuntimeAssetState State,
    ImmutableArray<AssetKey>DependencyChain,
    RuntimeAssetCancellationSource LastCancellationSource,
    bool LastKnownGoodFallback,string Error);

public readonly record struct RuntimeAssetManifestUpdateResult(
    bool Succeeded,bool Cancelled,bool UsedLastKnownGood,
    Hash256 ActiveManifestFingerprint,string Error);

public interface IRuntimeAssetCodec {
    Type AssetType{get;}
    bool Supports(ContentArtifactKind Kind);
    ValueTask<object>DecodeAsync(ContentArtifact Artifact,CancellationToken CancellationToken);
    void Release(object Value);
}

/// <summary>The explicit L4 adapter from one canonical artifact kind set to one engine-owned immutable runtime value type.</summary>
public sealed class RuntimeAssetCodec<T>:IRuntimeAssetCodec where T:notnull {
    private readonly Func<ContentArtifactKind,bool>SupportsValue;
    private readonly Func<ContentArtifact,CancellationToken,ValueTask<T>>Decode;
    private readonly Action<T>?ReleaseValue;

    public RuntimeAssetCodec(
        Func<ContentArtifactKind,bool>Supports,
        Func<ContentArtifact,CancellationToken,ValueTask<T>>Decode,
        Action<T>?Release=null){
        this.SupportsValue=Supports??throw new ArgumentNullException(nameof(Supports));
        this.Decode=Decode??throw new ArgumentNullException(nameof(Decode));
        this.ReleaseValue=Release;
    }

    public Type AssetType=>typeof(T);
    public bool Supports(ContentArtifactKind Kind)=>this.SupportsValue(Kind);
    public async ValueTask<object>DecodeAsync(ContentArtifact Artifact,CancellationToken CancellationToken)=>
        await this.Decode(Artifact,CancellationToken).ConfigureAwait(false);
    public void Release(object Value){
        if(Value is not T Typed){ throw new ArgumentException($"Runtime asset value is not {typeof(T)}.",nameof(Value)); }
        this.ReleaseValue?.Invoke(Typed);
    }
}

internal interface IAssetHandleLease {
    bool IsValid{get;}
    RuntimeAssetDiagnostics Diagnostics{get;}
    void Release();
}

public sealed class AssetHandle<T>:IDisposable,IAsyncDisposable where T:notnull {
    private IAssetHandleLease?Lease;

    internal AssetHandle(AssetArtifactReference Reference,T Value,IAssetHandleLease Lease){
        this.Reference=Reference;
        this.ValueValue=Value;
        this.Lease=Lease;
    }

    public AssetArtifactReference Reference{get;}
    public AssetKey AssetKey=>this.Reference.Key;
    public T Value{
        get {
            if(this.Lease is not {IsValid:true}){ throw new ObjectDisposedException(nameof(AssetHandle<T>)); }
            return this.ValueValue;
        }
    }
    public RuntimeAssetDiagnostics Diagnostics=>
        this.Lease?.Diagnostics??throw new ObjectDisposedException(nameof(AssetHandle<T>));
    private T ValueValue{get;}

    public void Dispose(){ Interlocked.Exchange(ref this.Lease,null)?.Release(); }
    public ValueTask DisposeAsync(){ this.Dispose();return ValueTask.CompletedTask; }
}

/// <summary>Owns shared typed runtime leases and their validated immutable artifact closure.</summary>
public sealed class RuntimeAssetStore:IAsyncDisposable {
    private readonly object Gate=new();
    private readonly IContentArtifactReader Reader;
    private readonly ImmutableDictionary<Type,IRuntimeAssetCodec>Codecs;
    private readonly Dictionary<TypedKey,TypedEntry>TypedEntries=[];
    private readonly Dictionary<RawKey,RawEntry>RawEntries=[];
    private readonly CancellationTokenSource Lifetime=new();
    private readonly SemaphoreSlim LoadConcurrency;
    private readonly SemaphoreSlim ManifestUpdates=new(1,1);
    private ShippingContentManifest ActiveManifest;
    private bool Disposed;
    private bool LastManifestUpdateUsedLastKnownGoodValue;
    private Task?DisposalTask;

    public RuntimeAssetStore(
        IContentArtifactReader Reader,ShippingContentManifest InitialManifest,
        IEnumerable<IRuntimeAssetCodec>Codecs,int MaximumConcurrentLoads=8){
        this.Reader=Reader??throw new ArgumentNullException(nameof(Reader));
        this.ActiveManifest=InitialManifest??throw new ArgumentNullException(nameof(InitialManifest));
        if(MaximumConcurrentLoads<=0){ throw new ArgumentOutOfRangeException(nameof(MaximumConcurrentLoads)); }
        this.LoadConcurrency=new SemaphoreSlim(MaximumConcurrentLoads,MaximumConcurrentLoads);
        ArgumentNullException.ThrowIfNull(Codecs);
        var Builder=ImmutableDictionary.CreateBuilder<Type,IRuntimeAssetCodec>();
        foreach(var Codec in Codecs){
            if(Codec is null){ throw new ArgumentException("Runtime codec collection cannot contain null.",nameof(Codecs)); }
            if(!Builder.TryAdd(Codec.AssetType,Codec)){
                throw new ArgumentException($"Runtime codec type is registered more than once: {Codec.AssetType}.",nameof(Codecs));
            }
        }
        this.Codecs=Builder.ToImmutable();
    }

    public int ResidentAssetCount{get { lock(this.Gate){ return this.TypedEntries.Count; } }}
    public int ResidentArtifactCount{get { lock(this.Gate){ return this.RawEntries.Count; } }}
    public Hash256 ActiveManifestFingerprint{get { lock(this.Gate){ return this.ActiveManifest.Fingerprint; } }}
    public bool LastManifestUpdateUsedLastKnownGood{get { lock(this.Gate){ return this.LastManifestUpdateUsedLastKnownGoodValue; } }}

    public async ValueTask<AssetHandle<T>>RetainAsync<T>(
        AssetKey Key,CancellationToken CancellationToken=default) where T:notnull {
        TypedEntry Entry;
        lock(this.Gate){
            this.ThrowIfDisposed();
            if(!Key.IsValid){ throw new ArgumentException("A valid AssetKey is required.",nameof(Key)); }
            if(!this.ActiveManifest.TryResolve(Key,out var ManifestEntry)||ManifestEntry is null){
                throw new KeyNotFoundException($"AssetKey is absent from the active shipping manifest: {Key}.");
            }
            if(!this.Codecs.TryGetValue(typeof(T),out var Codec)){
                throw new InvalidOperationException($"No runtime asset codec is registered for {typeof(T)}.");
            }
            if(!Codec.Supports(ManifestEntry.Kind)){
                throw new InvalidOperationException($"Runtime asset codec {typeof(T)} does not support artifact kind {ManifestEntry.Kind}.");
            }
            var TypedKey=new TypedKey(ManifestEntry.Reference,ManifestEntry.Address,typeof(T));
            if(!this.TypedEntries.TryGetValue(TypedKey,out Entry!)){
                ImmutableArray<RawEntry>Artifacts=this.AcquireArtifacts(this.ActiveManifest,ManifestEntry.Reference);
                RawEntry RootArtifact=Artifacts.Single(Artifact=>Artifact.ManifestEntry.Reference==ManifestEntry.Reference);
                Entry=new TypedEntry(TypedKey,Codec,Artifacts);
                Entry.RetainCount=1;
                this.TypedEntries.Add(TypedKey,Entry);
                Entry.LoadTask=this.LoadTypedAsync(Entry,RootArtifact);
                _=this.ObserveCompletionAsync(Entry);
            } else {
                checked { Entry.RetainCount++; }
            }
        }
        try {
            object Value=await Entry.LoadTask.WaitAsync(CancellationToken).ConfigureAwait(false);
            lock(this.Gate){ this.ThrowIfDisposed(); }
            return new AssetHandle<T>(Entry.Key.Reference,(T)Value,new AssetLease(this,Entry));
        }catch(OperationCanceledException) when(CancellationToken.IsCancellationRequested){
            lock(this.Gate){ Entry.LastCancellationSource=RuntimeAssetCancellationSource.Caller; }
            this.Release(Entry);
            throw;
        }catch{
            this.Release(Entry);
            throw;
        }
    }

    public ImmutableArray<RuntimeAssetDiagnostics>GetDiagnostics(AssetKey Key){
        lock(this.Gate){
            return this.TypedEntries.Values
                .Where(Entry=>Entry.Key.Reference.Key==Key)
                .OrderBy(static Entry=>Entry.Key.AssetType.FullName,StringComparer.Ordinal)
                .Select(this.CreateDiagnostics)
                .ToImmutableArray();
        }
    }

    public async ValueTask<RuntimeAssetManifestUpdateResult>TryUpdateManifestAsync(
        ShippingContentManifest Candidate,CancellationToken CancellationToken=default){
        ArgumentNullException.ThrowIfNull(Candidate);
        using var LinkedCancellation=CancellationTokenSource.CreateLinkedTokenSource(CancellationToken,this.Lifetime.Token);
        try {
            await this.ManifestUpdates.WaitAsync(LinkedCancellation.Token).ConfigureAwait(false);
        }catch(OperationCanceledException) when(CancellationToken.IsCancellationRequested){
            lock(this.Gate){
                this.ThrowIfDisposed();
                this.LastManifestUpdateUsedLastKnownGoodValue=true;
                return new(false,true,true,this.ActiveManifest.Fingerprint,"Manifest update was cancelled; the last-known-good manifest remains active.");
            }
        }catch(OperationCanceledException) when(this.Lifetime.IsCancellationRequested){
            throw new ObjectDisposedException(nameof(RuntimeAssetStore));
        }
        try {
            lock(this.Gate){
                this.ThrowIfDisposed();
                if(Candidate.Fingerprint==this.ActiveManifest.Fingerprint){
                    this.LastManifestUpdateUsedLastKnownGoodValue=false;
                    return new(true,false,false,this.ActiveManifest.Fingerprint,string.Empty);
                }
            }
            try {
                foreach(var Entry in Candidate.Entries){
                    LinkedCancellation.Token.ThrowIfCancellationRequested();
                    ReadOnlyMemory<byte>Bytes=await this.Reader.ReadAsync(Entry.Address,LinkedCancellation.Token).ConfigureAwait(false);
                    if(!ManifestArtifactValidator.TryValidate(Bytes,Entry,out _,out string Error)){
                        throw new InvalidDataException($"Candidate manifest artifact {Entry.Reference.Key} is invalid: {Error}");
                    }
                }
            }catch(OperationCanceledException) when(CancellationToken.IsCancellationRequested){
                lock(this.Gate){
                    this.ThrowIfDisposed();
                    this.LastManifestUpdateUsedLastKnownGoodValue=true;
                    return new(false,true,true,this.ActiveManifest.Fingerprint,"Manifest update was cancelled; the last-known-good manifest remains active.");
                }
            }catch(OperationCanceledException) when(this.Lifetime.IsCancellationRequested){
                throw new ObjectDisposedException(nameof(RuntimeAssetStore));
            }catch(Exception Exception) when(Exception is IOException or InvalidDataException or UnauthorizedAccessException){
                lock(this.Gate){
                    this.ThrowIfDisposed();
                    this.LastManifestUpdateUsedLastKnownGoodValue=true;
                    return new(false,false,true,this.ActiveManifest.Fingerprint,Exception.Message);
                }
            }
            lock(this.Gate){
                this.ThrowIfDisposed();
                this.ActiveManifest=Candidate;
                this.LastManifestUpdateUsedLastKnownGoodValue=false;
                return new(true,false,false,this.ActiveManifest.Fingerprint,string.Empty);
            }
        } finally { this.ManifestUpdates.Release(); }
    }

    public ValueTask DisposeAsync(){
        lock(this.Gate){
            this.DisposalTask??=this.DisposeCoreAsync();
            return new ValueTask(this.DisposalTask);
        }
    }

    private async Task DisposeCoreAsync(){
        TypedEntry[]Entries;
        lock(this.Gate){
            this.Disposed=true;
            this.Lifetime.Cancel();
            Entries=this.TypedEntries.Values.ToArray();
            foreach(var Entry in Entries){
                Entry.RetainCount=0;
                Entry.LastCancellationSource=RuntimeAssetCancellationSource.StoreDisposal;
            }
        }
        foreach(var Entry in Entries){
            try {
                _=await Entry.LoadTask.ConfigureAwait(false);
            }catch{ }
            try { await this.EvictCompletedAsync(Entry).ConfigureAwait(false); }
            catch(Exception Exception){ Entry.Error=Exception.Message; }
        }
        this.Lifetime.Dispose();
        this.LoadConcurrency.Dispose();
    }

    private ImmutableArray<RawEntry>AcquireArtifacts(
        ShippingContentManifest Manifest,AssetArtifactReference Root){
        ImmutableArray<ShippingContentManifestEntry>Closure=Manifest.GetDependencyClosure(Root);
        var Result=ImmutableArray.CreateBuilder<RawEntry>(Closure.Length);
        foreach(var ManifestEntry in Closure){
            var Key=new RawKey(ManifestEntry.Reference,ManifestEntry.Address);
            if(!this.RawEntries.TryGetValue(Key,out var Entry)){
                Entry=new RawEntry(Key,ManifestEntry);
                this.RawEntries.Add(Key,Entry);
                Entry.LoadTask=this.LoadArtifactAsync(ManifestEntry);
            }
            checked { Entry.RetainCount++; }
            Result.Add(Entry);
        }
        return Result.MoveToImmutable();
    }

    private async Task<ContentArtifact>LoadArtifactAsync(ShippingContentManifestEntry Entry){
        await this.LoadConcurrency.WaitAsync(this.Lifetime.Token).ConfigureAwait(false);
        try {
            ReadOnlyMemory<byte>Bytes=await this.Reader.ReadAsync(Entry.Address,this.Lifetime.Token).ConfigureAwait(false);
            if(!ManifestArtifactValidator.TryValidate(Bytes,Entry,out var Artifact,out string Error)||Artifact is null){
                throw new InvalidDataException($"Runtime artifact {Entry.Reference.Key} does not match the active manifest: {Error}");
            }
            return Artifact;
        } finally {
            this.LoadConcurrency.Release();
        }
    }

    private async Task<object>LoadTypedAsync(TypedEntry Entry,RawEntry RootArtifact){
        try {
            await Task.WhenAll(Entry.Artifacts.Select(static Artifact=>Artifact.LoadTask)).ConfigureAwait(false);
            ContentArtifact Artifact=await RootArtifact.LoadTask.ConfigureAwait(false);
            object Value=await Entry.Codec.DecodeAsync(Artifact,this.Lifetime.Token).ConfigureAwait(false);
            if(Value is null||!Entry.Key.AssetType.IsInstanceOfType(Value)){
                throw new InvalidDataException($"Runtime codec returned an invalid value for {Entry.Key.AssetType}.");
            }
            lock(this.Gate){
                Entry.DecodedValue=Value;
                Entry.HasDecodedValue=true;
                Entry.State=RuntimeAssetState.Ready;
            }
            return Value;
        }catch(Exception Exception){
            lock(this.Gate){ Entry.State=RuntimeAssetState.Faulted;Entry.Error=Exception.Message; }
            throw;
        }
    }

    private async Task ObserveCompletionAsync(TypedEntry Entry){
        try {
            _=await Entry.LoadTask.ConfigureAwait(false);
        }catch{ }
        try { await this.EvictCompletedAsync(Entry).ConfigureAwait(false); }
        catch(Exception Exception){ Entry.Error=Exception.Message; }
    }

    private void Release(TypedEntry Entry){
        lock(this.Gate){
            if(Entry.EvictionScheduled){ return; }
            if(Entry.RetainCount<=0){
                if(this.Disposed){ return; }
                throw new InvalidOperationException("Runtime asset lease was released more than once.");
            }
            Entry.RetainCount--;
        }
        this.TryEvict(Entry);
    }

    private void TryEvict(TypedEntry Entry){
        Task?Cleanup=null;
        lock(this.Gate){
            if(Entry.RetainCount!=0||!Entry.LoadTask.IsCompleted){ return; }
            if(Entry.EvictionScheduled){ Cleanup=Entry.CleanupCompletion.Task; }
        }
        (Cleanup??this.EvictCompletedAsync(Entry)).GetAwaiter().GetResult();
    }

    private Task EvictCompletedAsync(TypedEntry Entry){
        object?Value;
        lock(this.Gate){
            if(Entry.EvictionScheduled){ return Entry.CleanupCompletion.Task; }
            if(Entry.RetainCount!=0){ return Task.CompletedTask; }
            Entry.EvictionScheduled=true;
            this.TypedEntries.Remove(Entry.Key);
            Value=Entry.HasDecodedValue?Entry.DecodedValue:null;
            Entry.DecodedValue=null;
            Entry.HasDecodedValue=false;
        }
        Exception?Failure=null;
        try {
            if(Value is not null){ Entry.Codec.Release(Value); }
        }catch(Exception Exception){
            Failure=Exception;
        } finally {
            lock(this.Gate){
                foreach(var Artifact in Entry.Artifacts){
                    Artifact.RetainCount--;
                    if(Artifact.RetainCount<0){ throw new InvalidOperationException("Runtime artifact ownership became negative."); }
                    if(Artifact.RetainCount==0&&Artifact.LoadTask.IsCompleted){ this.RawEntries.Remove(Artifact.Key); }
                }
            }
        }
        if(Failure is null){ Entry.CleanupCompletion.TrySetResult(); }
        else { Entry.CleanupCompletion.TrySetException(Failure); }
        return Entry.CleanupCompletion.Task;
    }

    private RuntimeAssetDiagnostics CreateDiagnostics(TypedEntry Entry)=>new(
        Entry.Key.Reference.Key,Entry.Key.Reference,Entry.Key.AssetType,
        Entry.RetainCount,Entry.State,
        Entry.Artifacts.Select(static Artifact=>Artifact.Key.Reference.Key).ToImmutableArray(),
        Entry.LastCancellationSource,this.LastManifestUpdateUsedLastKnownGoodValue,Entry.Error);

    private void ThrowIfDisposed(){
        if(this.Disposed){ throw new ObjectDisposedException(nameof(RuntimeAssetStore)); }
    }

    private sealed class AssetLease(RuntimeAssetStore Owner,TypedEntry Entry):IAssetHandleLease {
        private RuntimeAssetStore?OwnerValue=Owner;
        public bool IsValid{
            get {
                RuntimeAssetStore?Current=this.OwnerValue;
                if(Current is null){ return false; }
                lock(Current.Gate){ return !Current.Disposed&&!Entry.EvictionScheduled; }
            }
        }
        public RuntimeAssetDiagnostics Diagnostics{
            get {
                RuntimeAssetStore Current=this.OwnerValue??throw new ObjectDisposedException(nameof(AssetLease));
                lock(Current.Gate){ return Current.CreateDiagnostics(Entry); }
            }
        }
        public void Release(){ Interlocked.Exchange(ref this.OwnerValue,null)?.Release(Entry); }
    }
    private readonly record struct RawKey(AssetArtifactReference Reference,Hash256 Address);
    private readonly record struct TypedKey(AssetArtifactReference Reference,Hash256 Address,Type AssetType);
    private sealed class RawEntry(RawKey Key,ShippingContentManifestEntry ManifestEntry) {
        internal RawKey Key{get;}=Key;
        internal ShippingContentManifestEntry ManifestEntry{get;}=ManifestEntry;
        internal Task<ContentArtifact>LoadTask{get;set;}=null!;
        internal int RetainCount{get;set;}
    }
    private sealed class TypedEntry(TypedKey Key,IRuntimeAssetCodec Codec,ImmutableArray<RawEntry>Artifacts) {
        internal TypedKey Key{get;}=Key;
        internal IRuntimeAssetCodec Codec{get;}=Codec;
        internal ImmutableArray<RawEntry>Artifacts{get;}=Artifacts;
        internal Task<object>LoadTask{get;set;}=null!;
        internal int RetainCount{get;set;}
        internal RuntimeAssetState State{get;set;}=RuntimeAssetState.Loading;
        internal RuntimeAssetCancellationSource LastCancellationSource{get;set;}
        internal string Error{get;set;}=string.Empty;
        internal bool EvictionScheduled{get;set;}
        internal object?DecodedValue{get;set;}
        internal bool HasDecodedValue{get;set;}
        internal TaskCompletionSource CleanupCompletion{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
