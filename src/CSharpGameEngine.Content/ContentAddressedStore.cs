using System.Collections.Immutable;
using CSharpGameEngine.Contracts;

namespace CSharpGameEngine.Content;

public readonly record struct PublishedContentArtifact(
    AssetArtifactReference Reference,
    Hash256 Address);

public enum ContentGarbageCollectionMode:byte { Preview,Quarantine }
public readonly record struct ContentGarbageCollectionResult(
    ImmutableArray<Hash256>Candidates,
    ImmutableArray<Hash256>Quarantined);

public interface IContentArtifactLocator {
    bool TryResolve(AssetArtifactReference Reference,out Hash256 Address);
}

/// <summary>コンパイラーまたはパッケージマニフェストが公開する、参照からCASアドレスへの不変スナップショット。</summary>
public sealed class ContentArtifactIndex:IContentArtifactLocator {
    private readonly ImmutableDictionary<AssetArtifactReference,Hash256>Entries;

    public ContentArtifactIndex(IEnumerable<PublishedContentArtifact>Artifacts){
        ArgumentNullException.ThrowIfNull(Artifacts);
        var Builder=ImmutableDictionary.CreateBuilder<AssetArtifactReference,Hash256>();
        foreach(var Artifact in Artifacts){
            if(!Artifact.Reference.IsValid||!Artifact.Address.IsValid){
                throw new ArgumentException("Artifact index entries require valid references and addresses.",nameof(Artifacts));
            }
            if(Builder.TryGetValue(Artifact.Reference,out var Existing)){
                if(Existing!=Artifact.Address){
                    throw new ArgumentException($"Artifact reference maps to more than one canonical envelope: {Artifact.Reference.Key}.",nameof(Artifacts));
                }
                continue;
            }
            Builder.Add(Artifact.Reference,Artifact.Address);
        }
        this.Entries=Builder.ToImmutable();
    }

    public int Count=>this.Entries.Count;
    public bool TryResolve(AssetArtifactReference Reference,out Hash256 Address)=>this.Entries.TryGetValue(Reference,out Address);
}

/// <summary>検証済みの正規エンベロープを保存するファイルシステムCAS。公開には同一ボリューム内のアトミックな名前変更を使う。</summary>
public interface IContentArtifactReader {
    ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default);
}

public sealed class ContentAddressedStore:IContentArtifactReader {
    private const int DefaultMaximumArtifactBytes=512*1024*1024;
    private readonly string ObjectsDirectory;
    private readonly string TemporaryDirectory;
    private readonly string QuarantineDirectory;
    private readonly int MaximumArtifactBytes;

    public ContentAddressedStore(string RootDirectory,int MaximumArtifactBytes=DefaultMaximumArtifactBytes){
        if(string.IsNullOrWhiteSpace(RootDirectory)){ throw new ArgumentException("A CAS root directory is required.",nameof(RootDirectory)); }
        if(MaximumArtifactBytes<=0){ throw new ArgumentOutOfRangeException(nameof(MaximumArtifactBytes)); }
        var Root=Path.GetFullPath(RootDirectory);
        this.ObjectsDirectory=Path.Combine(Root,"objects");
        this.TemporaryDirectory=Path.Combine(Root,"temporary");
        this.QuarantineDirectory=Path.Combine(Root,"quarantine");
        this.MaximumArtifactBytes=MaximumArtifactBytes;
        Directory.CreateDirectory(this.ObjectsDirectory);
        Directory.CreateDirectory(this.TemporaryDirectory);
        Directory.CreateDirectory(this.QuarantineDirectory);
    }

