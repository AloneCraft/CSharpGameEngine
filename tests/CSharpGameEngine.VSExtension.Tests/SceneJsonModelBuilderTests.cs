using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0050: <see cref="SceneJsonModelBuilder"/>の単体テスト。Runtime側
/// (<c>src/CSharpGameEngine.CodeAnalysis/SceneJsonAnalyzer.cs</c>)と同じ値解釈規則
/// (宣言型で決める、enumはメンバー名文字列)を、VSIXが描画・編集に使う<see cref="SceneModel"/>系型で
/// 検証する。
/// </summary>
public sealed class SceneJsonModelBuilderTests {
    private static SceneModel Build(string Json){
        CSharpCompilation Compilation=SceneAnalysisTestHelper.CreateCompilation(SceneAnalysisTestHelper.CommonComponents);
        return SceneJsonModelBuilder.Build(
            @"G:\Project\Scenes\MainScene.scene.json",SourceText.From(Json),Compilation,CancellationToken.None);
    }

    [Fact]
    public void LiteralMembersResolveByDeclaredType(){
        SceneModel Model=Build("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "name": "Root",
                "components": [ { "type": "Sample.Label", "members": { "Text": "hello" } } ]
              }
            }
            """);

        Assert.True(Model.Diagnostics.IsEmpty);
        Assert.NotNull(Model.Root);
        Assert.Equal("Game.MainScene",Model.ClassFullyQualifiedName);
        Assert.Equal("Root",Model.Root!.Name);

        SceneComponent Component=Model.Root.Components[0];
        Assert.Equal("global::Sample.Label",Component.TypeFullyQualifiedName);
        SceneComponentMember Member=Component.Members.Single();
        Assert.Equal(SceneValueKind.Literal,Member.Value.Kind);
        Assert.Equal("hello",Member.Value.ConstantValue);
        Assert.True(Member.Value.IsEditable);
    }

    [Fact]
    public void EnumMemberNameResolvesToADeclaringFieldSymbol(){
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": {
                "components": [ { "type": "Sample.PrimitiveMesh", "members": { "Type": "Sphere" } } ]
              }
            }
            """);

        Assert.True(Model.Diagnostics.IsEmpty);
        SceneComponentMember Member=Model.Root!.Components[0].Members.Single();
        Assert.Equal(SceneValueKind.EnumMember,Member.Value.Kind);
        Assert.NotNull(Member.Value.ReferencedSymbol);
        Assert.Equal("Sphere",Member.Value.ReferencedSymbol!.Name);
        Assert.Equal("global::Sample.PrimitiveType.Sphere",Member.Value.EmitExpressionText);
    }

    [Fact]
    public void MultipleChildrenBuildTheSameNodePathsAsCSharpAnalysis(){
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": {
                "children": [ { "name": "A" }, { "name": "B" } ]
              }
            }
            """);

        Assert.True(Model.Diagnostics.IsEmpty);
        Assert.Equal(3,Model.TotalNodeCount);
        Assert.Equal("(root)",Model.Root!.Path.ToDisplayString());
        Assert.Equal("0",Model.Root.Children[0].Path.ToDisplayString());
        Assert.Equal("1",Model.Root.Children[1].Path.ToDisplayString());
    }

    [Fact]
    public void MalformedJsonReportsInvalidSceneJsonAndKeepsRootNull(){
        SceneModel Model=Build("{ not valid json");

        Assert.Null(Model.Root);
        Assert.True(Model.HasErrors);
        Assert.Contains(Model.Diagnostics,static D=>D.Id=="SCENE200");
    }

    [Fact]
    public void UnknownComponentTypeReportsSCENE107(){
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.DoesNotExist", "members": {} } ] }
            }
            """);

        Assert.True(Model.HasErrors);
        Assert.Contains(Model.Diagnostics,static D=>D.Id=="SCENE107");
    }

    [Fact]
    public void UnknownMemberReportsSCENE201(){
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.Label", "members": { "DoesNotExist": 1 } } ] }
            }
            """);

        Assert.True(Model.HasErrors);
        Assert.Contains(Model.Diagnostics,static D=>D.Id=="SCENE201");
    }
}
