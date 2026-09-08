using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.ContentCompiler;

public sealed class ContentCompileNode {
    public ContentCompileNode(
        AssetKey Key,string SourceUri,Hash256 SourceHash,string ToolVersion,
        string ImporterIdentity,Hash256 OptionsHash,IEnumerable<AssetKey>?Dependencies,
        Func<ContentCompileContext,CancellationToken,ValueTask<ReadOnlyMemory<byte>>>CompileAsync){
        if(!Key.IsValid){ throw new ArgumentException("A valid AssetKey is required.",nameof(Key)); }
        if(string.IsNullOrWhiteSpace(SourceUri)){ throw new ArgumentException("A source URI is required.",nameof(SourceUri)); }
        if(!SourceHash.IsValid){ throw new ArgumentException("A valid source hash is required.",nameof(SourceHash)); }
        if(string.IsNullOrWhiteSpace(ToolVersion)){ throw new ArgumentException("A tool version is required.",nameof(ToolVersion)); }
        if(string.IsNullOrWhiteSpace(ImporterIdentity)){ throw new ArgumentException("An importer identity is required.",nameof(ImporterIdentity)); }
        if(!OptionsHash.IsValid){ throw new ArgumentException("A valid options hash is required.",nameof(OptionsHash)); }
        this.Key=Key;
        this.SourceUri=SourceUri;
        this.SourceHash=SourceHash;
        this.ToolVersion=ToolVersion;
        this.ImporterIdentity=ImporterIdentity;
        this.OptionsHash=OptionsHash;
        var Builder=ImmutableArray.CreateBuilder<AssetKey>();
        if(Dependencies is not null){ Builder.AddRange(Dependencies); }
        Builder.Sort();
        for(var Index=0;Index<Builder.Count;Index++){
            if(!Builder[Index].IsValid){ throw new ArgumentException("Dependency keys must be valid.",nameof(Dependencies)); }
            if(Index!=0&&Builder[Index-1]==Builder[Index]){ throw new ArgumentException("Dependency keys must be unique.",nameof(Dependencies)); }
        }
        this.Dependencies=Builder.ToImmutable();
        this.CompileAsync=CompileAsync??throw new ArgumentNullException(nameof(CompileAsync));
    }
    public AssetKey Key{get;}
    public string SourceUri{get;}
    public Hash256 SourceHash{get;}
    public string ToolVersion{get;}
    public string ImporterIdentity{get;}
    public Hash256 OptionsHash{get;}
    public ImmutableArray<AssetKey>Dependencies{get;}
    internal Func<ContentCompileContext,CancellationToken,ValueTask<ReadOnlyMemory<byte>>>CompileAsync{get;}
}

public sealed class ContentCompileContext {
    internal ContentCompileContext(ContentCompileNode Node,IReadOnlyDictionary<AssetKey,PublishedContentArtifact>Dependencies){
        this.Node=Node;
        this.Dependencies=Dependencies;
    }
    public ContentCompileNode Node{get;}
    public IReadOnlyDictionary<AssetKey,PublishedContentArtifact>Dependencies{get;}
}

public enum ContentCompileStatus:byte { Succeeded,InvalidGraph,Cancelled,CompilationFailed,StateCorrupt }
public sealed class ContentCompileResult {
    internal ContentCompileResult(ContentCompileStatus Status,ImmutableArray<AssetKey>Rebuilt,ImmutableArray<AssetKey>Reused,ContentArtifactIndex?Index,string Error){
        this.Status=Status;this.Rebuilt=Rebuilt;this.Reused=Reused;this.Index=Index;this.Error=Error;
    }
    public ContentCompileStatus Status{get;}
    public ImmutableArray<AssetKey>Rebuilt{get;}
    public ImmutableArray<AssetKey>Reused{get;}
    public ContentArtifactIndex?Index{get;}
    public string Error{get;}
    public bool Succeeded=>this.Status==ContentCompileStatus.Succeeded;
}

/// <summary>Dependency-addressed incremental compiler with atomic last-known-good state publication.</summary>
public sealed class IncrementalContentCompiler {
    private const int StateSchemaVersion=1;
    private readonly SemaphoreSlim OperationGate=new(1,1);
    private readonly ContentAddressedStore Store;
    private readonly string StatePath;

    public IncrementalContentCompiler(ContentAddressedStore Store,string StatePath){
        this.Store=Store??throw new ArgumentNullException(nameof(Store));
        if(string.IsNullOrWhiteSpace(StatePath)){ throw new ArgumentException("A compiler state path is required.",nameof(StatePath)); }
        this.StatePath=Path.GetFullPath(StatePath);
        Directory.CreateDirectory(Path.GetDirectoryName(this.StatePath)!);
    }

