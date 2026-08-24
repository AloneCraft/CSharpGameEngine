using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class FeatureRunResultStoreTests {
    [Fact]
    public async Task SaveAndLoad_PreserveTheVersionedResultContract(){
        var DirectoryPath=Path.Combine(Path.GetTempPath(),"CSharpGameEngine.FeatureRunner.Tests",Guid.NewGuid().ToString("N"));
        try {
            var Catalog=FeatureStageCatalog.CreateDefault();
            var Stage=Catalog.GetStage("runner.contract");
            var Options=new FeatureStageExecutionOptions(DirectoryPath,"Release","full",FeatureAdapter.Warp,17,120);
            var Result=FeatureStageRunResult.Create(Stage,Options,FeatureStageRunStatus.Passed,"ok",[new FeatureStageAssertion("exit-code","0",true)],"state","event");
            var Document=FeatureRunResultDocument.Create("full",[Result]);
            var Store=new FeatureRunResultStore(DirectoryPath);
            var PathValue=await Store.SaveAsync(Document,CancellationToken.None);
            var Loaded=await Store.LoadAsync(PathValue,CancellationToken.None);
            Assert.Equal(FeatureRunResultDocument.CurrentSchemaVersion,Loaded.SchemaVersion);
            Assert.Equal("runner.contract",Assert.Single(Loaded.Results).StageId);
            Assert.False(string.IsNullOrWhiteSpace(Loaded.Results[0].ArtifactSha256));
        } finally {
            if(Directory.Exists(DirectoryPath)){
                Directory.Delete(DirectoryPath,true);
            }
        }
    }
}
