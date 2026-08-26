using System.Text.Json;
namespace CSharpGameEngine.FeatureStages;
public sealed class FeatureRunnerApplication {
    private readonly FeatureStageCatalog Catalog;
    private readonly IFeatureStageExecutor Executor;
    private readonly IFeatureRunResultStore ResultStore;
    private readonly IChangedPathProvider ChangedPathProvider;
    private readonly TextWriter Output;
    private readonly TextWriter Error;
    public FeatureRunnerApplication(FeatureStageCatalog Catalog,IFeatureStageExecutor Executor,IFeatureRunResultStore ResultStore,IChangedPathProvider ChangedPathProvider,TextWriter Output,TextWriter Error){
        this.Catalog=Catalog;
        this.Executor=Executor;
        this.ResultStore=ResultStore;
        this.ChangedPathProvider=ChangedPathProvider;
        this.Output=Output;
        this.Error=Error;
    }
    public async Task<int>RunAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length==0||IsHelp(Args[0])){
            this.WriteHelp();
            return 0;
        }
        try {
            return Args[0].ToLowerInvariant()switch {
                "list"=>this.RunList(Args),
                "run"=>await this.RunStageAsync(Args,CancellationToken).ConfigureAwait(false),
                "run-feature"=>await this.RunFeatureAsync(Args,CancellationToken).ConfigureAwait(false),
                "suite"=>await this.RunSuiteAsync(Args,CancellationToken).ConfigureAwait(false),
                "resume"=>await this.ResumeAsync(Args,CancellationToken).ConfigureAwait(false),
                _=>this.UnknownCommand(Args[0]),
            };
        } catch(OperationCanceledException)when(CancellationToken.IsCancellationRequested){
            await this.Error.WriteLineAsync("Feature run was cancelled.").ConfigureAwait(false);
            return 130;
        } catch(Exception Ex)when(Ex is ArgumentException or FormatException or KeyNotFoundException or InvalidDataException or DirectoryNotFoundException){
            await this.Error.WriteLineAsync(Ex.Message).ConfigureAwait(false);
            return 2;
        }
    }
    private int RunList(string[]Args){
        var Json=Args.Skip(1).Any(static Argument=>Argument.Equals("--json",StringComparison.OrdinalIgnoreCase));
        if(Json){
            var Features=this.Catalog.Features.Select(static Feature=>new {id=Feature.Id,name=Feature.Name,layer=Feature.Layer,status=Feature.Status.ToString(),track=Feature.Track.ToString(),dependencyLevel=Feature.DependencyLevel,dependsOnFeatureIds=Feature.DependsOnFeatureIds});
            this.Output.WriteLine(JsonSerializer.Serialize(new {schema=FeatureStageCatalog.CatalogSchemaVersion,features=Features,stages=this.Catalog.Stages},FeatureRunResultStore.CreateJsonOptions()));
            return 0;
        }
        foreach(var Stage in this.Catalog.Stages){
            this.Output.WriteLine($"{Stage.Id,-42} {Stage.Availability,-14} {Stage.Pack,-20} {string.Join(',',Stage.FeatureIds)}");
        }
        return 0;
    }
    private async Task<int>RunStageAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("run requires a stage ID."); }
        var Stage=this.Catalog.GetStage(Args[1]);
        var Parsed=ParseOptions(Args,2,"single");
        return await this.ExecuteAndStoreAsync([Stage],Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>RunFeatureAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("run-feature requires a feature ID."); }
        var Feature=this.Catalog.GetFeature(Args[1]);
        var Stages=this.Catalog.Stages.Where(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)).ToArray();
        var Parsed=ParseOptions(Args,2,"feature");
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>RunSuiteAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("suite requires changed, smoke, full, stress, or vsix."); }
        var Suite=Args[1].ToLowerInvariant();
        if(Suite is not("changed" or "smoke" or "full" or "stress" or "vsix")){ throw new ArgumentException($"Unknown suite: {Args[1]}"); }
        var Parsed=ParseOptions(Args,2,Suite);
        FeatureStageDefinition[]Stages;
        if(Suite=="changed"){
            var ChangedPaths=await this.ChangedPathProvider.GetChangedPathsAsync(Parsed.RepositoryRoot,CancellationToken).ConfigureAwait(false);
            Stages=[.. this.Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable).Where(Stage=>MatchesChangedPath(Stage,ChangedPaths))];
            if(Stages.Length==0){ Stages=[this.Catalog.GetStage("runner.contract")]; }
        } else {
            Stages=[.. this.Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable).Where(Stage=>Stage.Profiles.Contains(Suite,StringComparer.OrdinalIgnoreCase))];
        }
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>ResumeAsync(string[]Args,CancellationToken CancellationToken){
        if(Args.Length<2){ throw new ArgumentException("resume requires a run-result.json path."); }
        var Previous=await this.ResultStore.LoadAsync(Args[1],CancellationToken).ConfigureAwait(false);
        var StageIds=Previous.Results.Where(static Result=>Result.Status!=FeatureStageRunStatus.Passed).Select(static Result=>Result.StageId).Distinct(StringComparer.Ordinal).ToArray();
        if(StageIds.Length==0){
            await this.Output.WriteLineAsync("All stages in the result already passed.").ConfigureAwait(false);
            return 0;
        }
        var Stages=StageIds.Select(this.Catalog.GetStage).ToArray();
        var Parsed=ParseOptions(Args,2,Previous.Profile);
        return await this.ExecuteAndStoreAsync(Stages,Parsed,CancellationToken).ConfigureAwait(false);
    }
    private async Task<int>ExecuteAndStoreAsync(IReadOnlyList<FeatureStageDefinition>Stages,ParsedExecutionOptions Parsed,CancellationToken CancellationToken){
        if(Stages.Count==0){ throw new ArgumentException("No stages matched the request."); }
        (string Commit,bool IsDirty)Provenance;
        try {
            Provenance=await GitChangedPathProvider.ReadProvenanceAsync(Parsed.RepositoryRoot,CancellationToken).ConfigureAwait(false);
        } catch(Exception Ex)when(Ex is not OperationCanceledException){
            Provenance=("unknown",true);
            await this.Error.WriteLineAsync($"Warning: git provenance is unavailable: {Ex.Message}").ConfigureAwait(false);
        }
        var Results=new List<FeatureStageRunResult>();
        foreach(var Stage in Stages){
            var Options=new FeatureStageExecutionOptions(Parsed.RepositoryRoot,Parsed.Configuration,Parsed.Profile,Parsed.Adapter,Parsed.Seed??Stage.Seed,Parsed.Ticks??Stage.Ticks,Provenance.Commit,Provenance.IsDirty);
            await this.Output.WriteLineAsync($"RUN {Stage.Id} [{Options.Configuration}/{Options.Profile}/{Options.Adapter}] seed={Options.Seed} ticks={Options.Ticks}").ConfigureAwait(false);
            FeatureStageRunResult Result;
            try {
                Result=await this.Executor.ExecuteAsync(Stage,Options,CancellationToken).ConfigureAwait(false);
            } catch(Exception Ex)when(Ex is not OperationCanceledException){
                Result=FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.InfrastructureError,"Executor failed before producing a result.",[],null,null,[Ex.ToString()]);
            }
            Results.Add(Result);
            await this.Output.WriteLineAsync($"{Result.Status} {Stage.Id}: {Result.Summary}").ConfigureAwait(false);
        }
        var Document=FeatureRunResultDocument.Create(Parsed.Profile,Results);
        var ResultPath=await this.ResultStore.SaveAsync(Document,CancellationToken).ConfigureAwait(false);
        await this.Output.WriteLineAsync($"Result: {ResultPath}").ConfigureAwait(false);
        return ExitCode(Results);
    }
    private static bool MatchesChangedPath(FeatureStageDefinition Stage,IReadOnlyList<string>ChangedPaths)=>ChangedPaths.Any(PathValue=>Stage.InputPaths.Any(Prefix=>PathValue.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase)));
    private static int ExitCode(IReadOnlyList<FeatureStageRunResult>Results){
        if(Results.All(static Result=>Result.Status==FeatureStageRunStatus.Passed)){ return 0; }
        if(Results.Any(static Result=>Result.Status==FeatureStageRunStatus.TimedOut)){ return 4; }
        if(Results.All(static Result=>Result.Status is FeatureStageRunStatus.Passed or FeatureStageRunStatus.NotImplemented)){ return 3; }
        return 1;
    }
    private static ParsedExecutionOptions ParseOptions(string[]Args,int StartIndex,string DefaultProfile){
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Configuration="Debug";
        var Profile=DefaultProfile;
        var Adapter=FeatureAdapter.Warp;
        uint?Seed=null;
        int?Ticks=null;
        for(var Index=StartIndex;Index<Args.Length;Index++){
            var Argument=Args[Index];
            if(Argument is "--json"){ continue; }
            if(Index+1>=Args.Length){ throw new ArgumentException($"Missing value for {Argument}."); }
            var Value=Args[++Index];
            switch(Argument.ToLowerInvariant()){
                case "--configuration":
                    if(Value is not("Debug" or "Release")){ throw new ArgumentException("Configuration must be Debug or Release."); }
                    Configuration=Value;
                    break;
                case "--profile":
                    Profile=Value;
                    break;
                case "--adapter":
                    if(!Enum.TryParse(Value,true,out Adapter)){ throw new ArgumentException("Adapter must be warp or hardware."); }
                    break;
                case "--seed":
                    if(!uint.TryParse(Value,out var ParsedSeed)||ParsedSeed==0){ throw new FormatException("Seed must be a positive UInt32."); }
                    Seed=ParsedSeed;
                    break;
                case "--ticks":
                    if(!int.TryParse(Value,out var ParsedTicks)||ParsedTicks<=0){ throw new FormatException("Ticks must be a positive Int32."); }
                    Ticks=ParsedTicks;
                    break;
                case "--repository-root":
                    RepositoryRoot=FeatureStageRepositoryLocator.Find(Value);
                    break;
                default:
                    throw new ArgumentException($"Unknown option: {Argument}");
            }
        }
        return new ParsedExecutionOptions(RepositoryRoot,Configuration,Profile,Adapter,Seed,Ticks);
    }
    private static bool IsHelp(string Argument)=>Argument is "help" or "--help" or "-h";
    private int UnknownCommand(string Command){
        this.Error.WriteLine($"Unknown command: {Command}");
        this.WriteHelp();
        return 2;
    }
    private void WriteHelp(){
        this.Output.WriteLine("C# Game Engine Feature Runner");
        this.Output.WriteLine("Usage:");
        this.Output.WriteLine("  list [--json]");
        this.Output.WriteLine("  run <stage-id> [options]");
        this.Output.WriteLine("  run-feature <feature-id> [options]");
        this.Output.WriteLine("  suite <changed|smoke|full|stress|vsix> [options]");
        this.Output.WriteLine("  resume <run-result.json> [options]");
        this.Output.WriteLine("Options: --configuration Debug|Release --profile <name> --adapter warp|hardware --seed <uint> --ticks <int> --repository-root <path>");
        this.Output.WriteLine("Visual certification uses WARP by default; hardware is an optional additional adapter pass.");
    }
    private sealed record ParsedExecutionOptions(string RepositoryRoot,string Configuration,string Profile,FeatureAdapter Adapter,uint?Seed,int?Ticks);
}