    public async ValueTask<ContentCompileResult>CompileAsync(
        IEnumerable<ContentCompileNode>Nodes,IEnumerable<AssetKey>Roots,
        int MaximumConcurrency=4,CancellationToken CancellationToken=default){
        if(MaximumConcurrency<=0){ return Fail(ContentCompileStatus.InvalidGraph,"Maximum concurrency must be positive."); }
        try { await this.OperationGate.WaitAsync(CancellationToken).ConfigureAwait(false); }
        catch(OperationCanceledException){ return Fail(ContentCompileStatus.Cancelled,"Compilation was cancelled before acquiring the operation lease."); }
        try {
            this.RecoverInterruptedPublications();
            ContentCompileGraph Graph;
            HashSet<AssetKey>Closure;
            try { Graph=ContentCompileGraph.Create(Nodes);Closure=Graph.GetClosure(Roots); }
            catch(ArgumentException Exception){ return Fail(ContentCompileStatus.InvalidGraph,Exception.Message); }
            Dictionary<AssetKey,StateEntry>State;
            try { State=await this.ReadStateAsync(CancellationToken).ConfigureAwait(false); }
            catch(OperationCanceledException){ return Fail(ContentCompileStatus.Cancelled,"Compilation was cancelled while reading state."); }
            catch(Exception Exception) when(Exception is IOException or InvalidDataException or JsonException){ return Fail(ContentCompileStatus.StateCorrupt,Exception.Message); }
            var Candidate=new Dictionary<AssetKey,StateEntry>(State);
            var Completed=new Dictionary<AssetKey,PublishedContentArtifact>();
            var Pending=new HashSet<AssetKey>(Closure);
            var Rebuilt=ImmutableArray.CreateBuilder<AssetKey>();
            var Reused=ImmutableArray.CreateBuilder<AssetKey>();
            while(Pending.Count!=0){
                var Wave=Graph.TopologicalOrder.Where(Key=>Pending.Contains(Key)&&Graph[Key].Dependencies.All(Dependency=>!Pending.Contains(Dependency))).ToArray();
                if(Wave.Length==0){ return Fail(ContentCompileStatus.InvalidGraph,"Reachable content graph cannot make progress."); }
                using var Concurrency=new SemaphoreSlim(Math.Min(Wave.Length,MaximumConcurrency));
                var Tasks=new Task<NodeCompileResult>[Wave.Length];
                for(var Index=0;Index<Wave.Length;Index++){
                    var Node=Graph[Wave[Index]];
                    Tasks[Index]=ProcessWithGateAsync(Node);
                }
                var Results=await Task.WhenAll(Tasks).ConfigureAwait(false);
                var Failure=Results.FirstOrDefault(static Result=>!Result.Succeeded);
                if(Failure is not null){
                    return Fail(Failure.Cancelled?ContentCompileStatus.Cancelled:ContentCompileStatus.CompilationFailed,Failure.Error);
                }
                foreach(var Result in Results){
                    Completed.Add(Result.Node.Key,Result.Publication);
                    Candidate[Result.Node.Key]=new StateEntry(Result.Node.Key,Result.Fingerprint,Result.Publication.Reference,Result.Publication.Address,Result.Node.Dependencies);
                    (Result.Reused?Reused:Rebuilt).Add(Result.Node.Key);
                    Pending.Remove(Result.Node.Key);
                }

                async Task<NodeCompileResult>ProcessWithGateAsync(ContentCompileNode Node){
                    try {
                        await Concurrency.WaitAsync(CancellationToken).ConfigureAwait(false);
                        try { return await this.ProcessNodeAsync(Node,Completed,State,CancellationToken).ConfigureAwait(false); }
                        finally { Concurrency.Release(); }
                    }catch(OperationCanceledException){ return NodeCompileResult.Cancel(Node); }
                    catch(Exception Exception){ return NodeCompileResult.Fail(Node,Exception.Message); }
                }
            }
            try { await this.WriteStateAsync(Candidate,CancellationToken).ConfigureAwait(false); }
            catch(OperationCanceledException){ return Fail(ContentCompileStatus.Cancelled,"Compilation was cancelled before state publication."); }
            catch(Exception Exception) when(Exception is IOException or UnauthorizedAccessException){ return Fail(ContentCompileStatus.CompilationFailed,Exception.Message); }
            var Publications=Graph.TopologicalOrder.Where(Closure.Contains).Select(Key=>Completed[Key]).ToArray();
            return new(ContentCompileStatus.Succeeded,Rebuilt.ToImmutable(),Reused.ToImmutable(),new ContentArtifactIndex(Publications),string.Empty);
        } finally { this.OperationGate.Release(); }
    }

