using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class ShowcaseManifestTests {
    [Fact]
    public async Task ExportShowcase_IsByteStableAndMatchesCatalog(){
        var Root=Path.Combine(Path.GetTempPath(),"CgeShowcaseManifest",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        try {
            var First=Path.Combine(Root,"first.json");
            var Second=Path.Combine(Root,"second.json");
            var Catalog=FeatureStageCatalog.CreateDefault();
            var Application=new FeatureRunnerApplication(Catalog,new NoopExecutor(),new NoopStore(),new EmptyChangedPathProvider(),new StringWriter(),new StringWriter());
            Assert.Equal(0,await Application.RunAsync(["export-showcase","--output",First],CancellationToken.None));
            Assert.Equal(0,await Application.RunAsync(["export-showcase","--output",Second],CancellationToken.None));
            Assert.Equal(await File.ReadAllBytesAsync(First),await File.ReadAllBytesAsync(Second));
            var Text=await File.ReadAllTextAsync(First);
            Assert.Contains("csharp-game-engine-showcase-manifest-v1",Text,StringComparison.Ordinal);
            Assert.Contains("manifestSha256",Text,StringComparison.Ordinal);
            Assert.All(Catalog.Features,Feature=>Assert.Contains(Feature.Id,Text,StringComparison.Ordinal));
            Assert.All(Catalog.Stages,Stage=>Assert.Contains(Stage.Id,Text,StringComparison.Ordinal));
        } finally {
            Directory.Delete(Root,true);
        }
    }
    private sealed class NoopExecutor:IFeatureStageExecutor {
        public Task<FeatureStageRunResult>ExecuteAsync(FeatureStageDefinition Stage,FeatureStageExecutionOptions Options,CancellationToken CancellationToken)=>throw new NotSupportedException();
    }
    private sealed class NoopStore:IFeatureRunResultStore {
        public Task<string>SaveAsync(FeatureRunResultDocument Document,CancellationToken CancellationToken)=>throw new NotSupportedException();
        public Task<FeatureRunResultDocument>LoadAsync(string Path,CancellationToken CancellationToken)=>throw new NotSupportedException();
    }
    private sealed class EmptyChangedPathProvider:IChangedPathProvider {
        public Task<IReadOnlyList<string>>GetChangedPathsAsync(string RepositoryRoot,CancellationToken CancellationToken)=>Task.FromResult<IReadOnlyList<string>>([]);
    }
}
