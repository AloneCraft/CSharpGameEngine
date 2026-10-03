using System.Text;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.Content.Tests;

public sealed class RuntimeAssetStoreTests:IDisposable {
    private static readonly StableId Namespace=StableId.Parse("37b56374-8e38-5239-9fa8-dd5095615f13");
    private readonly string Root=Path.Combine(Path.GetTempPath(),"CgeRuntimeAssetTests",Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ConcurrentRetains_JoinOneLoadAndReleaseAfterTheFinalHandle(){
        var Fixture=await this.CreateManifestAsync("first");
        var LoadGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int LoadCount=0;
        int ReleaseCount=0;
        var Codec=new RuntimeAssetCodec<string>(
            static Kind=>Kind==ContentArtifactKind.Mesh,
            async(Artifact,CancellationToken)=>{
                Interlocked.Increment(ref LoadCount);
                await LoadGate.Task.WaitAsync(CancellationToken);
                return Encoding.UTF8.GetString(Artifact.Payload.AsSpan());
            },
            _=>Interlocked.Increment(ref ReleaseCount));
        await using var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);

        Task<AssetHandle<string>>FirstTask=Runtime.RetainAsync<string>(Fixture.Key).AsTask();
        Task<AssetHandle<string>>SecondTask=Runtime.RetainAsync<string>(Fixture.Key).AsTask();
        Assert.True(SpinWait.SpinUntil(()=>Volatile.Read(ref LoadCount)==1,TimeSpan.FromSeconds(5)));
        LoadGate.SetResult();
        AssetHandle<string>First=await FirstTask;
        AssetHandle<string>Second=await SecondTask;

        Assert.Equal("first",First.Value);
        Assert.Equal(1,LoadCount);
        Assert.Equal(2,First.Diagnostics.RetainCount);
        First.Dispose();
        Assert.Equal(0,ReleaseCount);
        Second.Dispose();
        Assert.Equal(1,ReleaseCount);
        Assert.Equal(0,Runtime.ResidentAssetCount);
        Assert.Equal(0,Runtime.ResidentArtifactCount);
    }

