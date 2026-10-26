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
    private readonly string IndexPath=Path.GetFullPath(IndexPath);
    private readonly SemaphoreSlim Gate=new(1,1);
    public async Task<ModelContentCompileResult>CompileAsync(IEnumerable<ModelCompileInput>Inputs,CancellationToken CancellationToken=default){
        await this.Gate.WaitAsync(CancellationToken);
        try {
            var Sources=Inputs.OrderBy(I=>I.SourceUri,StringComparer.Ordinal).ToArray();
            if(Sources.Select(I=>I.SourceUri).Distinct(StringComparer.Ordinal).Count()!=Sources.Length||Sources.Select(I=>I.AssetId).Distinct().Count()!=Sources.Length){ throw new InvalidDataException("IMP010: duplicate source URI or asset identity."); }
            var Previous=File.Exists(this.IndexPath)?ModelContentIndex.Read(await File.ReadAllBytesAsync(this.IndexPath,CancellationToken)):new ModelContentIndex([]);
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
            var Compilation=await this.Compiler.CompileAsync(Prepared.SelectMany(P=>P.Nodes),Prepared.Select(P=>P.Root),1,CancellationToken);
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
            foreach(var Entry in Previous.Entries.Where(E=>Failed.Contains(E.SourceUri))){
                var Input=Sources.Single(S=>S.SourceUri==Entry.SourceUri);
                if(Input.AssetId.ToString()==Entry.Asset){ Entries.Add(Entry with { LastKnownGood=true }); }
            }
            var Index=new ModelContentIndex(Entries);
            Directory.CreateDirectory(Path.GetDirectoryName(this.IndexPath)!);
            var Temporary=this.IndexPath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try {
                await File.WriteAllBytesAsync(Temporary,Index.Write(),CancellationToken);
                CancellationToken.ThrowIfCancellationRequested();
                File.Move(Temporary,this.IndexPath,true);
            } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            return new(Failed.Count==0,Index,Diagnostics.ToImmutable(),Compilation);
        } finally { this.Gate.Release(); }
    }
}
