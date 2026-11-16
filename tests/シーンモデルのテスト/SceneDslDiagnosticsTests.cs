namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// SCENE100〜116の網羅。<b>1診断につき最低1テスト</b>を置き、「C#として合法でもScene DSLとして不正」な
/// ものが確実に落ちることを保証する(ADR-0040 G-2)。
///
/// <para>
/// SCENE114(Scene宣言外からのDSL型参照)・SCENE117(<c>[StartupScene]</c>が<c>IScene</c>実装でない)・
/// SCENE118(Startup Orderの重複)はクラス単体では判定できず、コンパイル全体を見る
/// <c>SceneDslAnalyzer</c>(Phase 2)とGenerator(Phase 4)の責務になる。それぞれのPhaseで検証する。
/// </para>
/// </summary>
public sealed class SceneDslDiagnosticsTests {
    [Fact]
    public void Scene100_NonPartialClass(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public class MainScene:IScene {
                                                      public Node Root=>new() { Name="Root" };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE100");
        Assert.Null(Model.Root);
    }

    [Fact]
    public void Scene100_GenericClass(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene<T>:IScene {
                                                      public Node Root=>new() { Name="Root" };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE100");
    }

    [Fact]
    public void Scene100_Struct(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial struct MainScene:IScene {
                                                      public Node Root=>new() { Name="Root" };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE100");
    }

    [Fact]
    public void Scene101_MultiStatementGetter(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root {
                                                          get {
                                                              Node Built=new Node { Name="Root" };
                                                              return Built;
                                                          }
                                                      }
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE101");
    }

    [Fact]
    public void Scene101_MissingRootProperty(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Other=>new Node { Name="Root" };
                                                      Node IScene.Root=>throw new System.NotImplementedException();
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertContainsDiagnostic(Model,"SCENE103");
    }

    [Fact]
    public void Scene102_DeclaredConstructor(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public MainScene(){
                                                      }
                                                      public Node Root=>new() { Name="Root" };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE102");
    }

    [Fact]
    public void Scene102_GeneratorEmittedConstructorIsIgnoredByLiveCompilation(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      SceneAnalysisTestHelper.CommonComponents,
                                                      """
                                                      // <auto-generated/>
                                                      namespace Game;
                                                      public partial class MainScene {
                                                          private MainScene(){
                                                          }
                                                      }
                                                      """,
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Name="Root" };
                                                      }
                                                      """,
                                                  ]);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Root",Model.Root!.Name);
    }

    [Fact]
    public void Scene103_RootIsNotANodeCreation(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      private readonly Node Backing=null!;
                                                      public Node Root=>this.Backing;
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE103");
    }

    [Fact]
    public void Scene104_UnknownNodeMember(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Tag="x" };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE104");
    }

    [Fact]
    public void Scene105_NonConstantName(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Name=System.DateTime.Now.ToString() };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE105");
    }

    [Fact]
    public void Scene106_ComponentsIsNotACollectionExpression(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      private static readonly object[]Empty=[];
                                                      public Node Root=>new() { Components=Empty };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE106");
    }

    [Fact]
    public void Scene106_ChildrenIsNotACollectionExpression(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      private static readonly Node[]None=[];
                                                      public Node Root=>new() { Children=None };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE106");
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("new NotAComponent()")]
    [InlineData("new Node()")]
    public void Scene107_ComponentElementIsNotAComponentStruct(string Element){
        var Model=SceneAnalysisTestHelper.Analyze($$"""
                                                    using CSharpGameEngine.Entities;
                                                    using Sample;
                                                    namespace Game;
                                                    public partial class MainScene:IScene {
                                                        public Node Root=>new() { Components=[{{Element}}] };
                                                    }
                                                    """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE107");
    }

    [Fact]
    public void Scene108_DuplicateComponentTypeInOneNode(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() {
                                                          Name="Sun",
                                                          Components=[
                                                              new DirectionalLight { Intensity=1f },
                                                              new DirectionalLight { Intensity=2f },
                                                          ],
                                                      };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE108");
        Assert.Single(Model.Root!.Components);
    }

    [Fact]
    public void Scene109_ChildElementIsNotANode(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Children=[new DirectionalLight()] };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE109");
    }

    [Fact]
    public void Scene110_CastInsideAComponentInitializer(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Components=[new Label { Text=(string)(object)"x" }] };
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE110");
    }

    [Fact]
    public void Scene113_SceneFactoryOnAnInstanceMethod(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      SceneAnalysisTestHelper.CommonComponents,
                                                      """
                                                      namespace Sample;
                                                      using CSharpGameEngine.Authoring;
                                                      public sealed class Holder {
                                                          [SceneFactory]
                                                          public System.Numerics.Vector3 Make()=>default;
                                                      }
                                                      """,
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      using Sample;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Components=[new Transform { Position=new Holder().Make() }] };
                                                      }
                                                      """,
                                                  ]);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE113");
    }

    [Theory]
    [InlineData("new Parent()")]
    [InlineData("new SceneNodeTag()")]
    public void Scene115_ReservedComponentTypes(string Element){
        var Model=SceneAnalysisTestHelper.Analyze($$"""
                                                    using CSharpGameEngine.Entities;
                                                    namespace Game;
                                                    public partial class MainScene:IScene {
                                                        public Node Root=>new() { Components=[{{Element}}] };
                                                    }
                                                    """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE115");
    }

    /// <summary>
    /// M0053: <c>IRuntimeComponent</c>を実装するComponentはエンジンが実行時に埋めるためのもので、
    /// 随伴先の作者Componentを宣言すればSGが自動付与する。明示すると重複するので弾く。
    /// </summary>
    [Fact]
    public void Scene119_RuntimeComponentDeclaredExplicitly(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      """
                                                      namespace Sample;
                                                      public struct Light { public float Intensity; }
                                                      public struct LightRuntime:CSharpGameEngine.Entities.IRuntimeComponent<Light> { public int Handle; }
                                                      """,
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      using Sample;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Components=[new LightRuntime()] };
                                                      }
                                                      """,
                                                  ]);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE119");
    }

    /// <summary>随伴先を持たない(単独で生成される)実行時Componentも同じく書けない。</summary>
    [Fact]
    public void Scene119_StandaloneRuntimeComponentDeclaredExplicitly(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      """
                                                      namespace Sample;
                                                      public struct Standalone:CSharpGameEngine.Entities.IRuntimeComponent { public int Handle; }
                                                      """,
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      using Sample;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Components=[new Standalone()] };
                                                      }
                                                      """,
                                                  ]);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE119");
    }

    [Fact]
    public void Scene116_NestedSceneClass(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  namespace Game;
                                                  public partial class Outer {
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Name="Root" };
                                                      }
                                                  }
                                                  """);

        SceneAnalysisTestHelper.AssertSingleDiagnostic(Model,"SCENE116");
    }
}
