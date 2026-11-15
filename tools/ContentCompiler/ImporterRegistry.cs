using System.Collections.Immutable;
using System.Text.Json;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Cooking;
namespace CSharpGameEngine.ContentCompiler;
public sealed record ImporterRegistration(string ModulePath,Hash256 ModuleHash,ImporterDescriptor Descriptor,bool ProjectModule) {
    public ImmutableArray<ModelProcessorDescriptor>Processors{get;init;}=[];
}
/// <summary>リフレクションで読み込んだプロバイダーコードではなく、describe サブプロセスの出力からレジストリを構築する。</summary>
public sealed class ImporterRegistry:IImporterCatalog {
    private readonly ImmutableArray<ImporterRegistration>Registrations;
    public ImporterRegistry(IEnumerable<ImporterRegistration>Registrations){
        var Entries=Registrations.ToArray();
        if(Entries.Any(Entry=>Entry is null||Entry.Descriptor is null)){ throw new InvalidDataException("IMP002: null importer registration."); }
        this.Registrations=[..Entries.OrderByDescending(R=>R.Descriptor.Priority).ThenByDescending(R=>R.ProjectModule).ThenBy(R=>R.Descriptor.Identity,StringComparer.Ordinal)];
        foreach(var Registration in this.Registrations){
            var Descriptor=Registration.Descriptor;
            if(!Registration.ModuleHash.IsValid||string.IsNullOrWhiteSpace(Registration.ModulePath)||string.IsNullOrWhiteSpace(Descriptor.Identity)||Descriptor.Version<1||Descriptor.Extensions.IsDefaultOrEmpty||Descriptor.Extensions.Any(Extension=>string.IsNullOrEmpty(Extension)||Extension[0]!='.'||Extension!=Extension.ToLowerInvariant())||Descriptor.Options.IsDefault||Registration.Processors.IsDefault||!Enum.IsDefined(Descriptor.Isolation)||!Enum.IsDefined(Descriptor.Determinism)){
                throw new InvalidDataException("IMP002: invalid importer registration.");
            }
            _=ImportOptions.Resolve(Descriptor,Registration.Processors,new Dictionary<string,string>());
        }
        if(this.Registrations.Select(R=>R.Descriptor.Identity).Distinct(StringComparer.Ordinal).Count()!=this.Registrations.Length){ throw new InvalidDataException("IMP003: importer identities must be unique across modules."); }
    }
    public IReadOnlyList<ImporterCandidateDto>Candidates(string Extension)=>this.Registrations.Where(R=>R.Descriptor.Extensions.Contains(Extension,StringComparer.OrdinalIgnoreCase)).Select(R=>new ImporterCandidateDto {
        Identity=R.Descriptor.Identity,Version=R.Descriptor.Version,DisplayName=R.Descriptor.DisplayName,Extensions=[..R.Descriptor.Extensions],Priority=R.Descriptor.Priority,Provider=R.Descriptor.Provider
    }).ToArray();
    public bool IsValid(string Identity,string Extension)=>this.Registrations.Any(R=>R.Descriptor.Identity==Identity&&R.Descriptor.Extensions.Contains(Extension,StringComparer.OrdinalIgnoreCase));
    public ImporterRegistration Resolve(string Identity,string Extension)=>this.Registrations.SingleOrDefault(R=>R.Descriptor.Identity==Identity&&R.Descriptor.Extensions.Contains(Extension,StringComparer.OrdinalIgnoreCase))??throw new InvalidDataException($"IMP002: importer '{Identity}' does not read '{Extension}'.");
    public static async Task<ImporterRegistry>DiscoverAsync(string CookToolPath,IEnumerable<(string ModulePath,bool ProjectModule)>Modules,string CacheDirectory,CancellationToken CancellationToken=default){
        Directory.CreateDirectory(CacheDirectory);
        var Runner=new ImportProcessRunner(CookToolPath);
        var Registrations=new List<ImporterRegistration>();
        foreach(var (ModulePath,ProjectModule) in Modules){
            CancellationToken.ThrowIfCancellationRequested();
            var Full=Path.GetFullPath(ModulePath);
            var Hash=Hash256.Compute(await File.ReadAllBytesAsync(Full,CancellationToken));
            var Cache=Path.Combine(CacheDirectory,Hash+".registry.json");
            string Description;
            if(File.Exists(Cache)){
                var Info=new FileInfo(Cache);
                if(Info.Length>1024*1024){ throw new InvalidDataException("IMP803: importer descriptor cache exceeds its bound."); }
                Description=await File.ReadAllTextAsync(Cache,CancellationToken);
            }
            else {
                var Output=await Runner.RunAsync(["describe",Full],CancellationToken);
                if(Output.ExitCode!=0){ throw new InvalidDataException("IMP801: describe failed: "+Output.Error); }
                Description=Output.Output;
            }
            if(System.Text.Encoding.UTF8.GetByteCount(Description)>1024*1024){ throw new InvalidDataException("IMP803: importer descriptor exceeds its byte bound."); }
            using var Json=JsonDocument.Parse(Description);
            if(Json.RootElement.GetProperty("Schema").GetString()!="cge-import-module/1"){ throw new InvalidDataException("IMP002: unsupported import module description."); }
            var Importers=Json.RootElement.GetProperty("Importers").Deserialize<ImporterDescriptor[]>()??throw new InvalidDataException("IMP002: missing importer descriptors.");
            var Processors=Json.RootElement.GetProperty("Processors").Deserialize<ImmutableArray<ModelProcessorDescriptor>>();
            foreach(var Descriptor in Importers){ Registrations.Add(new(Full,Hash,Descriptor,ProjectModule) { Processors=Processors }); }
            if(!File.Exists(Cache)){
                var Temporary=Cache+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { await File.WriteAllTextAsync(Temporary,Description,CancellationToken);CancellationToken.ThrowIfCancellationRequested();File.Move(Temporary,Cache,true); }
                finally { if(File.Exists(Temporary)){ File.Delete(Temporary); } }
            }
        }
        return new(Registrations);
    }
}
