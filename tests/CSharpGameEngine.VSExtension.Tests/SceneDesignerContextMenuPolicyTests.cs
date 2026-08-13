using System.Linq;
using CSharpGameEngine.VSExtension;
using Xunit;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneDesignerContextMenuPolicyTests {
    [Fact]
    public void SceneAtRightClickPosition_IsTheOnlyLatchedSubmenuItem(){
        SceneDesignerMenuItem Box=new("PrimitiveShowcaseBoxScene","Project.PrimitiveShowcaseBoxScene");
        SceneDesignerMenuItem Sphere=new("PrimitiveShowcaseSphereScene","Project.PrimitiveShowcaseSphereScene");

        Assert.True(SceneDesignerMenuSelectionPolicy.IsLatched("Project.PrimitiveShowcaseBoxScene",Box));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched("Project.PrimitiveShowcaseBoxScene",Sphere));
        Assert.False(SceneDesignerMenuSelectionPolicy.IsLatched(null,Box));
    }

    [Fact]
    public void NonSceneType_IsDisabledWithAnInlineReason(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Components.cs",
            SceneDesignerTargetState.NonSceneType,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Disabled,Presentation.Mode);
        Assert.Contains("IScene",Presentation.Text);
    }

    [Fact]
    public void PositionOutsideAType_IsDisabledWithAnInlineReason(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Scenes.cs",
            SceneDesignerTargetState.OutsideType,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Contains("ありません",Presentation.Text);
    }

    [Fact]
    public void PositionOutsideAType_InASceneFileUsesTheSceneSelector(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\PrimitiveShowcaseScene.cs",
            SceneDesignerTargetState.OutsideType,
            [
                new SceneDesignerMenuItem("BoxScene","Project.BoxScene"),
                new SceneDesignerMenuItem("SphereScene","Project.SphereScene"),
            ]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Submenu,Presentation.Mode);
    }
    [Fact]
    public void NonCSharpDocument_IsHidden(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\Readme.md",
            SceneDesignerTargetState.SceneType,
            [new SceneDesignerMenuItem("MainScene","Project.MainScene")]);

        Assert.False(Presentation.Visible);
        Assert.Equal(SceneDesignerMenuMode.Hidden,Presentation.Mode);
    }

    [Fact]
    public void OneScene_UsesTheDirectCommand(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\MainScene.cs",
            SceneDesignerTargetState.SceneType,
            [new SceneDesignerMenuItem("MainScene","Project.MainScene")]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Direct,Presentation.Mode);
        Assert.Equal("シーン デザイナーで開く",Presentation.Text);
    }

    [Fact]
    public void MultipleScenes_UsesASubmenuInDeclarationOrder(){
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\PrimitiveShowcaseScene.cs",
            SceneDesignerTargetState.SceneType,
            [
                new SceneDesignerMenuItem("PrimitiveShowcaseBoxScene","Project.PrimitiveShowcaseBoxScene"),
                new SceneDesignerMenuItem("PrimitiveShowcaseSphereScene","Project.PrimitiveShowcaseSphereScene"),
            ]);

        Assert.True(Presentation.Visible);
        Assert.True(Presentation.Enabled);
        Assert.Equal(SceneDesignerMenuMode.Submenu,Presentation.Mode);
        Assert.Equal(
            ["PrimitiveShowcaseBoxScene","PrimitiveShowcaseSphereScene"],
            Presentation.Items.Select(Item=>Item.Text).ToArray());
    }

    [Theory]
    [InlineData("Analyzing","解析中")]
    [InlineData("Failed","失敗")]
    public void IncompleteAnalysis_IsDisabledWithTheCurrentReason(
        string StateName,string ExpectedReason){
        SceneDesignerTargetState State=(SceneDesignerTargetState)System.Enum.Parse(typeof(SceneDesignerTargetState),StateName);
        SceneDesignerCommandPresentation Presentation=SceneDesignerContextMenuPolicy.Create(
            @"G:\Project\MainScene.cs",
            State,
            []);

        Assert.True(Presentation.Visible);
        Assert.False(Presentation.Enabled);
        Assert.Contains(ExpectedReason,Presentation.Text);
    }
}