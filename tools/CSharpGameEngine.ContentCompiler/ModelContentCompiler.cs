using System.Collections.Immutable;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.ContentCompiler;
public sealed record ModelCompileInput(AssetId AssetId,string SourceUri,Func<CancellationToken,Task<PreparedModelSource>>Prepare);
public sealed record ModelContentCompileResult(bool Succeeded,ModelContentIndex Index,ImmutableArray<ImportDiagnostic>Diagnostics,ContentCompileResult?Compilation);
/// <summary>変更のない増分コンパイラーを呼び出す前に、ソースの失敗を解決する。</summary>
public sealed class ModelContentCompiler(ContentAddressedStore Store,string StatePath,string IndexPath) {
    private readonly IncrementalContentCompiler Compiler=new(Store,StatePath);
    private readonly ContentAddressedStore ArtifactStore=Store;
    private readonly string IndexPath=Path.GetFullPath(IndexPath);
    private readonly SemaphoreSlim Gate=new(1,1);
    public async Task<ModelContentCompileResult>CompileAsync(IEnumerable<ModelCompileInput>Inputs,CancellationToken CancellationToken=default){
        await this.Gate.WaitAsync(CancellationToken);
        try {
            var Sources=Inputs.OrderBy(I=>I.SourceUri,StringComparer.Ordinal).ToArray();
            if(Sources.Select(I=>I.SourceUri).Distinct(StringComparer.Ordinal).Count()!=Sources.Length||Sources.Select(I=>I.AssetId).Distinct().Count()!=Sources.Length){ throw new InvalidDataException("IMP010: duplicate source URI or asset identity."); }
            var HadIndex=File.Exists(this.IndexPath);
            var Previous=HadIndex?ModelContentIndex.Read(await File.ReadAllBytesAsync(this.IndexPath,CancellationToken)):new ModelContentIndex([]);
            var Prepared=new List<PreparedModelSource>();
            var Failed=new HashSet<string>(StringComparer.Ordinal);
            var Diagnostics=ImmutableArray.CreateBuilder<ImportDiagnostic>();
            foreach(var Source in Sources){
                CancellationToken.ThrowIfCancellationRequested();
                try {
                    var Value=await Source.Prepare(CancellationToken);
                    if(Value.AssetId!=Source.AssetId||Value.SourceUri!=Source.SourceUri){ throw new InvalidDataException("IMP902: prepared source identity differs from its request."); }
                    Prepared.Add(Value);
                } catch(OperationCanceledException){ throw; }
                catch(Exception Error) when(Error is IOException or InvalidDataException or ArgumentException){
                    Failed.Add(Source.SourceUri);
                    Diagnostics.Add(new(ImportSeverity.Error,"IMP902","cge.content",ImportPhase.Write,Error.Message,new(Source.SourceUri)));
                }
            }
            var Compilation=Prepared.Count==0?new ContentCompileResult(ContentCompileStatus.Succeeded,[],[],new ContentArtifactIndex([]),string.Empty)
                :await this.Compiler.CompileAsync(Prepared.SelectMany(P=>P.Nodes),Prepared.Select(P=>P.Root),1,CancellationToken);
            if(!Compilation.Succeeded){
                Diagnostics.Add(new(ImportSeverity.Error,"IMP902","cge.content",ImportPhase.Write,Compilation.Error,new(string.Empty)));
                return new(false,Previous,Diagnostics.ToImmutable(),Compilation);
            }
            var Entries=new List<ModelContentEntry>();
            foreach(var Source in Prepared){
                foreach(var Artifact in Source.Artifacts){
                    Entries.Add(new(Source.SourceUri,Source.AssetId.ToString(),Artifact.Name,Artifact.Kind,new AssetUri(Artifact.Key).ToString(),Artifact.Reference.ContentHash.ToString(),Artifact.Address.ToString(),Artifact.Envelope.Length,false));
                }
            }
            foreach(var Group in Previous.Entries.Where(E=>Failed.Contains(E.SourceUri)).GroupBy(static Entry=>Entry.SourceUri)){
                var Input=Sources.Single(S=>S.SourceUri==Group.Key);
                var Retained=Group.Where(Entry=>Input.AssetId.ToString()==Entry.Asset).ToArray();
                if(Retained.Length==0){ continue; }
                try { await this.ValidateRetainedAsync(Retained,CancellationToken); }
                catch(Exception Error)when(Error is IOException or ArgumentException){
                    Diagnostics.Add(new(ImportSeverity.Error,"IMP902","cge.content",ImportPhase.Write,"Previous model artifacts cannot be retained: "+Error.Message,new(Group.Key)));
                    continue;
                }
                Entries.AddRange(Retained.Select(static Entry=>Entry with {LastKnownGood=true}));
            }
            var Index=new ModelContentIndex(Entries);
            Compilation=new(Compilation.Status,Compilation.Rebuilt,Compilation.Reused,new ContentArtifactIndex(Index.Entries.Select(Publication)),Compilation.Error);
            Directory.CreateDirectory(Path.GetDirectoryName(this.IndexPath)!);
            var Temporary=this.IndexPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                var Bytes=Index.Write();
                CancellationToken.ThrowIfCancellationRequested();
                if(!HadIndex||!Bytes.AsSpan().SequenceEqual(Previous.Write())){
                    await File.WriteAllBytesAsync(Temporary,Bytes,CancellationToken);
                    CancellationToken.ThrowIfCancellationRequested();File.Move(Temporary,this.IndexPath,true);
                }
            } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            return new(Failed.Count==0,Index,Diagnostics.ToImmutable(),Compilation);
        } finally { this.Gate.Release(); }
    }
    private static PublishedContentArtifact Publication(ModelContentEntry Entry){
        _=AssetUri.TryParse(Entry.Uri,out var Uri,out _);_=Hash256.TryParse(Entry.Hash,out var Hash);_=Hash256.TryParse(Entry.Address,out var Address);
        return new(new(Uri.Key,Hash),Address);
    }
    private async Task ValidateRetainedAsync(ModelContentEntry[]Entries,CancellationToken CancellationToken){
        var Artifacts=new List<ContentArtifact>();
        var References=Entries.Select(Publication).Select(static Publication=>Publication.Reference).ToHashSet();
        if(Entries.Count(static Entry=>Entry.Kind==ContentArtifactKind.Model)!=1){ throw new InvalidDataException("The previous source has no unique model root."); }
        foreach(var Entry in Entries){
            var Expected=Publication(Entry);
            var Bytes=await this.ArtifactStore.ReadAsync(Expected.Address,CancellationToken);
            if(Bytes.Length!=Entry.EnvelopeBytes||!ContentArtifactCodec.TryRead(Bytes.Span,out var Artifact,out _)||Artifact is null
                ||Artifact.Kind!=Entry.Kind||Artifact.ContentHash!=Expected.Reference.ContentHash||Artifact.Provenance.SourceUri!=Entry.SourceUri
                ||!Bytes.Span.SequenceEqual(ContentArtifactCodec.Write(Artifact))){ throw new InvalidDataException("The previous source has a missing, corrupt or inconsistent envelope."); }
            Artifacts.Add(Artifact);
        }
        if(Artifacts.Any(Artifact=>Artifact.Dependencies.Any(Dependency=>!References.Contains(Dependency)))){ throw new InvalidDataException("The previous source dependency closure is incomplete."); }
    }
}
