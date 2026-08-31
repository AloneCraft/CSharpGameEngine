using System.Text.Json;
namespace UserEcsProject.Showcase;
public sealed record ShowcaseFeature(string Id,string Name,string Layer,string Status);
public sealed record ShowcaseProgressSummary(
    int FeatureTotal,int Implemented,int PartiallyImplemented,int Specified,int Deferred,
    int StageTotal,int Executable);
public sealed class ShowcaseManifest {
    private ShowcaseManifest(string Hash,ShowcaseProgressSummary Summary,IReadOnlyList<ShowcaseFeature>Features){
        this.Hash=Hash;
        this.Summary=Summary;
        this.Features=Features;
    }
    public string Hash { get;}
    public ShowcaseProgressSummary Summary { get;}
    public IReadOnlyList<ShowcaseFeature>Features { get;}
    public static ShowcaseManifest Load(string Path){
        using var Stream=File.OpenRead(Path);
        using var Document=JsonDocument.Parse(Stream);
        var Root=Document.RootElement;
        if(Root.GetProperty("schema").GetString()!="csharp-game-engine-showcase-manifest-v1"){
            throw new InvalidDataException("Unsupported showcase manifest schema.");
        }
        var FeatureSummary=Root.GetProperty("summary").GetProperty("features");
        var StageSummary=Root.GetProperty("summary").GetProperty("stages");
        var Summary=new ShowcaseProgressSummary(
            FeatureSummary.GetProperty("total").GetInt32(),
            FeatureSummary.GetProperty("implemented").GetInt32(),
            FeatureSummary.GetProperty("partiallyImplemented").GetInt32(),
            FeatureSummary.GetProperty("specified").GetInt32(),
            FeatureSummary.GetProperty("deferred").GetInt32(),
            StageSummary.GetProperty("total").GetInt32(),
            StageSummary.GetProperty("executable").GetInt32());
        var Features=Root.GetProperty("features").EnumerateArray()
            .Select(static Feature=>new ShowcaseFeature(
                Feature.GetProperty("id").GetString()??string.Empty,
                Feature.GetProperty("name").GetString()??string.Empty,
                Feature.GetProperty("layer").GetString()??string.Empty,
                Feature.GetProperty("status").GetString()??string.Empty))
            .OrderBy(static Feature=>Feature.Id,StringComparer.Ordinal)
            .ToArray();
        return new ShowcaseManifest(Root.GetProperty("manifestSha256").GetString()??string.Empty,Summary,Features);
    }
}
