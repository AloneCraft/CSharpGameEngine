using System.Collections.Concurrent;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.ContentCompiler;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;

namespace CSharpGameEngine.ContentCompiler.Tests;

public sealed class IncrementalContentCompilerTests:IDisposable {
    private static readonly StableId Namespace=StableId.Parse("85ad0454-4773-5b60-94bc-272190a751a2");
    private readonly string Root=Path.Combine(Path.GetTempPath(),"CgeContentCompilerTests",Guid.NewGuid().ToString("N"));
    private readonly ConcurrentDictionary<string,int>CompileCounts=[];

    [Fact]
    public async Task ColdWarmAndLeafChangeRebuildExactlyTheReachableDirtyClosure(){
        var Fixture=this.CreateFixture();
        var LeafV1=this.Node("leaf","v1");
        var RootV1=this.Node("root","v1",[LeafV1.Key]);
        var Unused=this.Node("unused","v1");

        var Cold=await Fixture.Compiler.CompileAsync([RootV1,Unused,LeafV1],[RootV1.Key]);
        var Warm=await Fixture.Compiler.CompileAsync([RootV1,Unused,LeafV1],[RootV1.Key]);
        var LeafV2=this.Node("leaf","v2");
        var Changed=await Fixture.Compiler.CompileAsync([RootV1,Unused,LeafV2],[RootV1.Key]);

        Assert.True(Cold.Succeeded,Cold.Error);
        Assert.Equal(new HashSet<AssetKey>([LeafV1.Key,RootV1.Key]),Cold.Rebuilt.ToHashSet());
        Assert.Empty(Cold.Reused);
        Assert.True(Warm.Succeeded,Warm.Error);
        Assert.Empty(Warm.Rebuilt);
        Assert.Equal(2,Warm.Reused.Length);
        Assert.True(Changed.Succeeded,Changed.Error);
        Assert.Equal(new HashSet<AssetKey>([LeafV1.Key,RootV1.Key]),Changed.Rebuilt.ToHashSet());
        Assert.Equal(2,this.CompileCounts["leaf"]);
        Assert.Equal(2,this.CompileCounts["root"]);
        Assert.False(this.CompileCounts.ContainsKey("unused"));
    }

    [Fact]
    public async Task IndependentNodesCompileWithinTheDeclaredConcurrencyBound(){
        var Fixture=this.CreateFixture();
        var Probe=new ConcurrencyProbe();
        var Nodes=Enumerable.Range(0,6).Select(Index=>this.Node("parallel-"+Index,"v1",Probe:Probe)).ToArray();

        var Result=await Fixture.Compiler.CompileAsync(Nodes,Nodes.Select(static Node=>Node.Key),2);

        Assert.True(Result.Succeeded,Result.Error);
        Assert.Equal(2,Probe.Maximum);
    }

    [Fact]
    public async Task FailedDependentKeepsLastKnownGoodStateAndOrphanIsRecoverablyQuarantined(){
        var Fixture=this.CreateFixture();
        var LeafV1=this.Node("leaf","v1");
        var RootV1=this.Node("root","v1",[LeafV1.Key]);
        var Initial=await Fixture.Compiler.CompileAsync([LeafV1,RootV1],[RootV1.Key]);
        Assert.True(Initial.Succeeded,Initial.Error);
        var StateBefore=await File.ReadAllBytesAsync(Fixture.StatePath);
        var LeafV2=this.Node("leaf","v2");
        var FailedRoot=this.Node("root","v1",[LeafV2.Key],Fail:true);

        var Failed=await Fixture.Compiler.CompileAsync([LeafV2,FailedRoot],[FailedRoot.Key]);

        Assert.Equal(ContentCompileStatus.CompilationFailed,Failed.Status);
        Assert.Equal(StateBefore,await File.ReadAllBytesAsync(Fixture.StatePath));
        var Preview=await Fixture.Compiler.CollectGarbageAsync([LeafV1,RootV1],[RootV1.Key]);
        var Orphan=Assert.Single(Preview.Candidates);
        var Quarantined=await Fixture.Compiler.CollectGarbageAsync(
            [LeafV1,RootV1],[RootV1.Key],ContentGarbageCollectionMode.Quarantine);
        Assert.Equal(Orphan,Assert.Single(Quarantined.Quarantined));
        await Assert.ThrowsAsync<FileNotFoundException>(async()=>await Fixture.Store.ReadAsync(Orphan));
        Fixture.Store.RestoreQuarantined(Orphan);
        _=await Fixture.Store.ReadAsync(Orphan);
    }

