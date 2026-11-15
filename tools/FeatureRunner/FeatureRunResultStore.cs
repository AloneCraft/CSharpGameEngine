using System.Text.Json;
using System.Text.Json.Serialization;
namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureRunResultStore:IFeatureRunResultStore {
    private static readonly JsonSerializerOptions JsonOptions=CreateJsonOptions();
    private readonly string ArtifactDirectory;
    public FeatureRunResultStore(string RepositoryRoot){
        this.ArtifactDirectory=Path.Combine(Path.GetFullPath(RepositoryRoot),".artifacts","FeatureStages");
    }
    public async Task<string>SaveAsync(FeatureRunResultDocument Document,CancellationToken CancellationToken){
        Directory.CreateDirectory(this.ArtifactDirectory);
        var SafeProfile=string.Concat(Document.Profile.Select(static Character=>char.IsLetterOrDigit(Character)||Character is '-' or '_'?Character:'-'));
        var FileName=$"{Document.CreatedUtc:yyyyMMddTHHmmssfffZ}-{SafeProfile}-{Guid.NewGuid():N}.run-result.json";
        var OutputPath=Path.Combine(this.ArtifactDirectory,FileName);
        await File.WriteAllTextAsync(OutputPath,JsonSerializer.Serialize(Document,JsonOptions),CancellationToken).ConfigureAwait(false);
        return OutputPath;
    }
    public async Task<FeatureRunResultDocument>LoadAsync(string PathValue,CancellationToken CancellationToken){
        var Json=await File.ReadAllTextAsync(System.IO.Path.GetFullPath(PathValue),CancellationToken).ConfigureAwait(false);
        var Document=JsonSerializer.Deserialize<FeatureRunResultDocument>(Json,JsonOptions)??throw new InvalidDataException("Feature run result is empty.");
        if(Document.SchemaVersion!=FeatureRunResultDocument.CurrentSchemaVersion){ throw new InvalidDataException($"Unsupported feature run result schema: {Document.SchemaVersion}"); }
        return Document;
    }
    internal static JsonSerializerOptions CreateJsonOptions(){
        var Options=new JsonSerializerOptions(){PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true};
        Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return Options;
    }
}
