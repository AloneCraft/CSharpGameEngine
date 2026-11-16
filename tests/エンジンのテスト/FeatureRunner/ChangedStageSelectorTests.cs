using Xunit;
using CSharpGameEngine.FeatureStages;

namespace CSharpGameEngine.FeatureRunner.Tests;

public sealed class ChangedStageSelectorTests {
    [Theory]
    [InlineData("src/Engine/Rendering/EnvironmentLighting.cs","rendering.environment-lighting")]
    [InlineData("src/Engine/Rendering/View/RenderCamera.cs","rendering.radial-depth")]
    [InlineData("src/Engine/Rendering/Pipeline/StageGpuLayouts.cs","rendering.radial-depth")]
    [InlineData("src/Engine/Rendering/RenderView.cs","rendering.radial-depth")]
    [InlineData("src/Engine/Rendering/RenderViewSource.cs","rendering.radial-depth")]
    [InlineData("src/Engine/Rendering/View/RenderViewPolicy.cs","rendering.radial-depth")]
    [InlineData("src/Runtime/Windows/Camera.cs","rendering.radial-depth")]
    [InlineData("src/Runtime/Windows/PrimitiveRoster.cs","rendering.baseline")]
    [InlineData("src/Engine/Rendering/StageMeshGeometry.cs","rendering.baseline")]
    [InlineData("src/Engine/Rendering/StageRenderDefaults.cs","rendering.baseline")]
    [InlineData("src/Engine/Rendering/PixelReadbackResult.cs","rendering.baseline")]
    [InlineData("src/Engine/Rendering/Pipeline/PhysicalLighting.cs","rendering.environment-lighting")]
    [InlineData("src/Engine/Rendering/StageRenderInputs.cs","rendering.materials")]
    [InlineData("src/Entities/SpatialPartitioning.cs","spatial.runtime")]
    public void SelectFollowsRelocatedRenderingAndActualSpatialInputs(string Path,string ExpectedStage){
        var Stages=ChangedStageSelector.Select(FeatureStageCatalog.CreateDefault(),[Path]);
        Assert.Contains(Stages,Stage=>Stage.Id==ExpectedStage);
    }
    [Fact]
    public void SelectUsesPathBoundariesAndReturnsOnlyExecutableChangedProfileStages(){
        FeatureStageCatalog Catalog=FeatureStageCatalog.CreateDefault();

        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            Catalog,["src/Engine/Core/Audio/AudioMixer.cs"]);

        Assert.Contains(Selected,Stage=>Stage.Id=="audio.mixer");
        Assert.Contains(Selected,Stage=>Stage.Id=="audio.spatial");
        Assert.All(Selected,Stage=>{
            Assert.Equal(FeatureStageAvailability.Executable,Stage.Availability);
            Assert.Contains("changed",Stage.Profiles,StringComparer.OrdinalIgnoreCase);
        });
        IReadOnlyList<FeatureStageDefinition>NearMiss=ChangedStageSelector.Select(
            Catalog,["src/AudioExtra/AudioMixer.cs"]);
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
    [InlineData("tools/VSExtension/PhysicsSceneBakeService.cs")]
    [InlineData("tools/VSExtension/PrefractureDebugService.cs")]
    [InlineData("tests/VS拡張のテスト/PhysicsDebugOverlayTests.cs")]
    public void PhysicsAuthoringChangesSelectRigidPhysics(string ChangedPath){
        IReadOnlyList<FeatureStageDefinition>Selected=ChangedStageSelector.Select(
            FeatureStageCatalog.CreateDefault(),[ChangedPath]);

        Assert.Contains(Selected,Stage=>Stage.Id=="rigid.physics");
    }

    [Theory]
    [InlineData("src/Engine/Rendering/RenderDeviceLifecycle.cs")]
    [InlineData("src/Engine/Rendering/Backend/RenderBackendFailureException.cs")]
    [InlineData("src/Engine/Rendering/Server/RenderServer.cs")]
    [InlineData("src/Windows/Graphics/D3D12RenderServer.cs")]
    [InlineData("tests/Windowsのテスト/Windows/Runtime/RenderServerTests.cs")]
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
