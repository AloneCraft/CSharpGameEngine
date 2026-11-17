using System.Collections.Immutable;
using System.Text;
using CSharpGameEngine.Content;
using CSharpGameEngine.Content.Models;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Core;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.ContentCompiler;
public sealed class ModelProjectCompiler(string ProjectDirectory,string CookToolPath,IEnumerable<(string ModulePath,bool ProjectModule)>Modules,string ContentDirectory,string?ResourcesModule=null) {
    private static readonly StableId ImplicitAssetNamespace=StableId.Parse("b00b52c8-1e3e-56ed-8f47-c4569aa8ad73");
    private readonly string ProjectDirectory=Path.GetFullPath(ProjectDirectory);
    private readonly string CookToolPath=Path.GetFullPath(CookToolPath);
    private readonly (string ModulePath,bool ProjectModule)[]Modules=[..Modules];
    private readonly string ContentDirectory=Path.GetFullPath(ContentDirectory);
    private readonly string?ResourcesModule=ResourcesModule is null?null:Path.GetFullPath(ResourcesModule);
    public Task<ModelContentCompileResult>CompileAsync(IEnumerable<string>SourcePaths,IReadOnlyDictionary<string,string>?GlobalOptions=null,bool Verify=false,CancellationToken CancellationToken=default){
        // Mutex は取得したスレッドで解放する必要がある。非同期コンパイルの継続が別スレッドへ移っても
        // 所有スレッドが変わらないよう、待機と解放を同じワーカースレッドに置く。
        var Identity=Hash256.Compute(Encoding.UTF8.GetBytes(OperatingSystem.IsWindows()?this.ContentDirectory.ToUpperInvariant():this.ContentDirectory));
        return Task.Run(()=>{
            CancellationToken.ThrowIfCancellationRequested();
            using var Gate=new Mutex(false,"CSharpGameEngine.Models."+Identity);
            var Acquired=false;
            try {
                try { Acquired=WaitHandle.WaitAny([Gate,CancellationToken.WaitHandle])==0; }
                catch(AbandonedMutexException){ Acquired=true; }
                CancellationToken.ThrowIfCancellationRequested();
                return this.CompileExclusiveAsync(SourcePaths,GlobalOptions,Verify,CancellationToken).GetAwaiter().GetResult();
            } finally {
                if(Acquired){ Gate.ReleaseMutex(); }
            }
        },CancellationToken);
    }
    private async Task<ModelContentCompileResult>CompileExclusiveAsync(IEnumerable<string>SourcePaths,IReadOnlyDictionary<string,string>?GlobalOptions,bool Verify,CancellationToken CancellationToken){
        var Cache=Path.Combine(this.ProjectDirectory,"obj","cge-content");Directory.CreateDirectory(Cache);
        var OutputIdentity=Hash256.Compute(Encoding.UTF8.GetBytes(this.ContentDirectory));
        using var Gate=new FileStream(Path.Combine(Cache,"models-"+OutputIdentity+".lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
        var IndexPath=Path.Combine(this.ContentDirectory,"content-index.json");
        var Previous=File.Exists(IndexPath)?ModelContentIndex.Read(await File.ReadAllBytesAsync(IndexPath,CancellationToken)):new ModelContentIndex([]);
        var Paths=SourcePaths.Select(PathValue=>ModelImportClient.Resolve(this.ProjectDirectory,PathValue)).ToArray();
        if(Paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=Paths.Length){ throw new InvalidDataException("IMP010: a model source occurs more than once in the project input set."); }
        var Registry=Paths.Length==0?null:await ImporterRegistry.DiscoverAsync(this.CookToolPath,this.Modules,Path.Combine(Cache,"registry"),CancellationToken);
        var Diagnostics=new List<ImportDiagnostic>();
        var Inputs=new List<ModelCompileInput>();
        foreach(var FullSource in Paths.Order(StringComparer.Ordinal)){
            var SourceUri=Path.GetRelativePath(this.ProjectDirectory,FullSource).Replace('\\','/').Normalize(NormalizationForm.FormC);
            var MetadataPath=ModelImportClient.Resolve(this.ProjectDirectory,SourceUri+".meta");
            var Asset=new AssetId(StableId.FromName(ImplicitAssetNamespace,SourceUri));
            var Options=new SortedDictionary<string,string>(StringComparer.Ordinal);
            if(GlobalOptions is not null){ foreach(var Pair in GlobalOptions){ Options.Add(Pair.Key,Pair.Value); } }
            string?Identity=null;Hash256?MetadataHash=null;string?PlanError=null;
            if(File.Exists(MetadataPath)){
                try {
                    var Read=await ModelSourceMetadata.ReadAsync(FullSource,SourceUri,CancellationToken);var Metadata=Read.Metadata;MetadataHash=Read.Hash;
                    Asset=Metadata.AssetId;Identity=Metadata.Importer;
                    foreach(var Pair in Metadata.Options){ if(!Options.ContainsKey(Pair.Key)){ Options.Add(Pair.Key,Pair.Value); } }
                }catch(Exception Error)when(Error is IOException or InvalidDataException or ArgumentException){
                    PlanError=Error.Message;
                    var Last=Previous.Entries.FirstOrDefault(Entry=>Entry.SourceUri==SourceUri);
                    if(Last is not null){ Asset=AssetId.Parse(Last.Asset); }
                }
            }
            var CapturedAsset=Asset;var CapturedIdentity=Identity;var CapturedError=PlanError;var CapturedHash=MetadataHash;
            Inputs.Add(new(CapturedAsset,SourceUri,async Token=>{
                if(CapturedError is not null){ throw new InvalidDataException(CapturedError); }
                var Extension=Path.GetExtension(FullSource);
                var Selected=CapturedIdentity;
                if(Selected is null){
                    var Candidates=Registry!.Candidates(Extension);
                    if(Candidates.Count!=1){ throw new InvalidDataException(Candidates.Count==0?"IMP002: no importer is registered for this model.":"IMP003: select a model importer in metadata."); }
                    Selected=Candidates[0].Identity;
                    Diagnostics.Add(new(ImportSeverity.Warning,"IMP001","cge.content",ImportPhase.Read,$"Using implicit AssetId {CapturedAsset}; stable metadata is absent.",new(SourceUri)));
                }
                var Registration=Registry!.Resolve(Selected,Extension);
                var Imported=await new ModelImportClient(this.CookToolPath,Path.Combine(Cache,"ir-cache")).ImportAsync(Registration,CapturedAsset,this.ProjectDirectory,SourceUri,Options,Verify,Token);
                Diagnostics.AddRange(Imported.Diagnostics);
                if(Imported.Document is null){ throw new InvalidDataException("IMP101: model import failed."); }
                var Resolved=ImportOptions.Resolve(Registration.Descriptor,Registration.Processors,Options);
                var OptionsHash=Hash256.Compute(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Resolved));
                var Resources=await new ModelResourceClient(this.CookToolPath,this.ResourcesModule??Registration.ModulePath,this.ProjectDirectory,Cache).PrepareAsync(CapturedAsset,Imported.Document,Imported.SourceHash,OptionsHash,ModelArtifactPreparation.CurrentToolVersion,Verify,Token);
                if(!await ModelSourceMetadata.UnchangedAsync(MetadataPath,CapturedHash,Token)){
                    throw new InvalidDataException("IMP901: model metadata changed during preparation.");
                }
                return ModelArtifactPreparation.Prepare(CapturedAsset,Imported.Document,Imported.SourceHash,OptionsHash,ModelArtifactPreparation.CurrentToolVersion,Resources,Resolved.TryGetValue(Cooking.ImportOptions.VirtualGeometryEnabled,out var VirtualGeometry)&&VirtualGeometry=="true");
            }));
        }
        var Result=await new ModelContentCompiler(new ContentAddressedStore(this.ContentDirectory),Path.Combine(Cache,"model-state-"+OutputIdentity+".json"),IndexPath).CompileAsync(Inputs,CancellationToken);
        return Result with {Diagnostics=[..Diagnostics,..Result.Diagnostics]};
    }
}
