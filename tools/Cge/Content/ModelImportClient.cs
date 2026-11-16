using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler;
public sealed record ModelSourceImportResult(InterchangeDocument?Document,ImmutableArray<ImportDiagnostic>Diagnostics,bool Reused,Hash256 SourceHash);
internal sealed record ImportCacheEntry(string Importer,string ModuleHash,string OptionsHash,string Fingerprint,string IrHash,string[]ReadSet,ImportDiagnostic[]Diagnostics);
public sealed class ModelImportClient(string CookToolPath,string CacheDirectory) {
    private readonly ImportProcessRunner Runner=new(CookToolPath);
    private readonly string CacheDirectory=Path.GetFullPath(CacheDirectory);
    private static readonly Hash256 EngineHash=Hash256.Compute(File.ReadAllBytes(typeof(ModelImportPipeline).Assembly.Location));
    public async Task<ModelSourceImportResult>ImportAsync(ImporterRegistration Registration,AssetId AssetId,string ProjectDirectory,string SourcePath,IReadOnlyDictionary<string,string>Options,bool Verify=false,CancellationToken CancellationToken=default){
        CancellationToken.ThrowIfCancellationRequested();
        if(!AssetId.IsValid){ throw new ArgumentException("A valid AssetId is required.",nameof(AssetId)); }
        if(Hash256.Compute(await File.ReadAllBytesAsync(Registration.ModulePath,CancellationToken))!=Registration.ModuleHash){ throw new InvalidDataException("IMP901: import module changed after registry discovery."); }
        var Root=Path.TrimEndingDirectorySeparator(Path.GetFullPath(ProjectDirectory));
        var Source=Resolve(Root,SourcePath);
        var Uri=Path.GetRelativePath(Root,Source).Replace('\\','/');
        var OrderedOptions=ImportOptions.Resolve(Registration.Descriptor,Registration.Processors,Options);
        var OptionsHash=Hash256.Compute(JsonSerializer.SerializeToUtf8Bytes(OrderedOptions));
        var Catalog=Path.Combine(this.CacheDirectory,AssetId+".import.json");
        Directory.CreateDirectory(this.CacheDirectory);
        ImportCacheEntry?Previous=null;
        if(File.Exists(Catalog)){
            if(new FileInfo(Catalog).Length>4*1024*1024){ throw new InvalidDataException("IMP803: import catalog exceeds its bound."); }
            Previous=JsonSerializer.Deserialize<ImportCacheEntry>(await File.ReadAllBytesAsync(Catalog,CancellationToken))??throw new InvalidDataException("IMP901: invalid import catalog.");
            if(Previous.ReadSet is null||Previous.ReadSet.Length>65535||Previous.Diagnostics is null||Previous.Diagnostics.Length>256||!Hash256.TryParse(Previous.IrHash,out _)){ throw new InvalidDataException("IMP901: invalid import catalog fields."); }
        }
        var Reads=(Previous?.ReadSet??[]).Append(Uri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        var Fingerprint=await FingerprintForAsync(Root,Reads,Registration,OptionsHash,CancellationToken);
        if(!Verify&&Previous is not null&&Previous.Importer==Registration.Descriptor.Identity&&Previous.ModuleHash==Registration.ModuleHash.ToString()&&Previous.OptionsHash==OptionsHash.ToString()&&Previous.Fingerprint==Fingerprint.ToString()){
            var IrHash=Hash256.Parse(Previous.IrHash);
            var PathValue=Path.Combine(this.CacheDirectory,IrHash+".cgex");
            var Bytes=await ReadBoundedAsync(PathValue,InterchangeCodec.DefaultMaximumBytes,CancellationToken);
            if(Hash256.Compute(Bytes)!=IrHash){ throw new InvalidDataException("IMP901: cached IR address differs from its bytes."); }
            var Document=InterchangeCodec.Read(Bytes);
            if(Document.Provenance.SourceUri!=Uri||Document.Provenance.ImporterIdentity!=Registration.Descriptor.Identity||!Document.Provenance.ReadSet.SequenceEqual(Previous.ReadSet,StringComparer.Ordinal)){ throw new InvalidDataException("IMP901: cached IR provenance differs from its catalog."); }
            if(ModelImportPipeline.ValidateEngine(Document).Any(D=>D.Severity==ImportSeverity.Error)){ throw new InvalidDataException("IMP901: cached IR violates engine invariants."); }
            return new(Document,[..Previous.Diagnostics],true,Fingerprint);
        }
        var First=await this.RunWorkerAsync(Registration,AssetId,Root,Uri,OrderedOptions,CancellationToken);
        if(First.Document is null){ return new(null,First.Diagnostics,false,Fingerprint); }
        var Canonical=InterchangeCodec.Write(First.Document);
        var ReadSet=First.Document.Provenance.ReadSet.Append(Uri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        if(await FingerprintForAsync(Root,Reads,Registration,OptionsHash,CancellationToken)!=Fingerprint){ throw new InvalidDataException("IMP901: source dependencies changed during import."); }
        var Current=await FingerprintForAsync(Root,ReadSet,Registration,OptionsHash,CancellationToken);
        if(Verify||ReadSet.Except(Reads,StringComparer.Ordinal).Any()){
            var Second=await this.RunWorkerAsync(Registration,AssetId,Root,Uri,OrderedOptions,CancellationToken);
            if(Second.Document is null){ return new(null,Second.Diagnostics,false,Fingerprint); }
            var SecondReads=Second.Document.Provenance.ReadSet.Append(Uri).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            if(!SecondReads.SequenceEqual(ReadSet,StringComparer.Ordinal)||await FingerprintForAsync(Root,ReadSet,Registration,OptionsHash,CancellationToken)!=Current){ throw new InvalidDataException("IMP901: discovered dependencies changed during import."); }
            var SecondCanonical=InterchangeCodec.Write(Second.Document);
            if(Verify&&!Canonical.AsSpan().SequenceEqual(SecondCanonical)){
                return new(null,[new(ImportSeverity.Error,"IMP901",Registration.Descriptor.Identity,ImportPhase.Validate,"Separate workers produced different IR.",new(Uri))],false,Fingerprint);
            }
            First=Second;
            Canonical=SecondCanonical;
        }
        var Address=Hash256.Compute(Canonical);
        var CachePath=Path.Combine(this.CacheDirectory,Address+".cgex");
        if(File.Exists(CachePath)){
            if(Hash256.Compute(await ReadBoundedAsync(CachePath,InterchangeCodec.DefaultMaximumBytes,CancellationToken))!=Address){ throw new InvalidDataException("IMP901: existing IR cache object is corrupt."); }
        }
        else { await AtomicWriteAsync(CachePath,Canonical,CancellationToken); }
        var Entry=new ImportCacheEntry(Registration.Descriptor.Identity,Registration.ModuleHash.ToString(),OptionsHash.ToString(),Current.ToString(),Address.ToString(),ReadSet,[..First.Diagnostics]);
        var EntryBytes=JsonSerializer.SerializeToUtf8Bytes(Entry);
        if(ReadSet.Length>65535||EntryBytes.Length>4*1024*1024){ throw new InvalidDataException("IMP803: import catalog exceeds its bound."); }
        await AtomicWriteAsync(Catalog,EntryBytes,CancellationToken);
        return new(First.Document,First.Diagnostics,false,Current);
    }
    private async Task<(InterchangeDocument?Document,ImmutableArray<ImportDiagnostic>Diagnostics)>RunWorkerAsync(ImporterRegistration Registration,AssetId AssetId,string Root,string SourceUri,IReadOnlyDictionary<string,string>Options,CancellationToken CancellationToken){
        if(Hash256.Compute(await File.ReadAllBytesAsync(Registration.ModulePath,CancellationToken))!=Registration.ModuleHash){ throw new InvalidDataException("IMP901: import module changed after registry discovery."); }
        var Scratch=Path.Combine(this.CacheDirectory,"workers",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Scratch);
        var Request=Path.Combine(Scratch,"request.json");
        var Result=Path.Combine(Scratch,"result.json");
        var Ir=Path.Combine(Scratch,"output.cgex");
        await File.WriteAllBytesAsync(Request,JsonSerializer.SerializeToUtf8Bytes(new {
            ImporterIdentity=Registration.Descriptor.Identity,AssetId=AssetId.ToString(),SourcePath=SourceUri,ProjectDirectory=Root,ScratchDirectory=Scratch,OutputPath=Ir,Options
        }),CancellationToken);
        var Process=await this.Runner.RunAsync(["import-worker",Registration.ModulePath,Request,Result],CancellationToken);
        if(Hash256.Compute(await File.ReadAllBytesAsync(Registration.ModulePath,CancellationToken))!=Registration.ModuleHash){ throw new InvalidDataException("IMP901: import module changed during import."); }
        if(!File.Exists(Result)){ throw new InvalidDataException("IMP801: import worker did not publish a result: "+Process.Error); }
        using var Json=JsonDocument.Parse(await ReadBoundedAsync(Result,4*1024*1024,CancellationToken));
        var Diagnostics=Json.RootElement.GetProperty("Diagnostics").Deserialize<ImmutableArray<ImportDiagnostic>>();
        if(Process.ExitCode!=0||!Json.RootElement.GetProperty("Success").GetBoolean()){ return (null,Diagnostics); }
        if(Json.RootElement.GetProperty("OutputPath").GetString()!=Ir){ throw new InvalidDataException("IMP006: worker result identifies an unexpected output."); }
        var Document=InterchangeCodec.Read(await ReadBoundedAsync(Ir,InterchangeCodec.DefaultMaximumBytes,CancellationToken));
        var Validation=ModelImportPipeline.ValidateEngine(Document);
        if(Validation.Any(D=>D.Severity==ImportSeverity.Error)){ return (null,Diagnostics.AddRange(Validation)); }
        return (Document,Diagnostics);
    }
    private static async Task<Hash256>FingerprintForAsync(string Root,string[]ReadSet,ImporterRegistration Registration,Hash256 OptionsHash,CancellationToken CancellationToken){
        using var Buffer=new MemoryStream();
        var ReadBuffer=new byte[64*1024];
        using(var Writer=new BinaryWriter(Buffer,System.Text.Encoding.UTF8,true)){
            Writer.Write("cge-source-fingerprint/1");Writer.Write(InterchangeCodec.WireVersion);Writer.Write(EngineHash.ToByteArray());Writer.Write(Registration.ModuleHash.ToByteArray());Writer.Write(Registration.Descriptor.Identity);Writer.Write(Registration.Descriptor.Version);Writer.Write(OptionsHash.ToByteArray());
            foreach(var Relative in ReadSet){
                CancellationToken.ThrowIfCancellationRequested();
                var Full=Resolve(Root,Relative);
                Writer.Write(Relative);
                var Exists=File.Exists(Full);
                Writer.Write(Exists);
                if(Exists){
                    await using var Stream=new FileStream(Full,FileMode.Open,FileAccess.Read,FileShare.Read,64*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
                    if(Stream.Length>2L*1024*1024*1024){ throw new InvalidDataException("IMP803: source dependency exceeds 2 GiB."); }
                    using var Hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                    while(true){
                        var Count=await Stream.ReadAsync(ReadBuffer,CancellationToken);
                        if(Count==0){ break; }
                        Hash.AppendData(ReadBuffer,0,Count);
                    }
                    Writer.Write(Hash.GetHashAndReset());
                }
            }
        }
        return Hash256.Compute(Buffer.ToArray());
    }
    internal static string Resolve(string Root,string Relative){
        var Full=Path.GetFullPath(Path.IsPathRooted(Relative)?Relative:Path.Combine(Root,Relative));
        if(!Full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("IMP006: source or read-set dependency escapes project root."); }
        for(var Entry=Full;Entry is not null;Entry=Path.GetDirectoryName(Entry)){
            if((File.Exists(Entry)||Directory.Exists(Entry))&&(File.GetAttributes(Entry)&FileAttributes.ReparsePoint)!=0){ throw new InvalidDataException("IMP006: reparse-point dependencies are not allowed."); }
        }
        return Full;
    }
    private static async Task<byte[]>ReadBoundedAsync(string PathValue,int MaximumBytes,CancellationToken CancellationToken){
        if(new FileInfo(PathValue).Length>MaximumBytes){ throw new InvalidDataException("IMP803: import file exceeds its bound."); }
        var Bytes=await File.ReadAllBytesAsync(PathValue,CancellationToken);
        if(Bytes.Length>MaximumBytes){ throw new InvalidDataException("IMP803: import file changed size beyond its bound."); }
        return Bytes;
    }
    private static async Task AtomicWriteAsync(string PathValue,byte[]Bytes,CancellationToken CancellationToken){
        var Temporary=PathValue+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            await File.WriteAllBytesAsync(Temporary,Bytes,CancellationToken);
            CancellationToken.ThrowIfCancellationRequested();
            File.Move(Temporary,PathValue,true);
        } finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
    }
}
