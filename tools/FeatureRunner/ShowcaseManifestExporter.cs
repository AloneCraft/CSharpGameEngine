using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
public sealed record ShowcaseFeatureSummary(int Total,int Implemented,int PartiallyImplemented,int Specified,int Deferred,int Other);
public sealed record ShowcaseStageSummary(int Total,int Executable,int NotImplemented);
public sealed record ShowcaseManifestSummary(ShowcaseFeatureSummary Features,ShowcaseStageSummary Stages);
public sealed record ShowcaseFeature(string Id,string Name,string Layer,string Status,string Track,int DependencyLevel,IReadOnlyList<string>DependsOnFeatureIds);
public sealed record ShowcaseStage(string Id,string Name,string Pack,string Mode,string Availability,IReadOnlyList<string>FeatureIds,IReadOnlyList<string>Profiles,string Oracle);
public sealed record ShowcaseManifestPayload(string Schema,string CatalogSchema,ShowcaseManifestSummary Summary,IReadOnlyList<ShowcaseFeature>Features,IReadOnlyList<ShowcaseStage>Stages);
public sealed record ShowcaseManifestDocument(string Schema,string CatalogSchema,string ManifestSha256,ShowcaseManifestSummary Summary,IReadOnlyList<ShowcaseFeature>Features,IReadOnlyList<ShowcaseStage>Stages);
public static class ShowcaseManifestExporter {
    public const string Schema="csharp-game-engine-showcase-manifest-v1";
    public static void Export(FeatureStageCatalog Catalog,string OutputPath){
        ArgumentNullException.ThrowIfNull(Catalog);
        ArgumentException.ThrowIfNullOrWhiteSpace(OutputPath);
        var Features=Catalog.Features.OrderBy(static Feature=>LayerNumber(Feature.Layer)).ThenBy(static Feature=>Feature.Id,StringComparer.Ordinal).Select(static Feature=>new ShowcaseFeature(Feature.Id,Feature.Name,Feature.Layer,Feature.Status.ToString(),Feature.Track.ToString(),Feature.DependencyLevel,[.. Feature.DependsOnFeatureIds.Order(StringComparer.Ordinal)])).ToArray();
        var Stages=Catalog.Stages.OrderBy(static Stage=>Stage.Id,StringComparer.Ordinal).Select(static Stage=>new ShowcaseStage(Stage.Id,Stage.Name,Stage.Pack.ToString(),Stage.Mode.ToString(),Stage.Availability.ToString(),[.. Stage.FeatureIds.Order(StringComparer.Ordinal)],[.. Stage.Profiles.Order(StringComparer.OrdinalIgnoreCase)],Stage.Oracle)).ToArray();
        var FeatureSummary=new ShowcaseFeatureSummary(Features.Length,Features.Count(static Feature=>Feature.Status==nameof(FeatureLifecycleStatus.Implemented)),Features.Count(static Feature=>Feature.Status==nameof(FeatureLifecycleStatus.PartiallyImplemented)),Features.Count(static Feature=>Feature.Status==nameof(FeatureLifecycleStatus.Specified)),Features.Count(static Feature=>Feature.Status==nameof(FeatureLifecycleStatus.Deferred)),Features.Count(static Feature=>Feature.Status is not(nameof(FeatureLifecycleStatus.Implemented)or nameof(FeatureLifecycleStatus.PartiallyImplemented)or nameof(FeatureLifecycleStatus.Specified)or nameof(FeatureLifecycleStatus.Deferred))));
        var StageSummary=new ShowcaseStageSummary(Stages.Length,Stages.Count(static Stage=>Stage.Availability==nameof(FeatureStageAvailability.Executable)),Stages.Count(static Stage=>Stage.Availability==nameof(FeatureStageAvailability.NotImplemented)));
        var Summary=new ShowcaseManifestSummary(FeatureSummary,StageSummary);
        var Options=FeatureRunResultStore.CreateJsonOptions();
        var Payload=new ShowcaseManifestPayload(Schema,FeatureStageCatalog.CatalogSchemaVersion,Summary,Features,Stages);
        var PayloadBytes=JsonSerializer.SerializeToUtf8Bytes(Payload,Options);
        var Hash=Convert.ToHexString(SHA256.HashData(PayloadBytes)).ToLowerInvariant();
        var Document=new ShowcaseManifestDocument(Schema,FeatureStageCatalog.CatalogSchemaVersion,Hash,Summary,Features,Stages);
        var Json=JsonSerializer.Serialize(Document,Options).Replace("\r\n","\n",StringComparison.Ordinal)+"\n";
        var FullPath=Path.GetFullPath(OutputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(FullPath)!);
        var TemporaryPath=FullPath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try {
            File.WriteAllText(TemporaryPath,Json,new UTF8Encoding(false));
            File.Move(TemporaryPath,FullPath,true);
        } finally {
            if(File.Exists(TemporaryPath)){ File.Delete(TemporaryPath); }
        }
    }
    private static int LayerNumber(string Layer)=>int.TryParse(Layer.AsSpan(1),out var Value)?Value:int.MaxValue;
}
