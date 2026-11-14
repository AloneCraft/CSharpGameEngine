using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>ADR-0079：有効なプラグインのマニフェストを検査する（PLUGIN001-006）。既定のテスト用エンジンのバージョンは0.1.16（SceneProjectOptions）。</summary>
public sealed class PluginManifestAnalyzerTests {
    private static AdditionalText Manifest(string Folder,string Text)=>new InMemoryAdditionalText($@"C:\Game\Plugins\{Folder}\{Folder}.cgeplugin",Text);

    private static ImmutableArray<Diagnostic>Analyze(params AdditionalText[]Manifests){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation();
        return Compilation.WithAnalyzers([new PluginManifestAnalyzer()],SceneProjectOptions.Analyzer([.. Manifests]))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }

    [Fact]
    public void ValidManifestsWithTheirDependencies_ReportNothing(){
        var Diagnostics=Analyze(
            Manifest("Vehicles","""{ "Name": "Vehicles", "Version": "1.0.0", "EngineVersion": ">=0.1.16 <0.2", "Dependencies": ["Physics2"] }"""),
            Manifest("Physics2","""{ "Name": "Physics2", "Version": "2.1" }"""));

        Assert.Empty(Diagnostics);
    }

    [Theory]
    [InlineData("""{ "Version": "1.0.0" }""")]
    [InlineData("""{ "Name": "Not a name", "Version": "1.0.0" }""")]
    [InlineData("""{ "Name": "Cars", "Version": "one" }""")]
    [InlineData("""{ "Name": "Cars", "Version": "1.0", "EngineVersion": "~1" }""")]
    [InlineData("""{ "Name": "Cars", "Version": "1.0", "Dependencies": "Physics" }""")]
    [InlineData("""{ "Name": "Cars", "Version": "1.0", }""")]
    [InlineData("""[]""")]
    public void AnInvalidManifest_ReportsPlugin001(string Text){
        var Diagnostic=Assert.Single(Analyze(Manifest("Cars",Text)));

        Assert.Equal("PLUGIN001",Diagnostic.Id);
    }

    [Fact]
    public void TheSamePluginTwice_ReportsPlugin002AtTheLaterPath(){
        var Diagnostic=Assert.Single(Analyze(
            Manifest("A","""{ "Name": "Cars", "Version": "1.0" }"""),
            Manifest("B","""{ "Name": "Cars", "Version": "1.1" }""")));

        Assert.Equal("PLUGIN002",Diagnostic.Id);
        Assert.EndsWith(@"B\B.cgeplugin",Diagnostic.Location.GetLineSpan().Path);
    }

    [Fact]
    public void ADependencyThatIsNotEnabled_ReportsPlugin003AtTheName(){
        var Diagnostic=Assert.Single(Analyze(Manifest("Cars","""{ "Name": "Cars", "Version": "1.0", "Dependencies": ["Navigation"] }""")));

        Assert.Equal("PLUGIN003",Diagnostic.Id);
        Assert.Contains("'Navigation'",Diagnostic.GetMessage());
    }

    [Fact]
    public void ADependencyCycle_ReportsPlugin004WithThePath(){
        var Diagnostic=Assert.Single(Analyze(
            Manifest("A","""{ "Name": "A", "Version": "1.0", "Dependencies": ["B"] }"""),
            Manifest("B","""{ "Name": "B", "Version": "1.0", "Dependencies": ["C"] }"""),
            Manifest("C","""{ "Name": "C", "Version": "1.0", "Dependencies": ["A"] }""")));

        Assert.Equal("PLUGIN004",Diagnostic.Id);
        Assert.Contains("A -> B -> C -> A",Diagnostic.GetMessage());
    }

    [Theory]
    [InlineData(">=0.2.0",true)]
    [InlineData("<0.1.16",true)]
    [InlineData("0.1.15",true)]
    [InlineData(">0.1.16",true)]
    [InlineData("0.1.16",false)]
    [InlineData(">=0.1 <=0.1.16",false)]
    [InlineData("=0.1.16.0",false)]
    public void AnEngineVersionOutsideTheRange_ReportsPlugin005(string Range,bool Reported){
        var Diagnostics=Analyze(Manifest("Cars",$$"""{ "Name": "Cars", "Version": "1.0", "EngineVersion": "{{Range}}" }"""));

        Assert.Equal(Reported,Diagnostics.Any(static Diagnostic=>Diagnostic.Id=="PLUGIN005"));
        Assert.DoesNotContain(Diagnostics,static Diagnostic=>Diagnostic.Id!="PLUGIN005");
    }

    /// <summary>ADR-0098 §5 規則 4：最初の公開まで、エンジンの版は <c>0.1.0-dev.N</c>。SemVer と同じく、プレリリースは正式版より前に並ぶ。</summary>
    [Theory]
    [InlineData(">=0.1.0-dev.50",false)]
    [InlineData(">=0.1.0-dev.9",false)]
    [InlineData(">=0.1.0-dev.51",true)]
    [InlineData(">=0.1.0",true)]
    [InlineData("<0.1.0",false)]
    [InlineData(">=0.1.0-dev.50 <0.2",false)]
    [InlineData(">=0.1.0-alpha",false)]
    [InlineData(">=0.1.0-rc",true)]
    [InlineData(">=0.1.16",true)]
    public void APrereleaseEngineVersionIsComparedLikeSemVer(string Range,bool Reported){
        var Diagnostics=AnalyzeWithEngine("0.1.0-dev.50",Manifest("Cars",$$"""{ "Name": "Cars", "Version": "1.0", "EngineVersion": "{{Range}}" }"""));

        Assert.Equal(Reported,Diagnostics.Any(static Diagnostic=>Diagnostic.Id=="PLUGIN005"));
        Assert.DoesNotContain(Diagnostics,static Diagnostic=>Diagnostic.Id!="PLUGIN005");
    }

    [Fact]
    public void AnUnreadableEngineVersion_ReportsPlugin006InsteadOfSkippingTheCheck(){
        var Diagnostic=Assert.Single(AnalyzeWithEngine("0.1.0-",Manifest("Cars","""{ "Name": "Cars", "Version": "1.0", "EngineVersion": ">=0.1.0" }""")));

        Assert.Equal("PLUGIN006",Diagnostic.Id);
        Assert.Contains("'0.1.0-'",Diagnostic.GetMessage(),StringComparison.Ordinal);
    }

    private static ImmutableArray<Diagnostic>AnalyzeWithEngine(string EngineVersion,params AdditionalText[]Manifests){
        var Compilation=GameSystemGeneratorTestHelper.CreateCompilation();
        return Compilation.WithAnalyzers([new PluginManifestAnalyzer()],SceneProjectOptions.Analyzer([.. Manifests],EngineVersion))
            .GetAnalyzerDiagnosticsAsync().GetAwaiter().GetResult();
    }
}
