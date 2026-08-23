using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// Scene DSLから生成される<c>Instantiate</c>の形(ADR-0040 H-1)。
///
/// <para>
/// 最重要の検証は<b>「Scene authoringの抽象が生成コードに現れないこと」</b>である。<c>Node</c>や
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

    private static string Generate(string SceneSource,string HintName="Game.MainScene.Scene.g.cs"){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              [.. GameSystemGeneratorTestHelper.SceneDslSources,Components,SceneSource]);
        return GameSystemGeneratorTestHelper.GeneratedText(Result,HintName);
    }

    [Fact]
    public void SceneDslAbstractionsNeverReachTheGeneratedCode(){
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="Root",
                    Components=[new Transform { X=1f }],
                    Children=[new Node { Name="Child",Components=[new Light { Intensity=3f }] }],
                };
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
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root",Components=[new Transform { X=1f }] };
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
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        Assert.Contains("private MainScene() {",Text);
    }

    [Fact]
    public void ImplementsISceneDefinitionExplicitlySoInstantiateSceneCanDispatchWithoutReflection(){
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        Assert.Contains("partial class MainScene : global::CSharpGameEngine.Entities.ISceneDefinition {",Text);
        Assert.Contains("static global::CSharpGameEngine.Entities.SceneInstance global::CSharpGameEngine.Entities.ISceneDefinition.Instantiate(ref global::CSharpGameEngine.Entities.World World) {",Text);
    }

    [Fact]
    public void ChildNodesGetParentAppendedAutomatically(){
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Transform()],
                    Children=[new Node { Components=[new Light()] }],
                };
            }
            """);

        Assert.Contains("new global::CSharpGameEngine.Entities.Parent { Value = Node }",Text);
    }

    [Fact]
    public void ComponentlessNodeGetsSceneNodeTag(){
        // Archetypeは最低1つのComponentを要求するため、空ノードにはタグが入る。
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Empty" };
            }
            """);

        Assert.Contains(".Create(ref World, default)",Text);
    }

    [Fact]
    public void LocalNamesComeFromTheNodePathNotTheName(){
        // Nameは識別子である保証がまったく無い(省略可能で、空白も日本語も入る)。
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="ルート ノード",
                    Children=[new Node { Name="子 1" },new Node { Name="子 2" }],
                };
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
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Children=[
                        new Node { Children=[new Node()] },
                        new Node(),
                    ],
                };
            }
            """);

        Assert.Contains("public const int SceneEntityCount = 4;",Text);
    }

    [Fact]
    public void ValuesAreFullyQualifiedSoNoUsingsAreNeededInGeneratedCode(){
        var Text=Generate("""
            using CSharpGameEngine.Entities;
            using Xf=Sample.Transform;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Xf { X=1.5f }] };
            }
            """);

        Assert.Contains("new global::Sample.Transform() { X = 1.5f }",Text);
        Assert.DoesNotContain("using ",Text);
    }

    [Fact]
    public void SceneWithDiagnosticsEmitsNothing(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator(
                                                              [.. GameSystemGeneratorTestHelper.SceneDslSources,Components,
                                                                  """
                                                                  using CSharpGameEngine.Entities;
                                                                  namespace Game;
                                                                  public class MainScene:IScene {
                                                                      public Node Root=>new() { Name="Root" };
                                                                  }
                                                                  """]);

        Assert.DoesNotContain(Result.Results[0].GeneratedSources,static S=>S.HintName=="Game.MainScene.Scene.g.cs");
    }

    [Fact]
    public void GeneratorIsDeterministicAcrossTwoIndependentRuns(){
        const string Scene="""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=[new Transform { X=1f }],
                    Children=[new Node { Components=[new Light { Intensity=2f }] }],
                };
            }
            """;

        Assert.Equal(Generate(Scene),Generate(Scene));
    }
    [Fact]
    public void UserEcsRenderableScenesReceiveDeterministicSpatialBounds(){
        var Result=GameSystemGeneratorTestHelper.RunGenerator([
            .. GameSystemGeneratorTestHelper.SceneDslSources,
            """
            using CSharpGameEngine.Entities;
            namespace CSharpGameEngine.Runtime.Windows;
            public struct Transform { }
            public struct StaticMesh { }
            """,
            """
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace UserEcsProject.Scenes;
            public partial class SampleScene:IScene {
                public Node Root=>new() { Components=[new Transform(),new StaticMesh()] };
            }
            """,
        ]);
        var Text=GameSystemGeneratorTestHelper.GeneratedText(Result,"UserEcsProject.Scenes.SampleScene.Scene.g.cs");

        Assert.Contains("global::CSharpGameEngine.Entities.SpatialBounds.UnitCube",Text);
        Assert.Contains("global::CSharpGameEngine.Runtime.Windows.Transform",Text);
        Assert.Contains("global::CSharpGameEngine.Runtime.Windows.StaticMesh",Text);
    }
}