    public async ValueTask<ContentGarbageCollectionResult>CollectGarbageAsync(
        IEnumerable<ContentCompileNode>Nodes,IEnumerable<AssetKey>Roots,
        ContentGarbageCollectionMode Mode=ContentGarbageCollectionMode.Preview,
        CancellationToken CancellationToken=default){
        await this.OperationGate.WaitAsync(CancellationToken).ConfigureAwait(false);
        try {
            var Graph=ContentCompileGraph.Create(Nodes);
            var Closure=Graph.GetClosure(Roots);
            var State=await this.ReadStateAsync(CancellationToken).ConfigureAwait(false);
            var Reachable=new List<Hash256>();
            foreach(var Key in Closure){
                if(!State.TryGetValue(Key,out var Entry)){ throw new InvalidOperationException($"Reachable content node has no published state: {Key}."); }
                Reachable.Add(Entry.Address);
            }
            if(Mode==ContentGarbageCollectionMode.Quarantine){
                var Retained=State.Where(Pair=>Closure.Contains(Pair.Key)).ToDictionary(static Pair=>Pair.Key,static Pair=>Pair.Value);
                await this.WriteStateAsync(Retained,CancellationToken).ConfigureAwait(false);
            }
            return this.Store.CollectGarbage(Reachable,Mode);
        } finally { this.OperationGate.Release(); }
    }

    public int RecoverInterruptedPublications(){
        var Removed=this.Store.RecoverInterruptedPublications();
        var DirectoryPath=Path.GetDirectoryName(this.StatePath)!;
        var Prefix=Path.GetFileName(this.StatePath)+".tmp.";
        foreach(var PathValue in Directory.EnumerateFiles(DirectoryPath,Prefix+"*",SearchOption.TopDirectoryOnly)){
            File.Delete(PathValue);
            Removed++;
        }
        return Removed;
    }

    private async ValueTask<NodeCompileResult>ProcessNodeAsync(
        ContentCompileNode Node,IReadOnlyDictionary<AssetKey,PublishedContentArtifact>Completed,
        IReadOnlyDictionary<AssetKey,StateEntry>State,CancellationToken CancellationToken){
        var Dependencies=Node.Dependencies.ToDictionary(Key=>Key,Key=>Completed[Key]);
        var Fingerprint=ComputeFingerprint(Node,Dependencies);
        if(State.TryGetValue(Node.Key,out var Existing)&&Existing.Fingerprint==Fingerprint){
            try {
                var Bytes=await this.Store.ReadAsync(Existing.Address,CancellationToken).ConfigureAwait(false);
                if(TryValidateEnvelope(Node,Dependencies,Bytes.Span,out var Artifact,out _)
                    &&Artifact!.ContentHash==Existing.Reference.ContentHash){
                    return NodeCompileResult.Success(Node,Fingerprint,new PublishedContentArtifact(Existing.Reference,Existing.Address),true);
                }
            }catch(Exception Exception) when(Exception is IOException or InvalidDataException){ }
        }
        var Context=new ContentCompileContext(Node,Dependencies);
        var Envelope=await Node.CompileAsync(Context,CancellationToken).ConfigureAwait(false);
        if(!TryValidateEnvelope(Node,Dependencies,Envelope.Span,out _,out var Error)){
            return NodeCompileResult.Fail(Node,Error);
        }
        var Publication=await this.Store.PublishAsync(Node.Key,Envelope,CancellationToken).ConfigureAwait(false);
        return NodeCompileResult.Success(Node,Fingerprint,Publication,false);
    }

    private static bool TryValidateEnvelope(ContentCompileNode Node,IReadOnlyDictionary<AssetKey,PublishedContentArtifact>Dependencies,ReadOnlySpan<byte>Bytes,out ContentArtifact?Artifact,out string Error){
        Artifact=null;
        Error=string.Empty;
        if(!ContentArtifactCodec.TryRead(Bytes,out Artifact,out var ArtifactError)||Artifact is null){ Error=$"{ArtifactError.Field}: {ArtifactError.Message}";return false; }
        var Provenance=Artifact.Provenance;
        if(Provenance.SourceUri!=Node.SourceUri||Provenance.SourceHash!=Node.SourceHash||Provenance.ToolVersion!=Node.ToolVersion
            ||Provenance.ImporterIdentity!=Node.ImporterIdentity||Provenance.OptionsHash!=Node.OptionsHash){
            Error="Artifact provenance does not match its compile node.";
            return false;
        }
        var Expected=Dependencies.Values.Select(static Publication=>Publication.Reference).OrderBy(static Reference=>Reference.Key).ToArray();
        if(!Artifact.Dependencies.SequenceEqual(Expected)){
            Error="Artifact dependency closure does not match the compiled DAG node.";
            return false;
        }
        return true;
    }

