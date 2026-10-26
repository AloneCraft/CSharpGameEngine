using System.Xml.Linq;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// エンジンのコマンドは Tools メニューではなく Extensions &gt; C# Game Engine にあり、そのメニューから Scene Editor のオプションを直接開けるため、ナビゲーション設定は 1 クリックで操作できる。
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
    /// ADR-0071：シーンはダブルクリックで開くため、拡張機能はコードエディターや Solution Explorer のコンテキストメニューに項目を追加しない。（パッケージ読み込み前は、Visual Studio は .vsct の既定値でコンテキストメニューを表示し、すべての C# ファイルに「Open in Scene Designer」と 32 個の「Scene」行が追加されていた。）
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

    /// <summary>
    /// M0097（ユーザー要求）：ツールバーに Extensions &gt; C# Game Engine のすべてのコマンドを配置する。Options と Diagnostic Tools サブメニューも含め、メニューだけからしか操作できない機能をなくす。
    /// </summary>
    [Theory]
    [InlineData("OpenViewerCommandPackage.vsct")]
    [InlineData("OpenViewerCommandPackage.ja-JP.vsct")]
    public void EveryEngineMenuCommand_IsAlsoOnTheToolbar(string File){
        var Document=Load(File);
        var FromMenu=Reachable(Document,"CSharpGameEngineMenu");
        var FromToolbar=Reachable(Document,"CSharpGameEngineToolbar");
        Assert.Contains("OpenSceneEditorOptionsCommandId",FromMenu);
        Assert.Contains("AttachDebuggerToGameCommandId",FromMenu);
        Assert.Empty(FromMenu.Except(FromToolbar));
    }

    /// <summary>M0097：exe として起動したゲームに対し、Visual Studio のデバッガーとプロファイラーを提供する。</summary>
    [Fact]
    public void DiagnosticToolsSubmenu_HoldsTheFourCommandsWithTheirIds(){
        var Document=Load("OpenViewerCommandPackage.vsct");
        var Commands=Reachable(Document,"CSharpGameEngineDiagnosticsMenu");
        string[]Expected=["AttachDebuggerToGameCommandId","MeasureCpuUsageCommandId","MeasureDotNetAllocationsCommandId","MeasureGpuUsageCommandId"];
        Assert.Equal(Expected.OrderBy(static Id=>Id,StringComparer.Ordinal),Commands.OrderBy(static Id=>Id,StringComparer.Ordinal));
        int Value(string Name)=>Convert.ToInt32((string?)Assert.Single(Document.Descendants(Vsct+"IDSymbol"),
            Element=>(string?)Element.Attribute("name")==Name).Attribute("value"),16);
        Assert.Equal(GameDiagnosticsCommands.AttachDebuggerCommandId,Value("AttachDebuggerToGameCommandId"));
        Assert.Equal(GameDiagnosticsCommands.MeasureCpuUsageCommandId,Value("MeasureCpuUsageCommandId"));
        Assert.Equal(GameDiagnosticsCommands.MeasureDotNetAllocationsCommandId,Value("MeasureDotNetAllocationsCommandId"));
        Assert.Equal(GameDiagnosticsCommands.MeasureGpuUsageCommandId,Value("MeasureGpuUsageCommandId"));
        var Japanese=ById(Load("OpenViewerCommandPackage.ja-JP.vsct"),"Menu","CSharpGameEngineDiagnosticsMenu");
        Assert.Equal("診断ツール",Japanese.Descendants(Vsct+"ButtonText").Single().Value);
    }

    /// <summary>メニューまたはツールバー配下にあるボタンのID。グループ、サブメニュー、コマンド配置をたどる。</summary>
    private static HashSet<string>Reachable(XDocument Document,string Root){
        var ParentsOf=new Dictionary<string,List<string>>(StringComparer.Ordinal);
        void Link(string?Id,string?Parent){
            if(Id is null||Parent is null){ return; }
            if(!ParentsOf.TryGetValue(Id,out var Parents)){ ParentsOf[Id]=Parents=[]; }
            Parents.Add(Parent);
        }
        foreach(var Element in Document.Descendants().Where(static Element=>Element.Name.LocalName is "Button" or "Menu" or "Group" or "CommandPlacement")){
            Link((string?)Element.Attribute("id"),(string?)Element.Element(Vsct+"Parent")?.Attribute("id"));
        }
        var Buttons=Document.Descendants(Vsct+"Button").Select(static Element=>(string)Element.Attribute("id")!).ToHashSet(StringComparer.Ordinal);
        bool Under(string Id,HashSet<string>Seen)=>Id==Root
            ||(Seen.Add(Id)&&ParentsOf.TryGetValue(Id,out var Parents)&&Parents.Any(Parent=>Under(Parent,Seen)));
        return Buttons.Where(Id=>ParentsOf.ContainsKey(Id)&&ParentsOf[Id].Any(Parent=>Under(Parent,new HashSet<string>(StringComparer.Ordinal)))).ToHashSet(StringComparer.Ordinal);
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
