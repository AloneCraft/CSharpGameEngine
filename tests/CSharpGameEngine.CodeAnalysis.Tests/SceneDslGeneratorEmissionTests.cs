using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Scene JSON(M0049)から生成される<c>Instantiate</c>の形(ADR-0040 H-1)。
///
/// <para>
/// 最重要の検証は<b>「Scene authoringの抽象が生成コードに現れないこと」</b>である。JSONの構造や
/// <c>object[]</c>がRuntimeへ漏れていたら、この設計の中核が破れている。
/// </para>
/// </summary>
public sealed class SceneDslGeneratorEmissionTests {
    private const string Components="""
        namespace Sample;

        public struct Transform { public float X; public float Y; }
        public struct Light { public float Intensity; }
        public struct Mesh { public int Id; }

        // GameSystemGeneratorはISystem型がコンパイル内に無いと何も出力しない(ADR-0033のゲート)。
        internal struct KeepGeneratorAlive : CSharpGameEngine.Entities.ISystem
        {
            public bool Update(ref Transform transform, ref CSharpGameEngine.Entities.State state) => true;
        }
        """;

    private static string Generate(string SceneJson,string HintName="Game.MainScene.Scene.g.cs"){
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGeneratorWithSceneJson(
            [Components],[("Scenes/MainScene.scene.json",SceneJson)]);
        return GameSystemGeneratorTestHelper.GeneratedText(Result,HintName);
    }

    [Fact]
    public void SceneDslAbstractionsNeverReachTheGeneratedCode(){
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "name": "Root",
                "components": [ { "type": "Sample.Transform", "members": { "X": 1.0 } } ],
                "children": [
                  { "name": "Child", "components": [ { "type": "Sample.Light", "members": { "Intensity": 3.0 } } ] }
                ]
              }
            }
            """);

        // Runtimeで「Nodeグラフを作る → object[]を走査する → 型を判定する → AddComponentする」は行わない。
        Assert.DoesNotContain("Entities.Node",Text);
        Assert.DoesNotContain("object[]",Text);

        // ISceneDefinition(SGが実装するRuntime側のインターフェース)は現れてよいので、語境界で区別する。
        Assert.DoesNotMatch(new Regex(@"IScene\b"),Text);
        Assert.Contains("ISceneDefinition",Text);
    }

    [Fact]
    public void GeneratesAnInstantiateThatCallsArchetypeCreateDirectly(){
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "name": "Root",
                "components": [ { "type": "Sample.Transform", "members": { "X": 1.0 } } ]
              }
            }
            """);

        Assert.Contains("public static global::CSharpGameEngine.Entities.SceneInstance Instantiate(ref global::CSharpGameEngine.Entities.World World) {",Text);
        Assert.Contains("global::CSharpGameEngine.Entities.SceneInstance Instance = World.BeginSceneInstance();",Text);
        Assert.Contains(".Create(ref World, new global::Sample.Transform() { X = 1f })",Text);
        Assert.Contains("World.AttachToScene(Instance, ",Text);
        Assert.Contains("World.EndSceneInstance(Instance, Node);",Text);
        Assert.Contains("return Instance;",Text);
    }

    [Fact]
    public void EmitsAPrivateConstructorSoTheSceneCannotBeInstantiated(){
        var Text=Generate("""
            { "namespace": "Game", "className": "MainScene", "root": { "name": "Root" } }
            """);

        Assert.Contains("private MainScene() {",Text);
    }

    [Fact]
    public void ImplementsISceneDefinitionExplicitlySoInstantiateSceneCanDispatchWithoutReflection(){
        var Text=Generate("""
            { "namespace": "Game", "className": "MainScene", "root": { "name": "Root" } }
            """);

        Assert.Contains("partial class MainScene : global::CSharpGameEngine.Entities.ISceneDefinition {",Text);
        Assert.Contains("static global::CSharpGameEngine.Entities.SceneInstance global::CSharpGameEngine.Entities.ISceneDefinition.Instantiate(ref global::CSharpGameEngine.Entities.World World) {",Text);
    }

    [Fact]
    public void ChildNodesGetParentAppendedAutomatically(){
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "components": [ { "type": "Sample.Transform", "members": {} } ],
                "children": [ { "components": [ { "type": "Sample.Light", "members": {} } ] } ]
              }
            }
            """);

        Assert.Contains("new global::CSharpGameEngine.Entities.Parent { Value = Node }",Text);
    }

    [Fact]
    public void ComponentlessNodeGetsSceneNodeTag(){
        // Archetypeは最低1つのComponentを要求するため、空ノードにはタグが入る。
        var Text=Generate("""
            { "namespace": "Game", "className": "MainScene", "root": { "name": "Empty" } }
            """);

        Assert.Contains(".Create(ref World, default)",Text);
    }

    [Fact]
    public void LocalNamesComeFromTheNodePathNotTheName(){
        // Nameは識別子である保証がまったく無い(省略可能で、空白も日本語も入る)。
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "name": "ルート ノード",
                "children": [ { "name": "子 1" }, { "name": "子 2" } ]
              }
            }
            """);

        Assert.Contains("Entity Node = World.AttachToScene",Text);
        Assert.Contains("Entity Node_0 = World.AttachToScene",Text);
        Assert.Contains("Entity Node_1 = World.AttachToScene",Text);
        Assert.Contains("// [(root)] \"ルート ノード\"",Text);
    }

    [Fact]
    public void SceneEntityCountMatchesTheNodeTotal(){
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "children": [
                  { "children": [ {} ] },
                  {}
                ]
              }
            }
            """);

        Assert.Contains("public const int SceneEntityCount = 4;",Text);
    }

    [Fact]
    public void ValuesAreFullyQualifiedSoNoUsingsAreNeededInGeneratedCode(){
        var Text=Generate("""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": { "components": [ { "type": "Sample.Transform", "members": { "X": 1.5 } } ] }
            }
            """);

        Assert.Contains("new global::Sample.Transform() { X = 1.5f }",Text);
        Assert.DoesNotContain("using ",Text);
    }

    [Fact]
    public void InvalidSceneJsonEmitsNothing(){
        // 'className'が無いJSONはSCENE200(InvalidSceneJson)として拒否され、Scene.g.csは一切生成されない。
        GeneratorDriverRunResult Result=GameSystemGeneratorTestHelper.RunGeneratorWithSceneJson(
            [Components],[("Scenes/MainScene.scene.json","""{ "root": { "name": "Root" } }""")]);

        Assert.DoesNotContain(Result.Results[0].GeneratedSources,static S=>S.HintName=="Game.MainScene.Scene.g.cs");
    }

    [Fact]
    public void GeneratorIsDeterministicAcrossTwoIndependentRuns(){
        const string SceneJson="""
            {
              "namespace": "Game",
              "className": "MainScene",
              "root": {
                "components": [ { "type": "Sample.Transform", "members": { "X": 1.0 } } ],
                "children": [ { "components": [ { "type": "Sample.Light", "members": { "Intensity": 2.0 } } ] } ]
              }
            }
            """;

        Assert.Equal(Generate(SceneJson),Generate(SceneJson));
    }
}
