using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneCatalogTargetPolicyTests {
    [Fact]
    public void PinnedSceneProject_WinsWhenNoActiveDocumentProjectExists(){
        SceneKey Pinned=new("scene-project","global::Game.MainScene");

        Assert.Equal("scene-project",SceneCatalogTargetPolicy.SelectProjectId(Pinned,ActiveProjectId:null));
    }

    [Fact]
    public void PinnedSceneProject_WinsOverAnotherActiveDocumentProject(){
        SceneKey Pinned=new("scene-project","global::Game.MainScene");

        Assert.Equal("scene-project",SceneCatalogTargetPolicy.SelectProjectId(Pinned,"active-project"));
    }

    [Fact]
    public void NoPinnedScene_FallsBackToTheActiveDocumentProject(){
        Assert.Equal("active-project",SceneCatalogTargetPolicy.SelectProjectId(PinnedSceneKey:null,"active-project"));
    }

    [Fact]
    public void MissingCatalog_DoesNotDiscardAnAlreadyPinnedScene(){
        Assert.True(SceneCatalogTargetPolicy.KeepsPresentedScene(HasPinnedScene:true,HasCatalog:false));
        Assert.False(SceneCatalogTargetPolicy.KeepsPresentedScene(HasPinnedScene:false,HasCatalog:false));
    }
}
