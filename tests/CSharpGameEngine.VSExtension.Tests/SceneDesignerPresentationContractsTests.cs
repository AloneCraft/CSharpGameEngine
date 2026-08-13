namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerPresentationContractsTests {
    [Fact]
    public void CatalogEntry_DisplayName_IsPublicForWpfDisplayMemberBinding(){
        Assert.NotNull(typeof(ProjectSceneCatalogEntry).GetProperty(nameof(ProjectSceneCatalogEntry.DisplayName)));
    }

    [Fact]
    public void EmptyAndUnavailableStates_DoNotAttachNativeHost(){
        Assert.False(SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.Empty));
        Assert.False(SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.SceneUnavailable));
        Assert.True(SceneDesignerHostAttachmentPolicy.ShouldAttach(SceneDesignerHostState.SceneReady));
    }

    [Fact]
    public void ProjectCatalog_ContainsOnlyCurrentProject_InStableDeclarationOrder_AndDisambiguatesDuplicates(){
        ProjectSceneCatalogEntry[] Entries=[
            new(new SceneKey("current","global::Game.Beta"),"Beta",@"G:\Project\B.cs",20),
            new(new SceneKey("other","global::Other.Hidden"),"Hidden",@"G:\Other\Hidden.cs",0),
            new(new SceneKey("current","global::Game.Alpha"),"Alpha",@"G:\Project\A.cs",30),
            new(new SceneKey("current","global::Second.Alpha"),"Alpha",@"G:\Project\A.cs",10),
        ];

        IReadOnlyList<ProjectSceneCatalogEntry> Actual=ProjectSceneCatalogPresentation.Create("current",Entries);

        Assert.Collection(Actual,
            Entry=>{
                Assert.Equal("global::Second.Alpha",Entry.Key.ClassFullyQualifiedName);
                Assert.Equal("Second.Alpha",Entry.DisplayName);
            },
            Entry=>{
                Assert.Equal("global::Game.Alpha",Entry.Key.ClassFullyQualifiedName);
                Assert.Equal("Game.Alpha",Entry.DisplayName);
            },
            Entry=>Assert.Equal("Beta",Entry.DisplayName));
    }
}