    [Fact]
    public async Task CallerCancellation_DetachesOnlyThatRequester(){
        var Fixture=await this.CreateManifestAsync("shared");
        var LoadGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            async(Artifact,CancellationToken)=>{
                await LoadGate.Task.WaitAsync(CancellationToken);
                return Encoding.UTF8.GetString(Artifact.Payload.AsSpan());
            });
        await using var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);
        using var Cancel=new CancellationTokenSource();

        Task<AssetHandle<string>>Cancelled=Runtime.RetainAsync<string>(Fixture.Key,Cancel.Token).AsTask();
        Task<AssetHandle<string>>Survivor=Runtime.RetainAsync<string>(Fixture.Key).AsTask();
        Cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await Cancelled);
        RuntimeAssetDiagnostics DuringLoad=Assert.Single(Runtime.GetDiagnostics(Fixture.Key));
        Assert.Equal(RuntimeAssetCancellationSource.Caller,DuringLoad.LastCancellationSource);
        Assert.Equal(1,DuringLoad.RetainCount);

        LoadGate.SetResult();
        AssetHandle<string>Handle=await Survivor;
        Assert.Equal("shared",Handle.Value);
        Handle.Dispose();
    }

    [Fact]
    public async Task ManifestUpdate_IsAtomicAndKeepsLastKnownGoodOnFailureOrCancellation(){
        var First=await this.CreateManifestAsync("first");
        var Second=await this.CreateManifestAsync("second",First.Key,First.Store);
        var Reader=new SwitchableArtifactReader(First.Store,Second.Manifest.Entries[0].Address);
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            static(Artifact,_)=>ValueTask.FromResult(Encoding.UTF8.GetString(Artifact.Payload.AsSpan())));
        await using var Runtime=new RuntimeAssetStore(Reader,First.Manifest,[Codec]);

        RuntimeAssetManifestUpdateResult Failed=await Runtime.TryUpdateManifestAsync(Second.Manifest);
        Assert.False(Failed.Succeeded);
        Assert.True(Failed.UsedLastKnownGood);
        Assert.Equal(First.Manifest.Fingerprint,Runtime.ActiveManifestFingerprint);

        using(var Cancel=new CancellationTokenSource()){
            Cancel.Cancel();
            RuntimeAssetManifestUpdateResult Cancelled=await Runtime.TryUpdateManifestAsync(Second.Manifest,Cancel.Token);
            Assert.True(Cancelled.Cancelled);
            Assert.True(Cancelled.UsedLastKnownGood);
            Assert.Equal(First.Manifest.Fingerprint,Runtime.ActiveManifestFingerprint);
        }

        Reader.FailAddress=null;
        RuntimeAssetManifestUpdateResult Succeeded=await Runtime.TryUpdateManifestAsync(Second.Manifest);
        Assert.True(Succeeded.Succeeded);
        Assert.False(Succeeded.UsedLastKnownGood);
        Assert.Equal(Second.Manifest.Fingerprint,Runtime.ActiveManifestFingerprint);
        using AssetHandle<string>Handle=await Runtime.RetainAsync<string>(First.Key);
        Assert.Equal("second",Handle.Value);
    }

    [Fact]
    public async Task ManifestUpdate_CancelledWhileItsLastArtifactIsRead_KeepsTheOldManifest(){
        // Audit AUD-002: the caller cancels after the last candidate artifact was read and validated, before the update
        // commits. The cancellation wins: the old manifest stays the authority and the result says so.
        var First=await this.CreateManifestAsync("first");
        var Second=await this.CreateManifestAsync("second",First.Key,First.Store);
        using var Cancel=new CancellationTokenSource();
        var Reader=new CancelAfterReadReader(First.Store,Second.Manifest.Entries[^1].Address,Cancel);
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            static(Artifact,_)=>ValueTask.FromResult(Encoding.UTF8.GetString(Artifact.Payload.AsSpan())));
        await using var Runtime=new RuntimeAssetStore(Reader,First.Manifest,[Codec]);

        RuntimeAssetManifestUpdateResult Result=await Runtime.TryUpdateManifestAsync(Second.Manifest,Cancel.Token);

        Assert.True(Reader.Cancelled);
        Assert.False(Result.Succeeded);
        Assert.True(Result.Cancelled);
        Assert.True(Result.UsedLastKnownGood);
        Assert.Equal(First.Manifest.Fingerprint,Runtime.ActiveManifestFingerprint);
    }

    [Fact]
    public async Task UnsupportedTypeAndDecodeFailure_DoNotLeakOwnership(){
        var Fixture=await this.CreateManifestAsync("broken");
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            static(_,_)=>ValueTask.FromException<string>(new InvalidDataException("decode failed")));
        await using var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);

        await Assert.ThrowsAsync<InvalidOperationException>(async()=>await Runtime.RetainAsync<int>(Fixture.Key));
        await Assert.ThrowsAsync<InvalidDataException>(async()=>await Runtime.RetainAsync<string>(Fixture.Key));
        Assert.Equal(0,Runtime.ResidentAssetCount);
        Assert.Equal(0,Runtime.ResidentArtifactCount);
    }

    [Fact]
    public async Task SoleCancelledRequester_IsEvictedAfterSharedLoadCompletes(){
        var Fixture=await this.CreateManifestAsync("cancelled");
        var LoadGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var Started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            async(Artifact,_)=>{
                Started.SetResult();
                await LoadGate.Task;
                return Encoding.UTF8.GetString(Artifact.Payload.AsSpan());
            });
        await using var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);
        using var Cancel=new CancellationTokenSource();

        Task<AssetHandle<string>>Retain=Runtime.RetainAsync<string>(Fixture.Key,Cancel.Token).AsTask();
        await Started.Task;
        Cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async()=>await Retain);
        Assert.Equal(1,Runtime.ResidentAssetCount);
        LoadGate.SetResult();
        Assert.True(SpinWait.SpinUntil(
            ()=>Runtime.ResidentAssetCount==0&&Runtime.ResidentArtifactCount==0,
            TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task StoreDisposal_RacingLoadNeverPublishesAReleasedHandle(){
        var Fixture=await this.CreateManifestAsync("teardown");
        var LoadGate=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var Started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int DecodeCompletionCount=0;
        int ReleaseCount=0;
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            async(Artifact,_)=>{
                Started.SetResult();
                await LoadGate.Task;
                Interlocked.Increment(ref DecodeCompletionCount);
                return Encoding.UTF8.GetString(Artifact.Payload.AsSpan());
            },
            _=>Interlocked.Increment(ref ReleaseCount));
        var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);

        Task<AssetHandle<string>>Retain=Runtime.RetainAsync<string>(Fixture.Key).AsTask();
        await Started.Task;
        Task Dispose=Runtime.DisposeAsync().AsTask();
        Task JoinedDispose=Runtime.DisposeAsync().AsTask();
        Assert.False(JoinedDispose.IsCompleted);
        LoadGate.SetResult();
        ObjectDisposedException DisposedException=await Assert.ThrowsAsync<ObjectDisposedException>(async()=>await Retain);
        Assert.Equal(nameof(RuntimeAssetStore),DisposedException.ObjectName);
        await Dispose;
        await JoinedDispose;

        Assert.Equal(1,DecodeCompletionCount);
        Assert.Equal(1,ReleaseCount);
        Assert.Equal(0,Runtime.ResidentAssetCount);
        Assert.Equal(0,Runtime.ResidentArtifactCount);
    }

    [Fact]
    public async Task StoreDisposal_InvalidatesExistingHandleAndReleasesItsValueOnce(){
        var Fixture=await this.CreateManifestAsync("owned");
        int ReleaseCount=0;
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            static(Artifact,_)=>ValueTask.FromResult(Encoding.UTF8.GetString(Artifact.Payload.AsSpan())),
            _=>Interlocked.Increment(ref ReleaseCount));
        var Runtime=new RuntimeAssetStore(Fixture.Store,Fixture.Manifest,[Codec]);
        AssetHandle<string>Handle=await Runtime.RetainAsync<string>(Fixture.Key);
        Assert.Equal("owned",Handle.Value);

        await Runtime.DisposeAsync();

        Assert.Throws<ObjectDisposedException>(()=>Handle.Value);
        Assert.Equal(1,ReleaseCount);
        Handle.Dispose();
        Assert.Equal(1,ReleaseCount);
    }

    [Fact]
    public async Task ConcurrentManifestUpdates_CommitInInvocationOrder(){
        var First=await this.CreateManifestAsync("update-first");
        var Second=await this.CreateManifestAsync("update-second",First.Key,First.Store);
        var Third=await this.CreateManifestAsync("update-third",First.Key,First.Store);
        var Reader=new OrderedUpdateReader(First.Store,Second.Manifest.Entries[0].Address);
        await using var Runtime=new RuntimeAssetStore(
            Reader,First.Manifest,
            [new RuntimeAssetCodec<string>(
                static _=>true,
                static(Artifact,_)=>ValueTask.FromResult(Encoding.UTF8.GetString(Artifact.Payload.AsSpan())))]);

        Task<RuntimeAssetManifestUpdateResult>SecondUpdate=Runtime.TryUpdateManifestAsync(Second.Manifest).AsTask();
        await Reader.BlockedReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Task<RuntimeAssetManifestUpdateResult>ThirdUpdate=Runtime.TryUpdateManifestAsync(Third.Manifest).AsTask();
        await Task.Delay(50);
        Assert.False(Reader.WasRead(Third.Manifest.Entries[0].Address));

        Reader.ReleaseBlockedRead.SetResult();
        Assert.True((await SecondUpdate).Succeeded);
        Assert.True((await ThirdUpdate).Succeeded);
        Assert.Equal(Third.Manifest.Fingerprint,Runtime.ActiveManifestFingerprint);
    }

    [Fact]
    public async Task ArtifactReads_RespectTheConfiguredConcurrencyBound(){
        var Fixture=await this.CreateManifestWithDependenciesAsync(4);
        var Reader=new BoundedObservationReader(Fixture.Store,2);
        var Codec=new RuntimeAssetCodec<string>(
            static _=>true,
            static(Artifact,_)=>ValueTask.FromResult(Encoding.UTF8.GetString(Artifact.Payload.AsSpan())));
        await using var Runtime=new RuntimeAssetStore(Reader,Fixture.Manifest,[Codec],MaximumConcurrentLoads:2);

        Task<AssetHandle<string>>Retain=Runtime.RetainAsync<string>(Fixture.Key).AsTask();
        await Reader.BoundReached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2,Reader.MaximumObserved);
        Reader.Release.SetResult();
        using AssetHandle<string>Handle=await Retain;
        Assert.Equal("root",Handle.Value);
        Assert.Equal(2,Reader.MaximumObserved);
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private async Task<RuntimeFixture>CreateManifestAsync(
        string Payload,AssetKey?ExistingKey=null,ContentAddressedStore?ExistingStore=null){
        var Store=ExistingStore??new ContentAddressedStore(Path.Combine(this.Root,"cas-"+Guid.NewGuid().ToString("N")));
        AssetKey Key=ExistingKey??new AssetKey(new AssetId(StableId.FromName(Namespace,"asset")),null);
        byte[]PayloadBytes=Encoding.UTF8.GetBytes(Payload);
        var Artifact=new ContentArtifact(
            ContentArtifactKind.Mesh,
            new ArtifactProvenance("tests/1",Payload,Hash256.Compute(PayloadBytes),"fixture",Hash256.Compute([1])),
            null,PayloadBytes);
        PublishedContentArtifact Published=await Store.PublishAsync(Key,ContentArtifactCodec.Write(Artifact));
        var Index=new ContentArtifactIndex([Published]);
        ShippingContentManifest Manifest=await ShippingContentManifestBuilder.BuildAsync(Store,Index,[Published.Reference]);
        return new(Store,Manifest,Key);
    }

    private async Task<RuntimeFixture>CreateManifestWithDependenciesAsync(int DependencyCount){
        var Store=new ContentAddressedStore(Path.Combine(this.Root,"cas-dependencies-"+Guid.NewGuid().ToString("N")));
        var Dependencies=new PublishedContentArtifact[DependencyCount];
        for(var Index=0;Index<Dependencies.Length;Index++){
            string Payload="dependency-"+Index;
            byte[]PayloadBytes=Encoding.UTF8.GetBytes(Payload);
            Dependencies[Index]=await Store.PublishAsync(
                new AssetKey(new AssetId(StableId.FromName(Namespace,Payload)),null),
                ContentArtifactCodec.Write(new ContentArtifact(
                    ContentArtifactKind.Mesh,
                    new ArtifactProvenance("tests/1",Payload,Hash256.Compute(PayloadBytes),"fixture",Hash256.Compute([1])),
                    null,PayloadBytes)));
        }
        AssetKey RootKey=new(new AssetId(StableId.FromName(Namespace,"bounded-root")),null);
        byte[]RootPayload=Encoding.UTF8.GetBytes("root");
        PublishedContentArtifact RootArtifact=await Store.PublishAsync(
            RootKey,ContentArtifactCodec.Write(new ContentArtifact(
                ContentArtifactKind.Mesh,
                new ArtifactProvenance("tests/1","root",Hash256.Compute(RootPayload),"fixture",Hash256.Compute([1])),
                Dependencies.Select(static Dependency=>Dependency.Reference),RootPayload)));
        var Publications=Dependencies.Append(RootArtifact).ToArray();
        ShippingContentManifest Manifest=await ShippingContentManifestBuilder.BuildAsync(
            Store,new ContentArtifactIndex(Publications),[RootArtifact.Reference]);
        return new(Store,Manifest,RootKey);
    }

    private sealed record RuntimeFixture(ContentAddressedStore Store,ShippingContentManifest Manifest,AssetKey Key);
    private sealed class SwitchableArtifactReader(IContentArtifactReader Inner,Hash256?FailAddress):IContentArtifactReader {
        public Hash256?FailAddress{get;set;}=FailAddress;
        public ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default){
            if(this.FailAddress==Address){ return ValueTask.FromException<ReadOnlyMemory<byte>>(new InvalidDataException("simulated corruption")); }
            return Inner.ReadAsync(Address,CancellationToken);
        }
    }
    private sealed class CancelAfterReadReader(IContentArtifactReader Inner,Hash256 Address,CancellationTokenSource Cancel):IContentArtifactReader {
        public bool Cancelled{get;private set;}
        public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Read,CancellationToken CancellationToken=default){
            ReadOnlyMemory<byte>Bytes=await Inner.ReadAsync(Read,CancellationToken);
            if(Read==Address){
                Cancel.Cancel();
                this.Cancelled=true;
            }
            return Bytes;
        }
    }
    private sealed class BoundedObservationReader(IContentArtifactReader Inner,int ExpectedBound):IContentArtifactReader {
        private int Current;
        private int Maximum;
        public TaskCompletionSource BoundReached{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int MaximumObserved=>Volatile.Read(ref this.Maximum);
        public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default){
            int Active=Interlocked.Increment(ref this.Current);
            int Observed;
            do {
                Observed=Volatile.Read(ref this.Maximum);
                if(Active<=Observed){ break; }
            }while(Interlocked.CompareExchange(ref this.Maximum,Active,Observed)!=Observed);
            if(Active==ExpectedBound){ this.BoundReached.TrySetResult(); }
            try {
                await this.Release.Task.WaitAsync(CancellationToken);
                return await Inner.ReadAsync(Address,CancellationToken);
            } finally { Interlocked.Decrement(ref this.Current); }
        }
    }
    private sealed class OrderedUpdateReader(IContentArtifactReader Inner,Hash256 BlockAddress):IContentArtifactReader {
        private readonly object Gate=new();
        private readonly HashSet<Hash256>Reads=[];
        public TaskCompletionSource BlockedReadStarted{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseBlockedRead{get;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool WasRead(Hash256 Address){ lock(this.Gate){ return this.Reads.Contains(Address); } }
        public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default){
            lock(this.Gate){ this.Reads.Add(Address); }
            if(Address==BlockAddress){
                this.BlockedReadStarted.TrySetResult();
                await this.ReleaseBlockedRead.Task.WaitAsync(CancellationToken);
            }
            return await Inner.ReadAsync(Address,CancellationToken);
        }
    }
}
