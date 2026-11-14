using System.Collections.Immutable;
using System.Text.Json;
using CSharpGameEngine.Content;
using CSharpGameEngine.Contracts;
using CSharpGameEngine.Interchange;
namespace CSharpGameEngine.ContentCompiler;
public sealed class ModelResourceClient(string CookToolPath,string ModulePath,string ProjectDirectory,string CacheDirectory) {
    private readonly ImportProcessRunner Runner=new(CookToolPath);
    private readonly string ModulePath=Path.GetFullPath(ModulePath);
    private readonly string ProjectDirectory=Path.GetFullPath(ProjectDirectory);
    private readonly string CacheDirectory=Path.GetFullPath(CacheDirectory);
    public async Task<ImmutableArray<PreparedModelArtifact>>PrepareAsync(AssetId AssetId,InterchangeDocument Document,Hash256 SourceHash,Hash256 OptionsHash,string ToolVersion,bool Verify=false,CancellationToken CancellationToken=default){
        if(Document.Materials.IsEmpty){ return []; }
        var Owned=Path.GetFullPath(Path.Combine(this.CacheDirectory,"model-resources",Guid.NewGuid().ToString("N")));
        if(!Owned.StartsWith(this.CacheDirectory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){ throw new InvalidOperationException("IMP903: model resource work directory escapes its cache."); }
        Directory.CreateDirectory(Owned);
        try {
            await File.WriteAllBytesAsync(Path.Combine(Owned,"source.cgex"),InterchangeCodec.Write(Document),CancellationToken);
            var Input=Path.Combine(Owned,"request.cgemodelresources");
            await File.WriteAllBytesAsync(Input,JsonSerializer.SerializeToUtf8Bytes(new { Schema="cge-model-resources/1",AssetId=AssetId.ToString(),SourceHash=SourceHash.ToString(),OptionsHash=OptionsHash.ToString(),ToolVersion }),CancellationToken);
            var Output=Path.Combine(Owned,"out");
            var Arguments=new List<string>{"cook",this.ModulePath,Input,Output,"--project",this.ProjectDirectory,"--cooker","ModelResources"};
            if(Verify){ Arguments.Add("--verify"); }
            var Result=await this.Runner.RunAsync(Arguments,CancellationToken);
            if(Result.ExitCode!=0){ throw new InvalidDataException("IMP903: model resource worker failed. "+Result.Error+Result.Output); }
            var Manifest=Path.Combine(Output,"resources.json");
            if(!File.Exists(Manifest)||new FileInfo(Manifest).Length>16*1024*1024){ throw new InvalidDataException("IMP903: missing or oversized model resource manifest."); }
            using var Json=JsonDocument.Parse(await File.ReadAllBytesAsync(Manifest,CancellationToken));
            var Root=Json.RootElement;
            if(Root.GetProperty("Schema").GetString()!="cge-model-resources/1"||Root.GetProperty("AssetId").GetString()!=AssetId.ToString()){
                throw new InvalidDataException("IMP903: inconsistent model resource identity.");
            }
            var Entries=Root.GetProperty("Outputs");
            if(Entries.GetArrayLength()>65535){ throw new InvalidDataException("IMP903: too many model resources."); }
            var Provenance=new ArtifactProvenance(ToolVersion,Document.Provenance.SourceUri,SourceHash,Document.Provenance.ImporterIdentity,OptionsHash);
            var Artifacts=ImmutableArray.CreateBuilder<PreparedModelArtifact>();
            var Keys=new HashSet<AssetKey>();
            var Files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var Entry in Entries.EnumerateArray()){
                var FileName=Entry.GetProperty("File").GetString()!;
                if(FileName.Length!=11||!FileName.EndsWith(".cgea",StringComparison.Ordinal)||FileName.AsSpan(0,6).ContainsAnyExceptInRange('0','9')||!Files.Add(FileName)){
                    throw new InvalidDataException("IMP903: invalid or duplicate resource output path.");
                }
                if(!AssetUri.TryParse(Entry.GetProperty("Uri").GetString(),out var Uri,out _)||Uri.Key.AssetId!=AssetId||Uri.Key.SubAssetId is null||!Keys.Add(Uri.Key)){
                    throw new InvalidDataException("IMP903: invalid or duplicate resource output identity.");
                }
                var PathValue=Path.Combine(Output,FileName);
                if(new FileInfo(PathValue).Length>512L*1024*1024){ throw new InvalidDataException("IMP903: resource envelope is too large."); }
                var Name=Entry.GetProperty("Name").GetString()!;
                if(Name.Length>4096){ throw new InvalidDataException("IMP903: resource name is too long."); }
                var Artifact=ModelArtifactPreparation.Validate(Uri.Key,Name,await File.ReadAllBytesAsync(PathValue,CancellationToken),Provenance);
                if(Artifact.Kind is not(ContentArtifactKind.Image or ContentArtifactKind.Texture or ContentArtifactKind.Material)){ throw new InvalidDataException("IMP903: unexpected prepared resource kind."); }
                Artifacts.Add(Artifact);
            }
            return Artifacts.ToImmutable();
        }finally{
            if(Directory.Exists(Owned)&&Owned.StartsWith(this.CacheDirectory.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)){ Directory.Delete(Owned,true); }
        }
    }
}
