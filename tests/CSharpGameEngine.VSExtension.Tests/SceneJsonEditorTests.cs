using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// ADR-0071: <c>*.scene.json</c> opens in the Scene Editor (Design / JSON); every other JSON file goes
/// to the next editor. The views map onto one window's tabs, and each tab shows only its own scene.
/// </summary>
public sealed class SceneJsonEditorTests {
    [Theory]
    [InlineData(@"G:\Game\Scenes\Main.scene.json",true)]
    [InlineData(@"G:\Game\Scenes\Main.SCENE.JSON",true)]
    [InlineData(@"G:\Game\appsettings.json",false)]
    [InlineData(@"G:\Game\global.json",false)]
    [InlineData(@"G:\Game\Scenes\scene.json",false)]
    [InlineData(null,false)]
    public void Factory_AcceptsOnlySceneJson(string?Path,bool Expected){
        Assert.Equal(Expected,SceneJsonEditorFactory.Accepts(Path));
    }

    [Fact]
    public void LogicalViews_SelectTabsOfOneWindow(){
        Assert.Equal(SceneJsonEditorView.Design,SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_Primary));
        Assert.Equal(SceneJsonEditorView.Design,SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_Designer));
        Assert.Equal(SceneJsonEditorView.Json,SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_TextView));
        Assert.Equal(SceneJsonEditorView.Json,SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_Code));
        // A view the editor does not have: MapLogicalView answers E_NOTIMPL from the same mapping.
        Assert.Null(SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_Debugging));
    }

    [Fact]
    public void Package_RegistersTheFactoryForJsonAboveTheJsonEditor(){
        var Extension=Assert.Single(
            typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorExtensionAttribute>(),
            static Attribute=>Attribute.Factory==typeof(SceneJsonEditorFactory).GUID);
        Assert.Equal(".json",Extension.Extension);
        // The Visual Studio JSON editor registers .json at 0x32.
        Assert.True(Extension.Priority>0x32);
        var Views=typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorLogicalViewAttribute>()
            .Where(static Attribute=>Attribute.FactoryType==typeof(SceneJsonEditorFactory).GUID)
            .Select(static Attribute=>Attribute.LogicalView)
            .ToHashSet();
        Assert.Contains(VSConstants.LOGVIEWID_Designer,Views);
        Assert.Contains(VSConstants.LOGVIEWID_TextView,Views);
        Assert.Contains(VSConstants.LOGVIEWID_Code,Views);
    }

    [Fact]
    public void DesignerTab_ShowsOnlyItsOwnScene(){
        var Own=new SceneKey("Project","Game.Scenes.Main");
        var Other=new SceneKey(Own.ProjectId,"Game.Scenes.Other");

        Assert.True(SceneDesignerTargetFilter.Accepts(null,Other));
        Assert.True(SceneDesignerTargetFilter.Accepts(Own,Own));
        Assert.False(SceneDesignerTargetFilter.Accepts(Own,Other));
        Assert.False(SceneDesignerTargetFilter.Accepts(Own,null));
    }
}
