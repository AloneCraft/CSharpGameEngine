using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Content;
using CSharpGameEngine.Core;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler;
internal static class ModelImportCommands {
    private static readonly StableId ImplicitAssetNamespace=StableId.Parse("b00b52c8-1e3e-56ed-8f47-c4569aa8ad73");
    internal static async Task<int?>TryRunAsync(string[]Arguments,CancellationToken CancellationToken){
        if(Arguments.Length==0||Arguments[0] is not("import-ir" or "inspect" or "explain")){ return null; }
        try {
            if(Arguments.Length<2){ throw new ArgumentException("A source or CGEX file is required."); }
            if(Arguments[0]=="inspect"){
                if(Arguments.Length!=2){ throw new ArgumentException("inspect takes one CGEX path."); }
                var Info=new FileInfo(Arguments[1]);
                if(Info.Length>InterchangeCodec.DefaultMaximumBytes){ throw new InvalidDataException("IMP803: interchange file exceeds its bound."); }
                var Document=InterchangeCodec.Read(await File.ReadAllBytesAsync(Arguments[1],CancellationToken));
                Console.WriteLine(JsonSerializer.Serialize(new {
                    Schema="cge-import-summary/1",Document.SchemaVersion,Document.Space,Document.Provenance.SourceUri,
                    Meshes=Document.Meshes.Length,Materials=Document.Materials.Length,Nodes=Document.Nodes.Length,Skeletons=Document.Skeletons.Length,
                    Clips=Document.Clips.Take(256).Select(Clip=>new { Clip.Name,Clip.StartSeconds,Clip.EndSeconds }),Truncated=Document.Clips.Length>256
                }));
                return 0;
            }
            var Project=Directory.GetCurrentDirectory();
            string?Cook=null;
            string?Output=null;
            string?Identity=null;
            string?Asset=null;
            var Modules=new List<(string ModulePath,bool ProjectModule)>();
            var Options=new SortedDictionary<string,string>(StringComparer.Ordinal);
            var Verify=false;
            for(var Index=2;Index<Arguments.Length;Index++){
                switch(Arguments[Index]){
                    case "--project" when Index+1<Arguments.Length:Project=Arguments[++Index];break;
                    case "--cook" when Index+1<Arguments.Length:Cook=Arguments[++Index];break;
                    case "--module" when Index+1<Arguments.Length:Modules.Add((Arguments[++Index],true));break;
                    case "--out" when Index+1<Arguments.Length:Output=Arguments[++Index];break;
                    case "--importer" when Index+1<Arguments.Length:Identity=Arguments[++Index];break;
                    case "--asset" when Index+1<Arguments.Length:Asset=Arguments[++Index];break;
                    case "--verify":Verify=true;break;
                    case "--option" when Index+1<Arguments.Length:
                        var Pair=Arguments[++Index];
                        var Separator=Pair.IndexOf('=');
                        if(Separator<1||!Options.TryAdd(Pair[..Separator],Pair[(Separator+1)..])){ throw new ArgumentException("--option requires a unique name=value pair."); }
                        break;
                    default:throw new ArgumentException("Unknown or incomplete option: "+Arguments[Index]);
                }
            }
            if(Cook is null||Modules.Count==0){ throw new ArgumentException("--cook <Cook.Cli.dll> and --module <module.dll> are required."); }
            Project=Path.GetFullPath(Project);
            var Cache=Path.Combine(Project,"obj","cge-content");
            var Registry=await ImporterRegistry.DiscoverAsync(Cook,Modules,Path.Combine(Cache,"registry"),CancellationToken);
            var Extension=Path.GetExtension(Arguments[1]);
            var Candidates=Registry.Candidates(Extension);
            if(Arguments[0]=="explain"){
                Console.WriteLine(JsonSerializer.Serialize(new { Schema="cge-import-explain/1",Candidates,Sniff="NotEvaluated",SourceMetadata="NotInspected",PinnedImporter=Identity }));
                return 0;
            }
            if(Output is null){ throw new ArgumentException("import-ir requires --out <file.cgex>."); }
            var FullSource=ModelImportClient.Resolve(Project,Arguments[1]);
            var SourceUri=Path.GetRelativePath(Project,FullSource).Replace('\\','/');
            var MetadataPath=ModelImportClient.Resolve(Project,SourceUri+".meta");
            Hash256?MetadataHash=null;
            if(File.Exists(MetadataPath)){
                if(new FileInfo(MetadataPath).Length>64*1024){ throw new InvalidDataException("IMP007: metadata exceeds its bound."); }
                var MetadataBytes=await File.ReadAllBytesAsync(MetadataPath,CancellationToken);
                if(!AssetMetadata.TryParse(MetadataBytes,out var Metadata,out var MetadataError)||Metadata is null){ throw new InvalidDataException("IMP007: canonical metadata is required; migration is pending: "+MetadataError.Message); }
                if(Metadata.SourceUri!=SourceUri||(Identity is not null&&Identity!=Metadata.Importer)||(Asset is not null&&Asset!=Metadata.AssetId.ToString())){ throw new InvalidDataException("IMP007: explicit importer/identity or project-relative source URI disagrees with metadata."); }
                MetadataHash=Hash256.Compute(MetadataBytes);
                Identity=Metadata.Importer;Asset=Metadata.AssetId.ToString();
                foreach(var Option in Metadata.Options){ if(!Options.ContainsKey(Option.Key)){ Options.Add(Option.Key,Option.Value); } }
            }
            if(Identity is null){
                if(Candidates.Count==0){ throw new InvalidDataException("IMP002: no importer is registered for this source extension."); }
                if(Candidates.Count!=1){ throw new InvalidDataException("IMP003: choose a registered importer explicitly with --importer."); }
                Identity=Candidates[0].Identity;
            }
            var AssetId=Asset is null?new AssetId(StableId.FromName(ImplicitAssetNamespace,SourceUri.Normalize(System.Text.NormalizationForm.FormC))):Contracts.AssetId.Parse(Asset);
            if(Asset is null){ Console.Error.WriteLine($"{SourceUri}: warning IMP001: using implicit AssetId {AssetId}; explicit metadata integration is pending."); }
            var Registration=Registry.Resolve(Identity,Extension);
            var Result=await new ModelImportClient(Cook,Path.Combine(Cache,"ir-cache")).ImportAsync(Registration,AssetId,Project,Arguments[1],Options,Verify,CancellationToken);
            foreach(var Diagnostic in Result.Diagnostics){ Console.Error.WriteLine(Diagnostic); }
            if(Result.Document is null){ return 1; }
            if(File.Exists(MetadataPath)!=MetadataHash.HasValue||(MetadataHash is{ }Expected&&Hash256.Compute(await File.ReadAllBytesAsync(MetadataPath,CancellationToken))!=Expected)){ throw new InvalidDataException("IMP901: metadata changed during import; output was not published."); }
            var Destination=Path.GetFullPath(Output);
            if(string.Equals(Destination,FullSource,StringComparison.OrdinalIgnoreCase)){ throw new InvalidDataException("IMP006: output cannot replace its source."); }
            Directory.CreateDirectory(Path.GetDirectoryName(Destination)!);
            var Temporary=Destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { await File.WriteAllBytesAsync(Temporary,InterchangeCodec.Write(Result.Document),CancellationToken);CancellationToken.ThrowIfCancellationRequested();File.Move(Temporary,Destination,true); }
            finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            Console.WriteLine(JsonSerializer.Serialize(new { Schema="cge-import-result/1",Output=Destination,Result.Reused,SourceHash=Result.SourceHash.ToString(),AssetId=AssetId.ToString() }));
            return 0;
        } catch(OperationCanceledException){ Console.Error.WriteLine("IMP802: import cancelled.");return 1; }
        catch(Exception Error) when(Error is IOException or InvalidDataException or ArgumentException or JsonException or UnauthorizedAccessException){ Console.Error.WriteLine(Error.Message);return 1; }
    }
}
