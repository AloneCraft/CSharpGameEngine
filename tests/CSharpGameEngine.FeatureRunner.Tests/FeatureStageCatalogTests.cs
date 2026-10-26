using CSharpGameEngine.FeatureStages;
namespace CSharpGameEngine.FeatureRunner.Tests;
public sealed class FeatureStageCatalogTests {
    [Fact]
    public void EveryStageInputPathExists(){
        var Root=FeatureStageRepositoryLocator.Find();
        foreach(var Stage in FeatureStageCatalog.CreateDefault().Stages){
            foreach(var Input in Stage.InputPaths){
                var FullPath=Path.Combine(Root,Input.Replace('/',Path.DirectorySeparatorChar));
                Assert.True(File.Exists(FullPath)||Directory.Exists(FullPath),$"{Stage.Id}: {Input}");
            }
        }
    }
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
    public void ExtendedEcsMaskIsRequiredForThePlanetaryWorldRuntime(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id==FeatureStageCatalog.ExtendedEcsMaskFeatureId);
        Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
        Assert.Contains(Catalog.Stages,Stage=>Stage.Availability==FeatureStageAvailability.Executable&&Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal));
    }
    [Fact]
    public void PlanetaryWorldFoundationHasAnExplicitDependencyOrderedTrack(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var ExpectedFeatureIds=new[]{"F-L1-009","F-L2-006","F-L4-013","F-L5-036","F-L6-008","F-L5-037","F-L5-023","F-L5-027"};
        foreach(var FeatureId in ExpectedFeatureIds){
            var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id==FeatureId);
            Assert.Equal(FeatureRoadmapTrack.WorldScale,Feature.Track);
            bool IsImplemented=FeatureId is "F-L1-009" or "F-L2-006" or "F-L4-013" or "F-L5-036" or "F-L6-008" or "F-L5-037" or "F-L5-023" or "F-L5-027";
            FeatureLifecycleStatus ExpectedStatus=IsImplemented?FeatureLifecycleStatus.Implemented:FeatureLifecycleStatus.Specified;
            FeatureStageAvailability ExpectedAvailability=ExpectedStatus is FeatureLifecycleStatus.Implemented or FeatureLifecycleStatus.PartiallyImplemented
                ?FeatureStageAvailability.Executable
                :FeatureStageAvailability.NotImplemented;
            Assert.Equal(ExpectedStatus,Feature.Status);
            Assert.Contains(Catalog.Stages,Stage=>Stage.Availability==ExpectedAvailability&&Stage.FeatureIds.Contains(FeatureId,StringComparer.Ordinal));
        }
        Assert.DoesNotContain("F-L5-023",FeatureStageCatalog.DeferredSimulationFeatureIds,StringComparer.Ordinal);
        Assert.DoesNotContain("F-L5-027",FeatureStageCatalog.DeferredSimulationFeatureIds,StringComparer.Ordinal);
    }
    [Fact]
    public void LevelOneVerificationFeaturesHaveExecutableStages(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        foreach(var FeatureId in new[]{"F-L7-036","F-L7-037"}){
            var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id==FeatureId);
            Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
            var Stage=Assert.Single(Catalog.Stages,Stage=>Stage.FeatureIds.Contains(FeatureId,StringComparer.Ordinal));
            Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
            Assert.NotEmpty(Stage.Commands);
        }
    }
    [Fact]
    public void SharedRenderingPreviewRunsThePackagedWarpHostAndCommonRenderServerOracles(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id=="F-L8-018");
        Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
        var Stage=Assert.Single(Catalog.Stages,Stage=>Stage.Id=="vsix.rendering-preview");
        Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
        Assert.Equal(FeatureStageMode.Visual,Stage.Mode);
        Assert.Equal(FeatureAdapterRequirement.WarpRequiredHardwareOptional,Stage.AdapterRequirement);
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Contains(Stage.SceneProject!,StringComparer.Ordinal)&&Command.Arguments.Contains(Stage.SceneId!,StringComparer.Ordinal));
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Any(Argument=>Argument.Contains("PackagedRenderPreviewHostTests",StringComparison.Ordinal)));
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Any(Argument=>Argument.Contains("RenderPreviewHostClientTests",StringComparison.Ordinal)));
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Any(Argument=>Argument.Contains("RenderServerTests",StringComparison.Ordinal)));
    }
    [Fact]
    public void TypedInspectorAndComponentPaletteHaveAnExecutableContractStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id=="F-L8-024");
        Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
        var Stage=Assert.Single(Catalog.Stages,Stage=>Stage.Id=="vsix.inspector-contracts");
        Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
        Assert.Equal(FeatureStageMode.Headless,Stage.Mode);
        var Command=Assert.Single(Stage.Commands);
        Assert.Contains(Command.Arguments,Argument=>Argument.Contains("SceneInspectorModelTests",StringComparison.Ordinal));
        Assert.Contains(Command.Arguments,Argument=>Argument.Contains("HierarchyComponentMutationTests",StringComparison.Ordinal));
        Assert.Contains(Command.Arguments,Argument=>Argument.Contains("ComponentPaletteFilterPolicyTests",StringComparison.Ordinal));
        Assert.Contains(Command.Arguments,Argument=>Argument.Contains("SceneJsonCommitPlannerTests",StringComparison.Ordinal));
    }
    [Fact]
    public void SourceSynchronizationHasAnExecutableSourceTruthStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id=="F-L8-025");
        Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
        var Stage=Assert.Single(Catalog.Stages,Stage=>Stage.Id=="vsix.source-sync");
        Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
        Assert.Equal(FeatureStageMode.Headless,Stage.Mode);
        var Command=Assert.Single(Stage.Commands);
        Assert.Contains(Command.Arguments,Argument=>
            Argument.Contains("PendingSourceLandingTests",StringComparison.Ordinal)
            &&Argument.Contains("SceneJsonCommitPlannerTests",StringComparison.Ordinal)
            &&Argument.Contains("SceneSourceTruthArchitectureTests",StringComparison.Ordinal)
            &&Argument.Contains("SceneEditConflictMergerTests",StringComparison.Ordinal)
            &&Argument.Contains("SceneUndoRoutingPolicyTests",StringComparison.Ordinal));
    }
    [Fact]
    public void TransformToolsHaveAnExecutableVisualConformanceStage(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Feature=Assert.Single(Catalog.Features,Feature=>Feature.Id=="F-L8-023");
        Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status);
        var Stage=Assert.Single(Catalog.Stages,Stage=>Stage.Id=="vsix.transform-tools");
        Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
        Assert.Equal(FeatureStageMode.Visual,Stage.Mode);
        Assert.Equal(FeatureAdapterRequirement.WarpRequiredHardwareOptional,Stage.AdapterRequirement);
        Assert.Contains(Stage.Commands,Command=>
            Command.Arguments.Contains(Stage.SceneProject!,StringComparer.Ordinal)
            &&Command.Arguments.Contains(Stage.SceneId!,StringComparer.Ordinal));
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Any(Argument=>
            Argument.Contains("TransformGizmoTests",StringComparison.Ordinal)
            &&Argument.Contains("EditTransactionControllerTests",StringComparison.Ordinal)
            &&Argument.Contains("SceneJsonCommitPlannerTests",StringComparison.Ordinal)));
        Assert.Contains(Stage.Commands,Command=>Command.Arguments.Any(Argument=>
            Argument.Contains("RenderPreviewRenderSessionTests",StringComparison.Ordinal)));
    }
    [Fact]
    public void NonPhysicsRoadmapFeatures_SupportImplementedAndSpecifiedMetadataAndOracles(){
        var Catalog=FeatureStageCatalog.CreateDefault();
        var Features=Catalog.Features.Where(Feature=>FeatureStageCatalog.NonPhysicsRoadmapFeatureIds.Contains(Feature.Id,StringComparer.Ordinal)).ToArray();
        Assert.Equal(74,Features.Length);
        Assert.Equal(74,Features.Select(static Feature=>Feature.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.All(Features,static Feature=>Assert.NotEqual(FeatureRoadmapTrack.Established,Feature.Track));
        Assert.All(Features,static Feature=>Assert.InRange(Feature.DependencyLevel,1,7));
        var ImplementedIds=new HashSet<string>(["F-L3-018","F-L3-019","F-L3-020","F-L7-030","F-L5-035","F-L3-021","F-L3-022","F-L7-031","F-L6-007","F-L7-022","F-L4-004","F-L0-008","F-L4-005","F-L4-006","F-L4-007","F-L4-014","F-L4-015","F-L4-020","F-L5-038","F-L5-039","F-L0-009","F-L8-018","F-L6-004","F-L2-004","F-L7-025","F-L6-005","F-L7-026","F-L7-027","F-L4-008","F-L6-006","F-L6-009","F-L8-019","F-L7-028","F-L7-029","F-L2-005","F-L7-049","F-L8-020","F-L8-021","F-L8-022","F-L8-023","F-L8-024","F-L8-025","F-L8-026","F-L8-028","F-L8-031","F-L8-032","F-L1-008","F-L0-010","F-L4-009","F-L4-010","F-L4-011","F-L4-012","F-L7-034","F-L7-035","F-L9-009","F-L7-036","F-L7-037","F-L7-038","F-L7-039","F-L7-040","F-L7-041","F-L7-042","F-L7-043","F-L7-044","F-L7-023","F-L7-024","F-L7-032","F-L7-033","F-L7-045","F-L7-046","F-L7-047","F-L7-048"],StringComparer.Ordinal);
        var ImplementingIds=new HashSet<string>(["F-L8-029"],StringComparer.Ordinal);
        var PartialIds=new HashSet<string>(["F-L8-035","F-L8-036"],StringComparer.Ordinal);
        Assert.All(Features.Where(Feature=>ImplementedIds.Contains(Feature.Id)),static Feature=>Assert.Equal(FeatureLifecycleStatus.Implemented,Feature.Status));
        Assert.All(Features.Where(Feature=>ImplementingIds.Contains(Feature.Id)),static Feature=>Assert.Equal(FeatureLifecycleStatus.Implementing,Feature.Status));
        Assert.All(Features.Where(Feature=>!ImplementedIds.Contains(Feature.Id)&&!ImplementingIds.Contains(Feature.Id)&&!PartialIds.Contains(Feature.Id)),static Feature=>Assert.Equal(FeatureLifecycleStatus.Specified,Feature.Status));
        Assert.All(Features.Where(Feature=>PartialIds.Contains(Feature.Id)),static Feature=>Assert.Equal(FeatureLifecycleStatus.PartiallyImplemented,Feature.Status));
        foreach(var Feature in Features){
            var ExpectedAvailability=ImplementedIds.Contains(Feature.Id)||ImplementingIds.Contains(Feature.Id)||PartialIds.Contains(Feature.Id)?FeatureStageAvailability.Executable:FeatureStageAvailability.NotImplemented;
            var Stages=Catalog.Stages.Where(Stage=>Stage.FeatureIds.Contains(Feature.Id,StringComparer.Ordinal)&&Stage.Availability==ExpectedAvailability).ToArray();
            Assert.NotEmpty(Stages);
            Assert.All(Stages,static Stage=>{
                Assert.False(string.IsNullOrWhiteSpace(Stage.Oracle));
                Assert.NotEmpty(Stage.InputPaths);
            });
        }
        var TargetStageIds=Catalog.Stages.Where(Stage=>Stage.Availability==FeatureStageAvailability.NotImplemented&&Stage.FeatureIds.Any(FeatureId=>FeatureStageCatalog.NonPhysicsRoadmapFeatureIds.Contains(FeatureId,StringComparer.Ordinal))).Select(static Stage=>Stage.Id).ToArray();
        Assert.Empty(TargetStageIds);
        Assert.All(["ecs.baseline","ecs.fixed-tick","ecs.runtime-boundary","ecs.structural-changes","ecs.change-tracking","ecs.schedule-diagnostics","ecs.snapshot","ecs.performance","ecs.release","foundation.deterministic-jobs","foundation.extended-ecs-mask","rendering.baseline","rendering.render-graph","foundation.d3d12-compute","rendering.shader-artifacts","rendering.materials","rendering.visibility","rendering.quality-profiles","rendering.environment-lighting","rendering.skeletal-animation","rendering.radial-depth","rendering.device-recovery","rendering.performance","rendering.release","vsix.rendering-preview","content.identity","content.importer-conformance","content.artifact-schema","content.incremental-cook","content.cache-recovery","content.runtime-load","content.package","vsix.asset-authoring","content.performance","content.release","shadergraph.schema","shadergraph.compiler","vsix.shader-graph","vsix.experimental-smoke","vsix.experimental-automation","vsix.host-lifecycle","vsix.transform-tools","vsix.project-template","vsix.inspector-contracts","vsix.source-sync","vsix.bake-integration","vsix.compatibility","vsix.release","platform.boundaries","platform.window-input","audio.lifecycle","input.actions","audio.mixer","audio.spatial","verification.changed-selection","verification.clean-build","verification.golden-policy","verification.performance","verification.ci-matrix","verification.failure-policy","verification.stress-contract","release.packages","release.compatibility","release.candidate","release.manifest","showcase.visible-progress"],StageId=>Assert.Equal(FeatureStageAvailability.Executable,Assert.Single(Catalog.Stages,Stage=>Stage.Id==StageId).Availability));
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
