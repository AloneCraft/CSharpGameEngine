using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ChangedStageSelectorTests {
    [Theory]
    [InlineData("src/CSharpGameEngine.Rendering/EnvironmentLighting.cs","rendering.environment-lighting")]
    [InlineData("src/CSharpGameEngine.Rendering/View/RenderCamera.cs","rendering.radial-depth")]
    [InlineData("src/CSharpGameEngine.Rendering/Pipeline/PhysicalLighting.cs","rendering.environment-lighting")]
    [InlineData("src/CSharpGameEngine.Rendering/StageRenderInputs.cs","rendering.materials")]
    [InlineData("src/CSharpGameEngine.Entities/SpatialPartitioning.cs","spatial.runtime")]
    public void SelectFollowsRelocatedRenderingAndActualSpatialInputs(string Path,string ExpectedStage){
        var Stages=ChangedStageSelector.Select(FeatureStageCatalog.CreateDefault(),[Path]);
        Assert.Contains(Stages,Stage=>Stage.Id==ExpectedStage);
    }
    [Fact]
    public void SelectUsesPathBoundariesAndReturnsOnlyExecutableChangedProfileStages(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();

        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            Catalog,["src/CSharpGameEngine.Audio/AudioMixer.cs"]);

        Assert.Contains(Selected,Stage=>Stage.Id=="audio.mixer");
        Assert.Contains(Selected,Stage=>Stage.Id=="audio.spatial");
        Assert.All(Selected,Stage=>{
            Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
            Assert.Contains("changed",Stage.Profiles,StringComparer.OrdinalIgnoreCase);
        });
        IReadOnlyList<FeatureStageDefinition>NearMiss=ChangedStageSelector.Select(
            Catalog,["src/CSharpGameEngine.AudioExtra/AudioMixer.cs"]);
        Assert.Equal("runner.contract",Assert.Single(NearMiss).Id);
    }

    [Fact]
    public void GlobalBuildInputSelectsEveryChangedProfileButNeverVsixOnlyStage(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();

        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            Catalog,["Directory.Build.targets"]);

        FeatureStageDefinition[]Expected=[.. Catalog.Stages
            .Where(Stage=>Stage.Availability==FeatureStageAvailability.Executable)
            .Where(Stage=>Stage.Profiles.Contains("changed",StringComparer.OrdinalIgnoreCase))];
        Assert.Equal(Expected.Select(Stage=>Stage.Id),Selected.Select(Stage=>Stage.Id));
        Assert.DoesNotContain(Selected,Stage=>
            Stage.Profiles.Contains("vsix",StringComparer.OrdinalIgnoreCase)
            &&!Stage.Profiles.Contains("changed",StringComparer.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("tools/CSharpGameEngine.VSExtension/PhysicsSceneBakeService.cs")]
    [InlineData("tools/CSharpGameEngine.VSExtension/PrefractureDebugService.cs")]
    [InlineData("tests/CSharpGameEngine.VSExtension.Tests/PhysicsDebugOverlayTests.cs")]
    public void PhysicsAuthoringChangesSelectRigidPhysics(string ChangedPath){
        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            FeatureStageCatalog.CreateDefault(),[ChangedPath]);

        Assert.Contains(Selected,Stage=>Stage.Id=="rigid.physics");
    }

    [Theory]
    [InlineData("src/CSharpGameEngine.Rendering/RenderDeviceLifecycle.cs")]
    [InlineData("src/CSharpGameEngine.Windows/Runtime/RenderServer.cs")]
    [InlineData("tests/CSharpGameEngine.Windows.Tests/Runtime/RenderServerTests.cs")]
    public void RenderRecoveryChangesSelectDeviceRecovery(string ChangedPath){
        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            FeatureStageCatalog.CreateDefault(),[ChangedPath]);

        Assert.Contains(Selected,Stage=>Stage.Id=="rendering.device-recovery");
    }

    [Fact]
    public void GitPorcelainParserPreservesBothSidesOfRenameAndLiteralSpaces(){
        string Separator=((char)0).ToString();
        string Porcelain=string.Join(Separator,[
            "R  new folder/new.cs","old folder/old.cs"," M src/Test.cs","?? untracked file.cs","",
        ]);

        IReadOnlyList<string>Paths=GitChangedPathProvider.ParsePorcelainZ(Porcelain);

        Assert.Equal([
            "new folder/new.cs","old folder/old.cs","src/Test.cs","untracked file.cs",
        ],Paths);
    }
}
