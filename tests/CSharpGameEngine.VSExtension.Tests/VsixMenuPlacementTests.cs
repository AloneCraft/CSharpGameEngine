using System.Xml.Linq;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// The engine's commands live in Extensions &gt; C# Game Engine (not the Tools menu), and that menu
/// opens the Scene Editor options directly, so the navigation switches are one click away.
/// </summary>
public sealed class VsixMenuPlacementTests {
    private static readonly XNamespace Vsct="http://schemas.microsoft.com/VisualStudio/2005-10-18/CommandTable";
    private const string CommandSet="guidOpenViewerCommandPackageCmdSet";
    private static readonly string ProjectDirectory=Path.Combine(VsixBuildIdentity.RepoRoot,"tools","CSharpGameEngine.VSExtension");
    private static XDocument Load(string Name)=>XDocument.Load(Path.Combine(ProjectDirectory,Name));
    private static XElement Parent(XElement Element)=>Assert.IsType<XElement>(Element.Element(Vsct+"Parent"));
    private static XElement ById(XDocument Document,string Kind,string Id)=>
        Assert.Single(Document.Descendants(Vsct+Kind),Element=>(string?)Element.Attribute("id")==Id);
    [Fact]
    public void EngineMenu_IsASubmenuOfTheExtensionsMenu(){
        var Document=Load("OpenViewerCommandPackage.vsct");
        var Menu=ById(Document,"Menu","CSharpGameEngineMenu");
        Assert.Equal("Menu",(string?)Menu.Attribute("type"));
        var MenuGroupId=(string?)Parent(Menu).Attribute("id");
        var MenuGroup=ById(Document,"Group",MenuGroupId!);
        Assert.Equal("guidSHLMainMenu",(string?)Parent(MenuGroup).Attribute("guid"));
        Assert.Equal("IDM_VS_MENU_EXTENSIONS",(string?)Parent(MenuGroup).Attribute("id"));
    }
    [Fact]
    public void WindowAndGameCommands_MovedFromToolsIntoTheEngineMenu(){
        var Document=Load("OpenViewerCommandPackage.vsct");
        var CommandsGroup=ById(Document,"Group","OpenViewerMenuGroup");
        Assert.Equal(CommandSet,(string?)Parent(CommandsGroup).Attribute("guid"));
        Assert.Equal("CSharpGameEngineMenu",(string?)Parent(CommandsGroup).Attribute("id"));
        Assert.DoesNotContain(Document.Descendants(Vsct+"Parent"),Element=>(string?)Element.Attribute("id")=="IDM_VS_MENU_TOOLS");
        foreach(var Command in new[]{"OpenViewerCommandId","ToggleFabWatchCommandId","OpenFabCommandId","OpenGameCommandId",
            "ShowHierarchyToolWindowCommandId","ApplyGameEngineLayoutCommandId","ShowInspectorToolWindowCommandId","ShowComponentPaletteToolWindowCommandId"}){
            Assert.Equal("OpenViewerMenuGroup",(string?)Parent(ById(Document,"Button",Command)).Attribute("id"));
        }
    }
    /// <summary>
    /// ADR-0071: scenes open by double-click, so the extension adds nothing to the code editor's or
    /// Solution Explorer's context menus. (Before the package loads, Visual Studio shows context-menu
    /// commands with their .vsct defaults, which put "Open in Scene Designer" and 32 "Scene" rows on
    /// every C# file.)
    /// </summary>
    [Theory]
    [InlineData("OpenViewerCommandPackage.vsct")]
    [InlineData("OpenViewerCommandPackage.en-US.vsct")]
    [InlineData("OpenViewerCommandPackage.ja-JP.vsct")]
    public void NothingIsPlacedOnEditorOrSolutionExplorerContextMenus(string File){
        var ContextMenuParents=Load(File).Descendants(Vsct+"Parent")
            .Where(static Element=>((string?)Element.Attribute("id")??string.Empty).StartsWith("IDM_VS_CTXT_",StringComparison.Ordinal))
            .Select(static Element=>(string?)Element.Parent!.Attribute("id"))
            .ToList();

        Assert.Empty(ContextMenuParents);
    }

    [Fact]
    public void OptionsCommand_OpensFromTheEngineMenuWithLocalizedText(){
        var Document=Load("OpenViewerCommandPackage.vsct");
        var Button=ById(Document,"Button","OpenSceneEditorOptionsCommandId");
        var GroupId=(string?)Parent(Button).Attribute("id");
        Assert.Equal("CSharpGameEngineMenu",(string?)Parent(ById(Document,"Group",GroupId!)).Attribute("id"));
        var Symbol=Assert.Single(Document.Descendants(Vsct+"IDSymbol"),Element=>(string?)Element.Attribute("name")=="OpenSceneEditorOptionsCommandId");
        Assert.Equal(OpenSceneEditorOptionsCommand.CommandId,Convert.ToInt32((string?)Symbol.Attribute("value"),16));
        Assert.Equal("Options...",Button.Descendants(Vsct+"ButtonText").Single().Value);
        var Japanese=ById(Load("OpenViewerCommandPackage.ja-JP.vsct"),"Button","OpenSceneEditorOptionsCommandId");
        Assert.Equal("オプション...",Japanese.Descendants(Vsct+"ButtonText").Single().Value);
    }
}
