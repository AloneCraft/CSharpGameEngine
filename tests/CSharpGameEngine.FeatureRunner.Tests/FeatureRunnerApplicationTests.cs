using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class FeatureRunnerApplicationTests {
    [Fact]
    public async Task ListJson_WritesTheCanonicalCatalog(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Output=new StringWriter();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),Output,new StringWriter());
        var ExitCode=await Application.RunAsync(["list","--json"],CancellationToken.None);
        Assert.Equal(0,ExitCode);
        Assert.Contains("csharp-game-engine-feature-catalog-v2",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("F-L7-021",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("F-L7-022",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("dependencyLevel",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("dependsOnFeatureIds",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("Rendering",Output.ToString(),StringComparison.Ordinal);
        Assert.Contains("rigid.physics",Output.ToString(),StringComparison.Ordinal);
        Assert.Empty(Executor.ExecutedStageIds);
    }
    /// <summary>
    /// 結果は各ステージを実行したリポジトリに属する。リリース時の証跡確認はそこで結果を探す。以前はカレントディレクトリ周辺のリポジトリに保存していた。
    /// </summary>
    [Fact]
    public void ResultsRepository_IsTheRepositoryRootTheCommandNames(){
        string Other=Path.Combine(Path.GetTempPath(),"cge-runner-root-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(Other,"src"));
        File.WriteAllText(Path.Combine(Other,"CSharpGameEngine.slnx"),"<Solution />");
        try {
            Assert.Equal(Other,FeatureRunnerApplication.ResultsRepositoryRoot(
                ["run","ecs.performance","--configuration","Release","--repository-root",Path.Combine(Other,"src")]));
            Assert.Equal(FeatureStageRepositoryLocator.Find(),FeatureRunnerApplication.ResultsRepositoryRoot(["run","ecs.performance"]));
        } finally {
            Directory.Delete(Other,true);
        }
    }
    [Fact]
    public async Task RunFeature_ExecutesItsStageAndPersistsAResult(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["run-feature","F-L7-021"],CancellationToken.None);
        Assert.Equal(0,ExitCode);
        Assert.Contains("runner.contract",Executor.ExecutedStageIds);
        var Document=Assert.Single(Store.Documents);
        Assert.All(Document.Results,static Result=>Assert.Equal(FeatureStageRunStatus.Passed,Result.Status));
    }
    [Fact]
    public async Task RunDeferredFeature_RecordsNotImplementedAndReturnsNonzero(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["run-feature","F-L5-004"],CancellationToken.None);
        Assert.Equal(3,ExitCode);
        var Document=Assert.Single(Store.Documents);
        Assert.Equal(FeatureStageRunStatus.NotImplemented,Assert.Single(Document.Results).Status);
    }
    [Fact]
    public async Task RunRenderingPerformanceFeature_ExecutesItsStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["run-feature","F-L7-023"],CancellationToken.None);
        Assert.Equal(0,ExitCode);
        var Document=Assert.Single(Store.Documents);
        Assert.Equal(FeatureStageRunStatus.Passed,Assert.Single(Document.Results).Status);
        Assert.Contains("rendering.performance",Executor.ExecutedStageIds,StringComparer.Ordinal);
    }
    [Fact]
    public async Task FullSuite_ExecutesImplementedAndExcludesSpecifiedStages(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["suite","full"],CancellationToken.None);
        Assert.Equal(0,ExitCode);
        Assert.Contains("rendering.baseline",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.Contains("vsix.rendering-preview",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.Contains("content.identity",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.Contains("content.incremental-cook",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.Contains("content.cache-recovery",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.All(Assert.Single(Store.Documents).Results,static Result=>Assert.Equal(FeatureStageRunStatus.Passed,Result.Status));
    }
    [Fact]
    public async Task VisualSuiteExecutesEveryAndOnlyImplementedVisualStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());

        var ExitCode=await Application.RunAsync(["suite","visual","--adapter","warp"],CancellationToken.None);

        Assert.Equal(0,ExitCode);
        string[]Expected=[.. Catalog.Stages.Where(static Stage=>Stage.Availability==FeatureStageAvailability.Executable&&Stage.Mode==FeatureStageMode.Visual).Select(static Stage=>Stage.Id)];
        Assert.Equal(Expected,Executor.ExecutedStageIds);
        Assert.All(Expected,StageId=>Assert.Equal(FeatureAdapterRequirement.WarpRequiredHardwareOptional,Catalog.GetStage(StageId).AdapterRequirement));
    }
    [Fact]
    public async Task VerificationCommandsValidateTheRepositoryContracts(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Output=new StringWriter();
        var Error=new StringWriter();
        var Application=new FeatureRunnerApplication(Catalog,new FakeExecutor(),new RecordingStore(),new EmptyChangedPathProvider(),Output,Error);
        var CoverageExitCode=await Application.RunAsync(["verify-coverage"],CancellationToken.None);
        var LedgerExitCode=await Application.RunAsync(["verify-manual-ledger"],CancellationToken.None);
        var CompatibilityExitCode=await Application.RunAsync(["verify-vsix-compatibility","--configuration","Debug"],CancellationToken.None);
        Assert.Equal(0,CoverageExitCode);
        Assert.Equal(0,LedgerExitCode);
        Assert.Equal(0,CompatibilityExitCode);
        Assert.Contains("verification passed",Output.ToString(),StringComparison.Ordinal);
        Assert.Empty(Error.ToString());
    }
    private sealed class FakeExecutor:IFeatureStageExecutor {
        internal List<string>ExecutedStageIds{get;}=[];
        public Task<FeatureStageRunResult>ExecuteAsync(FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,CancellationToken CancellationToken){
            this.ExecutedStageIds.Add(Stage.Id);
            var Status=Stage.Availability==FeatureStageAvailability.NotImplemented?FeatureStageRunStatus.NotImplemented:FeatureStageRunStatus.Passed;
            return Task.FromResult(FeatureStageRunResult.Create(Stage,Options,Status,Status.ToString(),[],"state-hash",null));
        }
    }
    private sealed class RecordingStore:IFeatureRunResultStore {
        internal List<FeatureRunResultDocument>Documents{get;}=[];
        public Task<string>SaveAsync(FeatureRunResultDocument Document,CancellationToken CancellationToken){
            this.Documents.Add(Document);
            return Task.FromResult("run-result.json");
        }
        public Task<FeatureRunResultDocument>LoadAsync(string Path,CancellationToken CancellationToken)=>throw new NotSupportedException();
    }
    private sealed class EmptyChangedPathProvider:IChangedPathProvider {
        public Task<IReadOnlyList<string>>GetChangedPathsAsync(string RepositoryRoot,CancellationToken CancellationToken)=>Task.FromResult<IReadOnlyList<string>>([]);
    }
}