    private static Hash256 ComputeFingerprint(ContentCompileNode Node,IReadOnlyDictionary<AssetKey,PublishedContentArtifact>Dependencies){
        var Builder=new StringBuilder();
        Builder.Append(Node.Key).Append((char)10).Append(Node.SourceUri).Append((char)10).Append(Node.SourceHash).Append((char)10)
            .Append(Node.ToolVersion).Append((char)10).Append(Node.ImporterIdentity).Append((char)10).Append(Node.OptionsHash).Append((char)10);
        foreach(var Dependency in Dependencies.OrderBy(static Pair=>Pair.Key)){
            Builder.Append(Dependency.Key).Append('=').Append(Dependency.Value.Address).Append((char)10);
        }
        return Hash256.Compute(Encoding.UTF8.GetBytes(Builder.ToString()));
    }

    private async ValueTask<Dictionary<AssetKey,StateEntry>>ReadStateAsync(CancellationToken CancellationToken){
        if(!File.Exists(this.StatePath)){ return []; }
        var Bytes=await File.ReadAllBytesAsync(this.StatePath,CancellationToken).ConfigureAwait(false);
        var Document=JsonSerializer.Deserialize<StateDocument>(Bytes)??throw new InvalidDataException("Compiler state is empty.");
        if(Document.SchemaVersion!=StateSchemaVersion||Document.Entries is null){ throw new InvalidDataException("Compiler state schema is unsupported."); }
        var Result=new Dictionary<AssetKey,StateEntry>();
        foreach(var Entry in Document.Entries){
            try {
                var Key=AssetUri.Parse("asset://"+Entry.Key).Key;
                var Reference=new AssetArtifactReference(Key,Hash256.Parse(Entry.ContentHash));
                var Dependencies=(Entry.Dependencies??[]).Select(Value=>AssetUri.Parse("asset://"+Value).Key).ToImmutableArray();
                var State=new StateEntry(Key,Hash256.Parse(Entry.Fingerprint),Reference,Hash256.Parse(Entry.Address),Dependencies);
                if(!Result.TryAdd(Key,State)){ throw new InvalidDataException($"Compiler state contains duplicate key {Key}."); }
            }catch(Exception Exception) when(Exception is ArgumentException or FormatException){ throw new InvalidDataException("Compiler state contains an invalid identity or hash.",Exception); }
        }
        return Result;
    }

