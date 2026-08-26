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
    public async Task RunSpecifiedFeature_RecordsNotImplementedAndReturnsNonzero(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["run-feature","F-L7-022"],CancellationToken.None);
        Assert.Equal(3,ExitCode);
        var Document=Assert.Single(Store.Documents);
        Assert.Equal(FeatureStageRunStatus.NotImplemented,Assert.Single(Document.Results).Status);
    }
    [Fact]
    public async Task FullSuite_ExcludesSpecifiedStages(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Executor=new FakeExecutor();
        var Store=new RecordingStore();
        var Application=new FeatureRunnerApplication(Catalog,Executor,Store,new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
        var ExitCode=await Application.RunAsync(["suite","full"],CancellationToken.None);
        Assert.Equal(0,ExitCode);
        Assert.DoesNotContain("rendering.baseline",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.DoesNotContain("content.identity",Executor.ExecutedStageIds,StringComparer.Ordinal);
        Assert.All(Assert.Single(Store.Documents).Results,static Result=>Assert.Equal(FeatureStageRunStatus.Passed,Result.Status));
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
