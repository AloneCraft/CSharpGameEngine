using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// M0049: <see cref="SceneJsonAnalyzer"/>の単体テスト。<c>*.scene.json</c>から<see cref="SceneEmitModel"/>への
/// 変換(値の解釈は宣言型で決める)と、SCENE2xx診断の報告条件を検証する。
/// </summary>
public sealed class SceneJsonAnalyzerTests {
    private const string Components="""
        namespace Sample;

        public struct Transform { public float X; public string?Label; public bool Active; }

        public enum MeshMobility { Static, Movable }

        public struct StaticMesh { public MeshMobility Mobility; }

        public struct WithReadOnlyField { public readonly float X; }
        """;

    private static CSharpCompilation CreateCompilation()=>
        CSharpCompilation.Create(
            "SceneJsonAnalyzerTestAssembly",
            [CSharpSyntaxTree.ParseText(Components)],
            GameSystemGeneratorTestHelper.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

    private static SceneJsonAnalysisResult Analyze(string Json)=>
        SceneJsonAnalyzer.Analyze(new InMemoryAdditionalText("Scenes/MainScene.scene.json",Json),CreateCompilation(),default);

    [Fact]
    public void LiteralMembersResolveByDeclaredType(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "name": "Root",
                "components": [ { "type": "Sample.Transform", "members": { "X": 1.5, "Label": "hello", "Active": true } } ]
              }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        Assert.True(Result.Model is { CanEmit:true });
        Assert.Equal("MainScene",Result.Model!.Value.ClassName);
        Assert.Equal("Game",Result.Model!.Value.Namespace);
        Assert.Equal(1,Result.Model!.Value.TotalNodeCount);

        SceneEmitComponent Component=Result.Model!.Value.Root!.Value.Components[0];
        Assert.Equal("global::Sample.Transform",Component.TypeFullyQualifiedName);
        Assert.Contains("X = 1.5f",Component.EmitExpressionText);
        Assert.Contains("Label = \"hello\"",Component.EmitExpressionText);
        Assert.Contains("Active = true",Component.EmitExpressionText);
    }

    [Fact]
    public void EnumMemberNameResolvesToTheDeclaringFieldSymbol(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "components": [ { "type": "Sample.StaticMesh", "members": { "Mobility": "Movable" } } ]
              }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        SceneEmitComponent Component=Result.Model!.Value.Root!.Value.Components[0];
        Assert.Contains("Mobility = global::Sample.MeshMobility.Movable",Component.EmitExpressionText);
    }

    [Fact]
    public void MultipleChildrenAreWalkedRecursivelyWithPathBasedLocalNames(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "children": [
                  { "name": "A" },
                  { "name": "B", "children": [ { "name": "B0" } ] }
                ]
              }
            }
            """);

        Assert.Empty(Result.Diagnostics);
        SceneEmitNode Root=Result.Model!.Value.Root!.Value;
        Assert.Equal(4,Result.Model!.Value.TotalNodeCount);
        Assert.Equal(2,Root.Children.Length);
        Assert.Equal("Node_0",Root.Children[0].LocalName);
        Assert.Equal("Node_1",Root.Children[1].LocalName);
        Assert.Equal("Node_1_0",Root.Children[1].Children[0].LocalName);
    }

    [Fact]
    public void MalformedJsonReportsInvalidSceneJson(){
        SceneJsonAnalysisResult Result=Analyze("{ not valid json");

        Assert.Null(Result.Model);
        Assert.Single(Result.Diagnostics);
        Assert.Equal("SCENE200",Result.Diagnostics[0].Id);
    }

    [Fact]
    public void MissingClassNameReportsInvalidSceneJson(){
        SceneJsonAnalysisResult Result=Analyze("""{ "root": { "name": "Root" } }""");

        Assert.Null(Result.Model);
        Assert.Single(Result.Diagnostics);
        Assert.Equal("SCENE200",Result.Diagnostics[0].Id);
    }

    [Fact]
    public void MissingRootReportsInvalidSceneJson(){
        SceneJsonAnalysisResult Result=Analyze("""{ "className": "MainScene" }""");

        Assert.Null(Result.Model);
        Assert.Single(Result.Diagnostics);
        Assert.Equal("SCENE200",Result.Diagnostics[0].Id);
    }

    [Fact]
    public void UnknownComponentTypeReportsComponentMustBeStructCreation(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.DoesNotExist", "members": {} } ] }
            }
            """);

        Assert.False(Result.Model!.Value.CanEmit);
        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SCENE107");
    }

    [Fact]
    public void UnknownMemberReportsUnknownJsonMember(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.Transform", "members": { "DoesNotExist": 1 } } ] }
            }
            """);

        Assert.False(Result.Model!.Value.CanEmit);
        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SCENE201");
    }

    [Fact]
    public void StringValueOnNumericMemberReportsJsonValueTypeMismatch(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.Transform", "members": { "X": "not a number" } } ] }
            }
            """);

        Assert.False(Result.Model!.Value.CanEmit);
        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SCENE202");
    }

    [Fact]
    public void UnknownEnumMemberNameReportsUnknownEnumMember(){
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.StaticMesh", "members": { "Mobility": "NotAMember" } } ] }
            }
            """);

        Assert.False(Result.Model!.Value.CanEmit);
        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SCENE203");
    }

    [Fact]
    public void ReadOnlyFieldIsTreatedAsUnknownMember(){
        // readonlyフィールドへのオブジェクト初期化子代入はコンパイルエラー(CS0191)になるため、
        // TryResolveMemberは代入不能なメンバーを解決対象から除外しなければならない。
        SceneJsonAnalysisResult Result=Analyze("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.WithReadOnlyField", "members": { "X": 1.0 } } ] }
            }
            """);

        Assert.False(Result.Model!.Value.CanEmit);
        Assert.Contains(Result.Diagnostics,static D=>D.Id=="SCENE201");
    }
}
