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
    public void NonPhysicsRoadmapFeatures_AreSpecifiedWithMetadataAndOracles(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Features=Catalog.Features.Where(Feature=>FeatureStageCatalog.NonPhysicsRoadmapFeatureIds.Contains(Feature.Id,StringComparer.Ordinal)).ToArray();
        Assert.Equal(63,Features.Length);
        Assert.Equal(63,Features.Select(static Feature=>Feature.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Features,static Feature=>Assert.NotEqual(FeatureRoadmapTrack.Established,Feature.Track));
        Assert.All(Features,static Feature=>Assert.InRange(Feature.DependencyLevel,1,7));
        Assert.Equal(FeatureLifecycleStatus.Implemented,Assert.Single(Features,static Feature=>Feature.Id=="F-L3-018").Status);
        Assert.Equal(FeatureLifecycleStatus.Implemented,Assert.Single(Features,static Feature=>Feature.Id=="F-L7-030").Status);
        Assert.All(Features.Where(static Feature=>Feature.Id is not "F-L3-018" and not "F-L7-030"),static Feature=>Assert.Equal(FeatureLifecycleStatus.Specified,Feature.Status));
        foreach(var Feature in Features){
            var ExpectedAvailability=Feature.Id is "F-L3-018" or "F-L7-030"?FeatureStageAvailability.Executable:FeatureStageAvailability.NotImplemented;
            var Stages=Catalog.Stages.Where(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)&&Stage.Availability==ExpectedAvailability).ToArray();
            Assert.NotEmpty(Stages);
            Assert.All(Stages,static Stage=>{
                Assert.False(string.IsNullOrWhiteSpace(Stage.Oracle));
                Assert.NotEmpty(Stage.InputPaths);
            });
        }
        var TargetStageIds=Catalog.Stages.Where(Stage=>Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.FeatureIds.Any(FeatureId=>FeatureStageCatalog.NonPhysicsRoadmapFeatureIds.Contains(FeatureId,StringComparer.Ordinal))).Select(static Stage=>Stage.Id).ToArray();
        Assert.Equal(61,TargetStageIds.Length);
        Assert.Equal(59,TargetStageIds.Except(["foundation.d3d12-compute","foundation.deterministic-jobs"],StringComparer.Ordinal).Count());
        Assert.All(["ecs.baseline","ecs.fixed-tick"],StageId=>Assert.Equal(FeatureStageAvailability.Executable,Assert.Single(Catalog.Stages,Stage=>Stage.Id==StageId).Availability));
    }
    [Fact]
    public void RoadmapDependencies_AreKnownAcyclicAndLevelOrdered(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        foreach(var Feature in Catalog.Features){
            Assert.DoesNotContain(Feature.Id,Feature.DependsOnFeatureIds,StringComparer.Ordinal);
            foreach(var DependencyId in Feature.DependsOnFeatureIds){
                var Dependency=Assert.Single(Catalog.Features,Other=>Other.Id==DependencyId);
                Assert.True(Dependency.DependencyLevel<=Feature.DependencyLevel,$"{Feature.Id} depends on higher-level {DependencyId}.");
            }
        }
        Assert.DoesNotContain(Catalog.Validate(),static Error=>Error.Contains("dependency",StringComparison.OrdinalIgnoreCase)||Error.Contains("cycle",StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void NonPhysicsRoadmapLedger_MatchesTheCatalog(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var RepositoryRoot=FeatureStageRepositoryLocator.Find();
        var Ledger=File.ReadAllText(Path.Combine(RepositoryRoot,"Docs","Implementation-Status.md"));
        foreach(var FeatureId in FeatureStageCatalog.NonPhysicsRoadmapFeatureIds){
            var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id==FeatureId);
            var Match=System.Text.RegularExpressions.Regex.Match(Ledger,$@"(?ms)^## {System.Text.RegularExpressions.Regex.Escape(Feature.Id)}\b.*?(?=^## |\z)");
            Assert.True(Match.Success,$"Missing ledger entry for {Feature.Id}.");
            var Section=Match.Value;
            Assert.Contains($"- Status: {Feature.Status}",Section,StringComparison.Ordinal);
            Assert.Contains("- Intended specification:",Section,StringComparison.Ordinal);
            Assert.Contains("- Stage:",Section,StringComparison.Ordinal);
            Assert.Contains("- Depends on:",Section,StringComparison.Ordinal);
            Assert.Contains("- Next action:",Section,StringComparison.Ordinal);
        }
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
