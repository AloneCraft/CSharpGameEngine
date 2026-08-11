namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// <see cref="SceneDeclarationAnalyzer"/>の正常系。「C#がScene用の宣言DSLとして成立している」ことを
/// 直接検証する(ADR-0040)。
/// </summary>
public sealed class SceneDeclarationAnalyzerTests {
    [Fact]
    public void RootOnly_ProducesRootNodeWithNoComponents(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.NotNull(Model.Root);
        Assert.Equal("Root",Model.Root!.Name);
        Assert.Empty(Model.Root.Components);
        Assert.Empty(Model.Root.Children);
        Assert.True(Model.Root.Path.IsRoot);
        Assert.Equal(1,Model.TotalNodeCount);
        Assert.Equal("Game.MainScene",Model.ClassFullyQualifiedName);
        Assert.Equal("Game",Model.Namespace);
    }

    [Fact]
    public void Components_AreReadInSourceOrder(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            using System.Numerics;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="Floor",
                    Components=[
                        new Transform { Position=new Vector3(0f,0f,0f),Scale=new Vector3(10f,1f,10f) },
                        new PrimitiveMesh { Type=PrimitiveType.Cube },
                        new MaterialRef(),
                    ],
                };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(
            ["global::Sample.Transform","global::Sample.PrimitiveMesh","global::Sample.MaterialRef"],
            Model.Root!.Components.Select(static C=>C.TypeFullyQualifiedName));
    }

    [Fact]
    public void NestedChildren_GetSequentialNodePaths(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Children=[
                        new Node { Name="A" },
                        new Node {
                            Name="B",
                            Children=[
                                new Node { Name="B0" },
                                new Node { Name="B1" },
                            ],
                        },
                    ],
                };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(5,Model.TotalNodeCount);
        Assert.Equal("(root)",Model.Root!.Path.ToDisplayString());
        Assert.Equal("0",Model.Root.Children[0].Path.ToDisplayString());
        Assert.Equal("1",Model.Root.Children[1].Path.ToDisplayString());
        Assert.Equal("1/0",Model.Root.Children[1].Children[0].Path.ToDisplayString());
        Assert.Equal("1/1",Model.Root.Children[1].Children[1].Path.ToDisplayString());
    }

    [Fact]
    public void EnumerateNodes_WalksDepthFirstInDeclarationOrder(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="R",
                    Children=[
                        new Node { Name="A",Children=[new Node { Name="A0" }] },
                        new Node { Name="B" },
                    ],
                };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal(["R","A","A0","B"],Model.EnumerateNodes().Select(static N=>N.Name));
    }

    [Fact]
    public void FindNode_ResolvesByPathAndRejectsOutOfRange(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Child" }] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Child",Model.FindNode(NodePath.Root.Append(0))?.Name);
        Assert.Null(Model.FindNode(NodePath.Root.Append(7)));
    }

    [Fact]
    public void NameOmitted_IsNullAndDisplayNameFallsBackToPath(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node()] };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Null(Model.Root!.Children[0].Name);
        Assert.Equal("0",Model.Root.Children[0].DisplayName);
    }

    [Fact]
    public void ArrayInitializerSyntax_IsAcceptedLikeCollectionExpression(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Components=new object[] { new DirectionalLight { Intensity=3f } },
                    Children=new Node[] { new Node { Name="Child" } },
                };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Single(Model.Root!.Components);
        Assert.Single(Model.Root.Children);
    }

    [Fact]
    public void ExplicitInterfaceImplementation_IsResolved(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                Node IScene.Root=>new() { Name="Explicit" };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Explicit",Model.Root!.Name);
    }

    [Fact]
    public void GetAccessorWithSingleReturn_IsAccepted(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root { get { return new Node { Name="Returned" }; } }
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Returned",Model.Root!.Name);
    }

    [Fact]
    public void GetAccessorWithExpressionBody_IsAccepted(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root { get=>new Node { Name="Arrow" }; }
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Equal("Arrow",Model.Root!.Name);
    }

    [Fact]
    public void StartupSceneAttribute_IsReadWithOrder(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            [StartupScene(Order=3)]
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.True(Model.IsStartupScene);
        Assert.Equal(3,Model.StartupOrder);
    }

    [Fact]
    public void WithoutStartupSceneAttribute_IsNotAStartupScene(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        Assert.False(Model.IsStartupScene);
        Assert.Equal(0,Model.StartupOrder);
    }

    [Fact]
    public void GlobalNamespaceScene_HasNullNamespace(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.Null(Model.Namespace);
        Assert.Equal("MainScene",Model.ClassFullyQualifiedName);
    }

    [Fact]
    public void SourceLocations_PointAtTheDeclaringExpressions(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() {
                    Name="Root",
                    Components=[new DirectionalLight { Intensity=3f }],
                    Children=[new Node { Name="Child" }],
                };
            }
            """);

        SceneAnalysisTestHelper.AssertNoDiagnostics(Model);
        Assert.NotNull(Model.Root!.InitializerLocation);
        Assert.NotNull(Model.Root.NameValueLocation);
        Assert.NotNull(Model.Root.ComponentsListLocation);
        Assert.NotNull(Model.Root.ChildrenListLocation);
        Assert.True(Model.Root.Components[0].Location.Span.Length>0);
    }
}
