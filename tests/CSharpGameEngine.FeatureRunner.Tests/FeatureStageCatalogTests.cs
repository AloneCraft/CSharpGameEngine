using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class FeatureStageCatalogTests {
    [Fact]
    public void DefaultCatalog_IsInternallyConsistent(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        Assert.Empty(Catalog.Validate());
        Assert.Equal(Catalog.Features.Count,Catalog.Features.Select(static Feature=>Feature.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Catalog.Stages.Count,Catalog.Stages.Select(static Stage=>Stage.Id).Distinct(StringComparer.Ordinal).Count());
    }
    [Fact]
    public void EveryExistingFeatureHasAnExecutableAutomatedStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        foreach(var FeatureId in FeatureStageCatalog.ExistingFeatureIds){
            Assert.Contains(Catalog.Stages,Stage=>Stage.Availability==FeatureStageAvailability.Executable&&Stage.FeatureIds.Contains(FeatureId,StringComparer.Ordinal));
        }
    }
    [Fact]
    public void EveryDeferredSimulationFeatureHasAnExplicitNotImplementedStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        foreach(var FeatureId in FeatureStageCatalog.DeferredSimulationFeatureIds){
            Assert.Contains(Catalog.Stages,Stage=>Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.FeatureIds.Contains(FeatureId,StringComparer.Ordinal));
        }
    }
    [Fact]
    public void ExtendedEcsMaskRemainsADeferredFeature(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id==FeatureStageCatalog.ExtendedEcsMaskFeatureId);
        Assert.Equal(FeatureLifecycleStatus.Deferred,Feature.Status);
        Assert.Contains(Catalog.Stages,Stage=>Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal));
    }
    [Fact]
    public void EveryVisualStageRequiresWarpAndAllowsOptionalHardware(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var VisualStages=Catalog.Stages.Where(static Stage=>Stage.Mode==FeatureStageMode.Visual).ToArray();
        Assert.NotEmpty(VisualStages);
        Assert.All(VisualStages,static Stage=>Assert.Equal(FeatureAdapterRequirement.WarpRequiredHardwareOptional,Stage.AdapterRequirement));
    }
    [Fact]
    public void EveryVisualStageBindsToACSharpScenePack(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        foreach(var Stage in Catalog.Stages.Where(static Stage=>Stage.Mode==FeatureStageMode.Visual)){
            Assert.False(string.IsNullOrWhiteSpace(Stage.SceneProject));
            Assert.False(string.IsNullOrWhiteSpace(Stage.SceneId));
            var ProjectPath=Path.Combine(RepositoryRoot,Stage.SceneProject.Replace('/',Path.DirectorySeparatorChar));
            Assert.True(File.Exists(ProjectPath),Stage.SceneProject);
            var ProgramPath=Path.Combine(Path.GetDirectoryName(ProjectPath)!,"Program.cs");
            Assert.Contains($"\"{Stage.SceneId}\"",File.ReadAllText(ProgramPath),StringComparison.Ordinal);
        }
    }
    [Fact]
    public void EveryExecutableVisualStageRunsItsBoundScene(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        foreach(var Stage in Catalog.Stages.Where(static Stage=>Stage.Mode==FeatureStageMode.Visual&&Stage.Availability==FeatureStageAvailability.Executable)){
            Assert.Contains(Stage.Commands,Command=>Command.Arguments.Contains(Stage.SceneProject!,StringComparer.Ordinal)&&Command.Arguments.Contains(Stage.SceneId!,StringComparer.Ordinal));
        }
    }
}
