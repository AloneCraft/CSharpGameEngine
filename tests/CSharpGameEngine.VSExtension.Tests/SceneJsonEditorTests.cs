using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// ADR-0071: a scene JSON (a <c>*.json</c> under the project's <c>Scenes</c> folder) opens in the Scene Editor (Design / JSON); every other JSON file goes
/// to the next editor. The views map onto one window's tabs, and each tab shows only its own scene.
/// </summary>
public sealed class SceneJsonEditorTests {
    /// <summary>
    /// A double-click gives the editor only a path: the project is the nearest folder with a
    /// <c>*.csproj</c>, and a scene is a <c>*.json</c> under its <c>Scenes</c> folder.
    /// </summary>
    [Theory]
    [InlineData(@"Scenes\Main.json",true)]
    [InlineData(@"Scenes\Levels\Lvl_ThirdPerson.json",true)]
    [InlineData(@"appsettings.json",false)]
    [InlineData(@"Assets\Data.json",false)]
    [InlineData(@"Main.scene.json",false)]
    [InlineData(@"Scenes\Main.cs",false)]
    public void Factory_AcceptsOnlyJsonUnderTheProjectsScenesFolder(string RelativePath,bool Expected){
        string Root=Path.Combine(Path.GetTempPath(),"cge-scene-editor-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(Path.Combine(Root,"Scenes","Levels"));
            Directory.CreateDirectory(Path.Combine(Root,"Assets"));
            File.WriteAllText(Path.Combine(Root,"Game.csproj"),"<Project />");
            Assert.Equal(Expected,SceneJsonEditorFactory.Accepts(Path.Combine(Root,RelativePath)));
        } finally {
            Directory.Delete(Root,recursive:true);
        }
    }

    [Fact]
    public void Factory_DeclinesJsonOutsideAnyProject(){
        Assert.False(SceneJsonEditorFactory.Accepts(null));
        Assert.False(SceneJsonEditorFactory.Accepts(Path.Combine(Path.GetTempPath(),"cge-no-project-"+Guid.NewGuid().ToString("N"),"Scenes","Main.json")));
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