    public async ValueTask<PublishedContentArtifact>PublishAsync(
        AssetKey Key,ReadOnlyMemory<byte>CanonicalEnvelope,CancellationToken CancellationToken=default){
        if(!Key.IsValid){ throw new ArgumentException("A valid AssetKey is required.",nameof(Key)); }
        if(CanonicalEnvelope.Length==0||CanonicalEnvelope.Length>this.MaximumArtifactBytes){
            throw new InvalidDataException("Artifact envelope length exceeds the configured CAS bounds.");
        }
        if(!ContentArtifactCodec.TryRead(CanonicalEnvelope.Span,out var Artifact,out var Error)||Artifact is null){
            throw new InvalidDataException($"Artifact envelope is invalid: {Error.Field}: {Error.Message}");
        }
        var Reencoded=ContentArtifactCodec.Write(Artifact);
        if(!CanonicalEnvelope.Span.SequenceEqual(Reencoded)){
            throw new InvalidDataException("Artifact envelope is valid but not in canonical encoded form.");
        }
        var Address=ContentArtifactCodec.ComputeAddress(Reencoded);
        var Destination=this.GetObjectPath(Address);
        if(File.Exists(Destination)){
            _=await this.ReadAsync(Address,CancellationToken).ConfigureAwait(false);
            return new(new AssetArtifactReference(Key,Artifact.ContentHash),Address);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        var Temporary=Path.Combine(this.TemporaryDirectory,Guid.NewGuid().ToString("N")+".tmp");
        try {
            await using(var Stream=new FileStream(Temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,64*1024,FileOptions.Asynchronous|FileOptions.WriteThrough)){
                await Stream.WriteAsync(Reencoded,CancellationToken).ConfigureAwait(false);
                await Stream.FlushAsync(CancellationToken).ConfigureAwait(false);
            }
            try { File.Move(Temporary,Destination,false); }
            catch(IOException) when(File.Exists(Destination)){
                _=await this.ReadAsync(Address,CancellationToken).ConfigureAwait(false);
            }
            return new(new AssetArtifactReference(Key,Artifact.ContentHash),Address);
        } finally {
            if(File.Exists(Temporary)){ File.Delete(Temporary); }
        }
    }

    public async ValueTask<ReadOnlyMemory<byte>>ReadAsync(Hash256 Address,CancellationToken CancellationToken=default){
        if(!Address.IsValid){ throw new ArgumentException("A valid artifact address is required.",nameof(Address)); }
        var PathValue=this.GetObjectPath(Address);
        var Information=new FileInfo(PathValue);
        if(!Information.Exists){ throw new FileNotFoundException("CAS artifact does not exist.",PathValue); }
        if(Information.Length<=0||Information.Length>this.MaximumArtifactBytes){
            throw new InvalidDataException("CAS artifact length exceeds the configured bounds.");
        }
        var Bytes=await File.ReadAllBytesAsync(PathValue,CancellationToken).ConfigureAwait(false);
        if(Bytes.Length!=Information.Length||ContentArtifactCodec.ComputeAddress(Bytes)!=Address){
            throw new InvalidDataException("CAS artifact address does not match its bytes.");
        }
        if(!ContentArtifactCodec.TryRead(Bytes,out _,out var Error)){
            throw new InvalidDataException($"CAS artifact envelope is invalid: {Error.Field}: {Error.Message}");
        }
        return Bytes;
    }

    public int RecoverInterruptedPublications(){
        var Removed=0;
        foreach(var PathValue in Directory.EnumerateFiles(this.TemporaryDirectory,"*.tmp",SearchOption.TopDirectoryOnly)){
            File.Delete(PathValue);
            Removed++;
        }
        return Removed;
    }

    public ContentGarbageCollectionResult CollectGarbage(
        IEnumerable<Hash256>Reachable,ContentGarbageCollectionMode Mode=ContentGarbageCollectionMode.Preview){
        ArgumentNullException.ThrowIfNull(Reachable);
        var RootSet=new HashSet<Hash256>();
        foreach(var Address in Reachable){
            if(!Address.IsValid){ throw new ArgumentException("Reachable CAS addresses must be valid.",nameof(Reachable)); }
            RootSet.Add(Address);
        }
        foreach(var Address in RootSet){
            var RootPath=this.GetObjectPath(Address);
            if(!File.Exists(RootPath)){ throw new FileNotFoundException("Reachable CAS root does not exist.",RootPath); }
        }
        var Candidates=ImmutableArray.CreateBuilder<Hash256>();
        foreach(var PathValue in Directory.EnumerateFiles(this.ObjectsDirectory,"*.cgea",SearchOption.AllDirectories)){
            var Name=Path.GetFileNameWithoutExtension(PathValue);
            if(!Hash256.TryParse(Name,out var Address)||!Address.IsValid||RootSet.Contains(Address)){ continue; }
            Candidates.Add(Address);
        }
        Candidates.Sort();
        var Quarantined=ImmutableArray.CreateBuilder<Hash256>();
        if(Mode==ContentGarbageCollectionMode.Quarantine){
            foreach(var Address in Candidates){
                var Source=this.GetObjectPath(Address);
                var Destination=this.GetQuarantinePath(Address);
                if(File.Exists(Destination)){ throw new IOException($"CAS quarantine already contains address {Address}."); }
                File.Move(Source,Destination,false);
                Quarantined.Add(Address);
            }
        }
        return new(Candidates.ToImmutable(),Quarantined.ToImmutable());
    }

    public void RestoreQuarantined(Hash256 Address){
        if(!Address.IsValid){ throw new ArgumentException("A valid artifact address is required.",nameof(Address)); }
        var Source=this.GetQuarantinePath(Address);
        if(!File.Exists(Source)){ throw new FileNotFoundException("Quarantined CAS artifact does not exist.",Source); }
        var Destination=this.GetObjectPath(Address);
        Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
        if(File.Exists(Destination)){ throw new IOException($"Active CAS already contains address {Address}."); }
        File.Move(Source,Destination,false);
    }

    private string GetObjectPath(Hash256 Address){
        var Text=Address.ToString();
        return Path.Combine(this.ObjectsDirectory,Text.Substring(0,2),Text+".cgea");
    }
    private string GetQuarantinePath(Hash256 Address)=>Path.Combine(this.QuarantineDirectory,Address+".cgea");
}
