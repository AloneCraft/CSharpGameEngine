using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0050: <see cref="SceneJsonSourceWriter.Serialize"/>の往復テスト。<see cref="SceneJsonModelBuilder"/>で
/// 読み込んだ<see cref="SceneModel"/>を書き出し、再度読み込んだ結果が意味的に一致することを確認する
/// (無編集の保存でシーンの意味が変わってはならない)。
/// </summary>
public sealed class SceneJsonSourceWriterTests {
    private static CSharpCompilation CreateCompilation()=>
        SceneAnalysisTestHelper.CreateCompilation(SceneAnalysisTestHelper.CommonComponents);

    private static SceneModel Build(string Json,CSharpCompilation Compilation)=>
        SceneJsonModelBuilder.Build(@"G:\Project\Scenes\MainScene.scene.json",SourceText.From(Json),Compilation,CancellationToken.None);

    [Fact]
    public void RoundTripPreservesStructureAndValues(){
        CSharpCompilation Compilation=CreateCompilation();

        // Position(Vector3)はJSON形式が非対応の型(ネスト構造体)なので、null値でメンバー自体を
        // 省略した状態を検証対象にする(往復対象のメンバーはType/Textのみ)。
        SceneModel Original=Build("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "startupOrder": 0,
              "root": {
                "name": "Root",
                "components": [
                  { "type": "Sample.PrimitiveMesh", "members": { "Type": "Sphere" } },
                  { "type": "Sample.Label", "members": { "Text": "hello" } }
                ],
                "children": [
                  { "name": "Child", "components": [ { "type": "Sample.DirectionalLight", "members": { "Intensity": 1.5 } } ] }
                ]
              }
            }
            """,Compilation);
        Assert.True(Original.Diagnostics.IsEmpty);

        string RoundTripped=SceneJsonSourceWriter.Serialize(Original);
        SceneModel Reloaded=Build(RoundTripped,Compilation);
        Assert.True(Reloaded.Diagnostics.IsEmpty);

        Assert.Equal(Original.Namespace,Reloaded.Namespace);
        Assert.Equal(Original.ClassName,Reloaded.ClassName);
        Assert.Equal(Original.IsStartupScene,Reloaded.IsStartupScene);
        Assert.Equal(Original.StartupOrder,Reloaded.StartupOrder);
        Assert.Equal(Original.Root!.Name,Reloaded.Root!.Name);
        Assert.Equal(
            Original.Root.Components.Select(static C=>C.EmitExpressionText),
            Reloaded.Root.Components.Select(static C=>C.EmitExpressionText));
        Assert.Equal(Original.Root.Children[0].Name,Reloaded.Root.Children[0].Name);
        Assert.Equal(
            Original.Root.Children[0].Components[0].EmitExpressionText,
            Reloaded.Root.Children[0].Components[0].EmitExpressionText);
    }

    [Fact]
    public void EnumMemberSerializesAsTheMemberNameNotTheUnderlyingValue(){
        CSharpCompilation Compilation=CreateCompilation();
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.PrimitiveMesh", "members": { "Type": "Plane" } } ] }
            }
            """,Compilation);

        string Text=SceneJsonSourceWriter.Serialize(Model);

        Assert.Contains("\"Plane\"",Text);
        Assert.DoesNotContain("global::",Text);
    }

    [Fact]
    public void ComponentTypeSerializesWithoutTheGlobalPrefix(){
        CSharpCompilation Compilation=CreateCompilation();
        SceneModel Model=Build("""
            {
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.Label", "members": { "Text": "x" } } ] }
            }
            """,Compilation);

        string Text=SceneJsonSourceWriter.Serialize(Model);

        Assert.Contains("\"Sample.Label\"",Text);
        Assert.DoesNotContain("global::",Text);
    }
}