    private async ValueTask WriteStateAsync(IReadOnlyDictionary<AssetKey,StateEntry>State,CancellationToken CancellationToken){
        var Document=new StateDocument {
            SchemaVersion=StateSchemaVersion,
            Entries=State.Values.OrderBy(static Entry=>Entry.Key).Select(static Entry=>new StateEntryDocument {
                Key=Entry.Key.ToString(),Fingerprint=Entry.Fingerprint.ToString(),ContentHash=Entry.Reference.ContentHash.ToString(),Address=Entry.Address.ToString(),
                Dependencies=Entry.Dependencies.Select(static Key=>Key.ToString()).ToArray(),
            }).ToArray(),
        };
        var Bytes=JsonSerializer.SerializeToUtf8Bytes(Document);
        var Temporary=this.StatePath+".tmp."+Guid.NewGuid().ToString("N");
        try {
            await using(var Stream=new FileStream(Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough)){
                await Stream.WriteAsync(Bytes,CancellationToken).ConfigureAwait(false);
                await Stream.FlushAsync(CancellationToken).ConfigureAwait(false);
            }
            File.Move(Temporary,this.StatePath,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }

    private static ContentCompileResult Fail(ContentCompileStatus Status,string Error)=>new(Status,[],[],null,Error);

    private sealed record StateEntry(AssetKey Key,Hash256 Fingerprint,AssetArtifactReference Reference,Hash256 Address,ImmutableArray<AssetKey>Dependencies);
    private sealed class StateDocument { public int SchemaVersion{get;set;} public StateEntryDocument[]?Entries{get;set;} }
    private sealed class StateEntryDocument { public string Key{get;set;}=string.Empty;public string Fingerprint{get;set;}=string.Empty;public string ContentHash{get;set;}=string.Empty;public string Address{get;set;}=string.Empty;public string[]?Dependencies{get;set;} }
    private sealed class NodeCompileResult {
        private NodeCompileResult(ContentCompileNode Node,Hash256 Fingerprint,PublishedContentArtifact Publication,bool Reused,bool Cancelled,string Error){ this.Node=Node;this.Fingerprint=Fingerprint;this.Publication=Publication;this.Reused=Reused;this.Cancelled=Cancelled;this.Error=Error; }
        internal ContentCompileNode Node{get;}
        internal Hash256 Fingerprint{get;}
        internal PublishedContentArtifact Publication{get;}
        internal bool Reused{get;}
        internal bool Cancelled{get;}
        internal string Error{get;}
        internal bool Succeeded=>this.Error.Length==0;
        internal static NodeCompileResult Success(ContentCompileNode Node,Hash256 Fingerprint,PublishedContentArtifact Publication,bool Reused)=>new(Node,Fingerprint,Publication,Reused,false,string.Empty);
        internal static NodeCompileResult Fail(ContentCompileNode Node,string Error)=>new(Node,default,default,false,false,Error);
        internal static NodeCompileResult Cancel(ContentCompileNode Node)=>new(Node,default,default,false,true,"Compilation was cancelled.");
    }
}

internal sealed class ContentCompileGraph {
    private readonly Dictionary<AssetKey,ContentCompileNode>Nodes;
    private ContentCompileGraph(Dictionary<AssetKey,ContentCompileNode>Nodes,ImmutableArray<AssetKey>TopologicalOrder){ this.Nodes=Nodes;this.TopologicalOrder=TopologicalOrder; }
    internal ImmutableArray<AssetKey>TopologicalOrder{get;}
    internal ContentCompileNode this[AssetKey Key]=>this.Nodes[Key];
    internal static ContentCompileGraph Create(IEnumerable<ContentCompileNode>Nodes){
        ArgumentNullException.ThrowIfNull(Nodes);
        var ByKey=new Dictionary<AssetKey,ContentCompileNode>();
        foreach(var Node in Nodes){
            if(Node is null){ throw new ArgumentException("Compile graph cannot contain null nodes.",nameof(Nodes)); }
            if(!ByKey.TryAdd(Node.Key,Node)){ throw new ArgumentException($"Compile graph contains duplicate key {Node.Key}.",nameof(Nodes)); }
        }
        var InDegree=ByKey.Keys.ToDictionary(static Key=>Key,static _=>0);
        var Dependents=ByKey.Keys.ToDictionary(static Key=>Key,static _=>new List<AssetKey>());
        foreach(var Node in ByKey.Values){
            foreach(var Dependency in Node.Dependencies){
                if(!ByKey.ContainsKey(Dependency)){ throw new ArgumentException($"Compile node {Node.Key} has missing dependency {Dependency}.",nameof(Nodes)); }
                if(Dependency==Node.Key){ throw new ArgumentException($"Compile node {Node.Key} depends on itself.",nameof(Nodes)); }
                InDegree[Node.Key]++;
                Dependents[Dependency].Add(Node.Key);
            }
        }
        var Ready=new SortedSet<AssetKey>(InDegree.Where(static Pair=>Pair.Value==0).Select(static Pair=>Pair.Key));
        var Order=ImmutableArray.CreateBuilder<AssetKey>(ByKey.Count);
        while(Ready.Count!=0){
            var Key=Ready.Min;
            Ready.Remove(Key);
            Order.Add(Key);
            foreach(var Dependent in Dependents[Key]){ if(--InDegree[Dependent]==0){ Ready.Add(Dependent); } }
        }
        if(Order.Count!=ByKey.Count){ throw new ArgumentException("Compile graph contains a dependency cycle.",nameof(Nodes)); }
        return new ContentCompileGraph(ByKey,Order.MoveToImmutable());
    }
    internal HashSet<AssetKey>GetClosure(IEnumerable<AssetKey>Roots){
        ArgumentNullException.ThrowIfNull(Roots);
        var Closure=new HashSet<AssetKey>();
        var Pending=new Stack<AssetKey>();
        foreach(var Root in Roots){
            if(!this.Nodes.ContainsKey(Root)){ throw new ArgumentException($"Compile root is absent from graph: {Root}.",nameof(Roots)); }
            Pending.Push(Root);
        }
        while(Pending.Count!=0){
            var Key=Pending.Pop();
            if(!Closure.Add(Key)){ continue; }
            foreach(var Dependency in this.Nodes[Key].Dependencies){ Pending.Push(Dependency); }
        }
        if(Closure.Count==0){ throw new ArgumentException("At least one compile root is required.",nameof(Roots)); }
        return Closure;
    }
}
