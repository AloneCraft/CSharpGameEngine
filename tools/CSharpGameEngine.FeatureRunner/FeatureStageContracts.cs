using System.Security.Cryptography;
using System.Text;
namespace CSharpGameEngine.FeatureStages;
public enum FeatureLifecycleStatus {
    Implemented,
    PartiallyImplemented,
    Implementing,
    Specified,
    Deferred,
}
public enum FeatureRoadmapTrack {
    Established,
    Rendering,
    AssetContent,
    EditorVsix,
    EcsRuntime,
    AudioInputPlatform,
    TestingCiRelease,
}
public enum FeatureStageAvailability {
    Executable,
    NotImplemented,
}
public enum FeatureStageMode {
    Headless,
    Visual,
}
public enum FeatureStagePack {
    Foundation,
    Rendering,
    Rigid,
    Deformable,
    Fluid,
    Destruction,
    Celestial,
    ProbabilityQuantum,
    Coupling,
    Vsix,
}
public enum FeatureAdapterRequirement {
    None,
    WarpRequiredHardwareOptional,
}
public enum FeatureAdapter {
    Warp,
    Hardware,
}
public enum FeatureStageRunStatus {
    Passed,
    Failed,
    Blocked,
    NotImplemented,
    TimedOut,
    InfrastructureError,
}
public sealed record FeatureDefinition(string Id,string Name,string Layer,FeatureLifecycleStatus Status,FeatureRoadmapTrack Track,int DependencyLevel,IReadOnlyList<string>DependsOnFeatureIds);
public sealed record FeatureStageCommand(string FileName,IReadOnlyList<string>Arguments,IReadOnlyList<string>WarpArguments,IReadOnlyList<string>HardwareArguments){
    public FeatureStageCommand(string FileName,IReadOnlyList<string>Arguments):this(FileName,Arguments,[],[]){
    }
}
public sealed record FeatureStageDefinition(
    string Id,
    string Name,
    FeatureStagePack Pack,
    FeatureStageMode Mode,
    FeatureStageAvailability Availability,
    FeatureAdapterRequirement AdapterRequirement,
    IReadOnlyList<string>FeatureIds,
    IReadOnlyList<string>Profiles,
    uint Seed,
    int Ticks,
    int TimeoutSeconds,
    string Oracle,
    IReadOnlyList<FeatureStageCommand>Commands,
    IReadOnlyList<string>InputPaths,
    string?SceneProject=null,
    string?SceneId=null);
public sealed record FeatureStageExecutionOptions(
    string RepositoryRoot,
    string Configuration,
    string Profile,
    FeatureAdapter Adapter,
    uint Seed,
    int Ticks,
    string GitCommit="unknown",
    bool IsDirty=false);
public sealed record FeatureStageAssertion(string Name,string Details,bool Passed);
public sealed record FeatureStageRunResult(
    string StageId,
    IReadOnlyList<string>FeatureIds,
    string Pack,
    string Configuration,
    string Profile,
    string Adapter,
    uint Seed,
    int Ticks,
    string GitCommit,
    bool IsDirty,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    long DurationMilliseconds,
    string?StateHash,
    string?EventHash,
    IReadOnlyList<FeatureStageAssertion>Assertions,
    IReadOnlyList<string>Errors,
    string ArtifactSha256,
    FeatureStageRunStatus Status,
    string Summary,
    string StandardOutput,
    string StandardError){
    public static FeatureStageRunResult Create(FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,FeatureStageRunStatus Status,string Summary,IReadOnlyList<FeatureStageAssertion>Assertions,string?StateHash,string?EventHash,IReadOnlyList<string>?Errors=null,string StandardOutput="",string StandardError="",DateTimeOffset?StartedUtc=null,DateTimeOffset?FinishedUtc=null){
        var Started=StartedUtc??DateTimeOffset.UtcNow;
        var Finished=FinishedUtc??DateTimeOffset.UtcNow;
        var ResolvedErrors=Errors??[];
        var Evidence=string.Join("\n",[Stage.Id,Status.ToString(),Summary,StateHash??string.Empty,EventHash??string.Empty,StandardOutput,StandardError,.. Assertions.Select(static Assertion=>$"{Assertion.Name}:{Assertion.Passed}:{Assertion.Details}"),.. ResolvedErrors]);
        var ArtifactSha256=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Evidence))).ToLowerInvariant();
        return new FeatureStageRunResult(Stage.Id,Stage.FeatureIds,Stage.Pack.ToString(),Options.Configuration,Options.Profile,Options.Adapter.ToString(),Options.Seed,Options.Ticks,Options.GitCommit,Options.IsDirty,Started,Finished,(long)Math.Max(0,(Finished-Started).TotalMilliseconds),StateHash,EventHash,Assertions,ResolvedErrors,ArtifactSha256,Status,Summary,StandardOutput,StandardError);
    }
}
public sealed record FeatureRunResultDocument(string SchemaVersion,string Profile,DateTimeOffset CreatedUtc,IReadOnlyList<FeatureStageRunResult>Results){
    public const string CurrentSchemaVersion="csharp-game-engine-feature-run-result-v1";
    public static FeatureRunResultDocument Create(string Profile,IReadOnlyList<FeatureStageRunResult>Results)=>new(CurrentSchemaVersion,Profile,DateTimeOffset.UtcNow,Results);
}
public interface IFeatureStageExecutor {
    Task<FeatureStageRunResult>ExecuteAsync(FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,CancellationToken CancellationToken);
}
public interface IFeatureRunResultStore {
    Task<string>SaveAsync(FeatureRunResultDocument Document,CancellationToken CancellationToken);
    Task<FeatureRunResultDocument>LoadAsync(string Path,CancellationToken CancellationToken);
}
public interface IChangedPathProvider {
    Task<IReadOnlyList<string>>GetChangedPathsAsync(string RepositoryRoot,CancellationToken CancellationToken);
}