    [Fact]
    public void RestartRecoveryRemovesOnlyIncompleteTemporaryPublications(){
        var Fixture=this.CreateFixture();
        var StateTemporary=Fixture.StatePath+".tmp.interrupted";
        var CasTemporary=Path.Combine(this.Root,"cas","temporary","interrupted.tmp");
        File.WriteAllText(StateTemporary,"partial");
        File.WriteAllText(CasTemporary,"partial");

        var Removed=Fixture.Compiler.RecoverInterruptedPublications();

        Assert.Equal(2,Removed);
        Assert.False(File.Exists(StateTemporary));
        Assert.False(File.Exists(CasTemporary));
    }

    [Fact]
    public async Task InvalidGraphDoesNotCompileOrPublishState(){
        var Fixture=this.CreateFixture();
        var A=this.Node("cycle-a","v1");
        var B=this.Node("cycle-b","v1");
        var CyclicA=this.Node("cycle-a","v1",[B.Key]);
        var CyclicB=this.Node("cycle-b","v1",[A.Key]);

        var Cycle=await Fixture.Compiler.CompileAsync([CyclicA,CyclicB],[CyclicA.Key]);
        var Missing=this.Node("missing-root","v1",[this.Node("absent","v1").Key]);
        var MissingDependency=await Fixture.Compiler.CompileAsync([Missing],[Missing.Key]);

        Assert.Equal(ContentCompileStatus.InvalidGraph,Cycle.Status);
        Assert.Equal(ContentCompileStatus.InvalidGraph,MissingDependency.Status);
        Assert.Empty(this.CompileCounts);
        Assert.False(File.Exists(Fixture.StatePath));
    }

    [Fact]
    public async Task CorruptCompilerStateDoesNotCompileOrReplaceTheState(){
        var Fixture=this.CreateFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(Fixture.StatePath)!);
        var Corrupt=Encoding.UTF8.GetBytes("{not-json");
        await File.WriteAllBytesAsync(Fixture.StatePath,Corrupt);
        var RootNode=this.Node("root","v1");

        var Result=await Fixture.Compiler.CompileAsync([RootNode],[RootNode.Key]);

        Assert.Equal(ContentCompileStatus.StateCorrupt,Result.Status);
        Assert.Empty(this.CompileCounts);
        Assert.Equal(Corrupt,await File.ReadAllBytesAsync(Fixture.StatePath));
    }

    public void Dispose(){ if(Directory.Exists(this.Root)){ Directory.Delete(this.Root,true); } }

    private (ContentAddressedStore Store,IncrementalContentCompiler Compiler,string StatePath)CreateFixture(){
        var Store=new ContentAddressedStore(Path.Combine(this.Root,"cas"));
        var StatePath=Path.Combine(this.Root,"state","content-state.json");
        return(Store,new IncrementalContentCompiler(Store,StatePath),StatePath);
    }

    private ContentCompileNode Node(string Name,string Revision,IEnumerable<AssetKey>?Dependencies=null,bool Fail=false,ConcurrencyProbe?Probe=null){
        var Key=new AssetKey(new AssetId(StableId.FromName(Namespace,Name)),null);
        var SourceUri="asset://source/"+Name;
        var SourceHash=Hash256.Compute(Encoding.UTF8.GetBytes(Name+":"+Revision));
        var OptionsHash=Hash256.Compute(Encoding.UTF8.GetBytes("options:"+Name));
        return new ContentCompileNode(Key,SourceUri,SourceHash,"compiler/1","fixture",OptionsHash,Dependencies,
            (Context,CancellationToken)=>this.CompileAsync(Name,Revision,SourceUri,SourceHash,OptionsHash,Fail,Probe,Context,CancellationToken));
    }

    private async ValueTask<ReadOnlyMemory<byte>>CompileAsync(
        string Name,string Revision,string SourceUri,Hash256 SourceHash,Hash256 OptionsHash,bool Fail,ConcurrencyProbe?Probe,
        ContentCompileContext Context,CancellationToken CancellationToken){
        this.CompileCounts.AddOrUpdate(Name,1,static(_,Count)=>Count+1);
        if(Probe is not null){
            Probe.Enter();
            try { await Task.Delay(40,CancellationToken); }
            finally { Probe.Exit(); }
        }
        if(Fail){ throw new InvalidOperationException("injected compiler failure"); }
        var Artifact=new ContentArtifact(ContentArtifactKind.Scene,
            new ArtifactProvenance("compiler/1",SourceUri,SourceHash,"fixture",OptionsHash),
            Context.Dependencies.Values.Select(static Publication=>Publication.Reference),
            Encoding.UTF8.GetBytes(Name+":"+Revision));
        return ContentArtifactCodec.Write(Artifact);
    }

    private sealed class ConcurrencyProbe {
        private int Active;
        internal int Maximum;
        internal void Enter(){
            var Current=Interlocked.Increment(ref this.Active);
            int Existing;
            while(Current>(Existing=Volatile.Read(ref this.Maximum))&&Interlocked.CompareExchange(ref this.Maximum,Current,Existing)!=Existing){ }
        }
        internal void Exit()=>Interlocked.Decrement(ref this.Active);
    }
}
