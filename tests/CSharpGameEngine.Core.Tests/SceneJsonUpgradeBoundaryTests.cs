namespace CSharpGameEngine.Core.Tests;
/// <summary>
/// ADR-0098 §5 規則 2：旧版のシーン JSON を読む入口（<c>AnalyzeEarlierVersion</c>）と、それを使う書き換え（<c>SceneJsonAngleUpgrade</c>）は、
/// <c>SceneJson.Cli upgrade</c> だけが使う。生成器、VSIX、ランタイムは現行版だけを読む <c>Analyze</c> を使う。
/// </summary>
public sealed class SceneJsonUpgradeBoundaryTests {
    private static readonly string RepositoryRoot=FindRepositoryRoot();
    private static readonly string[]Roots=["src","tools","plugins","Templates","UserEcsProject","samples","stages"];
    private static readonly string[]SkippedDirectories=["bin","obj",".vs",".artifacts","node_modules"];
    private static readonly string[]Allowed=[
        "src/SceneModel/Json/SceneJsonSceneAnalyzer.cs",
        "tools/SceneJson.Cli/",
    ];
    [Fact]
    public void OnlyTheUpgradeCommandReadsEarlierSceneVersions(){
        var Violations=Roots.Select(Root=>Path.Combine(RepositoryRoot,Root)).Where(Directory.Exists).SelectMany(SourceFiles)
            .Select(File=>(Path:Path.GetRelativePath(RepositoryRoot,File).Replace('\\','/'),Text:System.IO.File.ReadAllText(File)))
            .Where(static Entry=>Entry.Text.Contains("AnalyzeEarlierVersion(",StringComparison.Ordinal)||Entry.Text.Contains("SceneJsonAngleUpgrade.",StringComparison.Ordinal))
            .Where(static Entry=>!Allowed.Any(Prefix=>Entry.Path.StartsWith(Prefix,StringComparison.Ordinal)))
            .Select(static Entry=>Entry.Path).ToArray();
        Assert.True(Violations.Length==0,string.Join(Environment.NewLine,Violations));
        Assert.Contains("SceneJsonAngleUpgrade.Upgrade(",File.ReadAllText(Path.Combine(RepositoryRoot,"tools","SceneJson.Cli","Program.cs")),StringComparison.Ordinal);
        // 変換処理は CLI のフォルダーにだけあり、生成器と VSIX が取り込む共有ソース（SceneModel）には無い。
        Assert.True(File.Exists(Path.Combine(RepositoryRoot,"tools","SceneJson.Cli","SceneJsonAngleUpgrade.cs")));
        Assert.False(File.Exists(Path.Combine(RepositoryRoot,"src","SceneModel","Json","SceneJsonAngleUpgrade.cs")));
    }
    /// <summary>旧版の読み方を有効にする記号を定義してよいのは、変換ツールと SceneModel の試験だけ（SceneModel は共有ソースで、自分のプロジェクトを持たない。ADR-0099）。生成器・VSIX・ゲームは定義しない。</summary>
    [Fact]
    public void OnlyTheUpgradeToolAndSceneModelTestsDefineTheUpgradeSymbol(){
        string[]AllowedProjects=[
            "tests/CSharpGameEngine.SceneModel.Tests/CSharpGameEngine.SceneModel.Tests.csproj",
            "tools/SceneJson.Cli/SceneJson.Cli.csproj",
        ];
        var Defining=Roots.Append("tests").Append("build").Append("packaging").Select(Root=>Path.Combine(RepositoryRoot,Root)).Where(Directory.Exists)
            .SelectMany(static Root=>ProjectFiles(Root))
            .Where(static File=>System.IO.File.ReadAllText(File).Contains("CGE_SCENE_UPGRADE",StringComparison.Ordinal))
            .Select(File=>Path.GetRelativePath(RepositoryRoot,File).Replace('\\','/')).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(AllowedProjects,Defining);
    }
    private static IEnumerable<string>ProjectFiles(string Directory){
        foreach(var File in System.IO.Directory.EnumerateFiles(Directory).Where(static File=>File.EndsWith(".csproj",StringComparison.OrdinalIgnoreCase)||File.EndsWith(".props",StringComparison.OrdinalIgnoreCase)||File.EndsWith(".targets",StringComparison.OrdinalIgnoreCase))){ yield return File; }
        foreach(var Child in System.IO.Directory.EnumerateDirectories(Directory)){
            if(SkippedDirectories.Contains(Path.GetFileName(Child),StringComparer.OrdinalIgnoreCase)){ continue; }
            foreach(var File in ProjectFiles(Child)){ yield return File; }
        }
    }
    private static IEnumerable<string>SourceFiles(string Directory){
        foreach(var File in System.IO.Directory.EnumerateFiles(Directory,"*.cs")){ yield return File; }
        foreach(var Child in System.IO.Directory.EnumerateDirectories(Directory)){
            if(SkippedDirectories.Contains(Path.GetFileName(Child),StringComparer.OrdinalIgnoreCase)){ continue; }
            foreach(var File in SourceFiles(Child)){ yield return File; }
        }
    }
    private static string FindRepositoryRoot(){
        var Current=new DirectoryInfo(AppContext.BaseDirectory);
        while(Current is not null){
            if(File.Exists(Path.Combine(Current.FullName,"CSharpGameEngine.slnx"))){ return Current.FullName; }
            Current=Current.Parent;
        }
        throw new DirectoryNotFoundException("CSharpGameEngine.slnxのルートが見つかりません。");
    }
}
