using System.Reflection;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// ADR-0071：シーン JSON（プロジェクトの <c>Scenes</c> フォルダー内にある <c>*.json</c>）は Scene Editor (Design / JSON) で開き、それ以外の JSON ファイルは次のエディターへ渡す。ビューは 1 つのウィンドウのタブに対応し、各タブにはそれぞれのシーンだけを表示する。
/// </summary>
public sealed class SceneJsonEditorTests {
    /// <summary>
    /// ダブルクリックでエディターに渡されるのはパスだけである。プロジェクトは最も近い <c>*.csproj</c> フォルダー、シーンはその <c>Scenes</c> フォルダー内の <c>*.json</c> として判定する。
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
        // エディターが持たないビュー：同じマッピングに対する MapLogicalView は E_NOTIMPL を返す。
        Assert.Null(SceneJsonEditorPane.ViewFor(VSConstants.LOGVIEWID_Debugging));
    }

    [Fact]
    public void Package_RegistersTheFactoryForJsonAboveTheJsonEditor(){
        var Extension=Assert.Single(
            typeof(CSharpGameEngineViewerPackage).GetCustomAttributes<ProvideEditorExtensionAttribute>(),
            static Attribute=>Attribute.Factory==typeof(SceneJsonEditorFactory).GUID);
        Assert.Equal(".json",Extension.Extension);
        // Visual Studio の JSON エディターは .json を 0x32 で登録する。
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
    /// <summary>
    /// M0095 1-4：固定シーンのタブを閉じるとピン留めが解除され、Hierarchy と Inspector は閉じたドキュメントの表示と編集をやめる。他のタブを閉じてもピン留めは維持される。
    /// </summary>
    [Fact]
    public void ClosingATab_ReleasesThePinOnlyWhenItShowsThePinnedScene(){
        var Own=new SceneKey("Project","Game.Scenes.Main");
        var Other=new SceneKey(Own.ProjectId,"Game.Scenes.Other");
        Assert.True(SceneDesignerTargetFilter.ReleasesPinOnClose(Own,Own));
        Assert.False(SceneDesignerTargetFilter.ReleasesPinOnClose(Own,Other));
        Assert.False(SceneDesignerTargetFilter.ReleasesPinOnClose(Own,null));
        // Design ビューを一度も表示していないタブにはシーンがなく、何もピン留めしない。
        Assert.False(SceneDesignerTargetFilter.ReleasesPinOnClose(null,Other));
    }
}
