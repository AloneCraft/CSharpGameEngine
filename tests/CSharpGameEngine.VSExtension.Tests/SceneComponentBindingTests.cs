using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneComponentBindings"/> が「実シーンで使われている完全修飾名」を認識することを固定する。
/// M0020 Phase 0 まで builder は <c>global::UserEcsProject.Transform</c> をハードコードしており、
/// 実シーンの Transform が一切認識されず全ノードが原点に重なって描画されていた。
/// </summary>
public sealed class SceneComponentBindingTests {
    private const string Components="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
            public struct PrimitiveMesh { public PrimitiveType Type; }
            public enum PrimitiveType { Plane, Box, Sphere }
        }

        namespace UserEcsProject {
            public struct Transform { public float PositionX; }
        }
        """;

    [Fact]
    public void Bindings_RecognizeTheEngineStandardTransform(){
        Assert.True(SceneComponentBindings.TryGetRole(
            "global::CSharpGameEngine.Runtime.Windows.Transform",out SceneComponentRole Role));
        Assert.Equal(SceneComponentRole.LocalTransform,Role);
    }

    [Fact]
    public void Bindings_DoNotRecognizeTheRetiredUserEcsProjectTransform(){
        // 互換のため両方登録すると、原点固まりバグが直ったかどうかを判定できなくなる。
        Assert.False(SceneComponentBindings.TryGetRole("global::UserEcsProject.Transform",out _));
    }

    [Fact]
    public void FindComponent_ReturnsTheComponentBoundToTheRequestedRole(){
        SceneNode Root=AnalyzeRoot("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    new PrimitiveMesh { Type=PrimitiveType.Box },
                    new Transform { PositionX=1f,RotationW=1f },
                ]};
            }
            """);

        SceneComponent?Transform=SceneComponentBindings.FindComponent(Root,SceneComponentRole.LocalTransform);

        Assert.NotNull(Transform);
        Assert.Equal("global::CSharpGameEngine.Runtime.Windows.Transform",Transform!.TypeFullyQualifiedName);
    }

    [Fact]
    public void FindComponent_NodeWithoutTheRole_ReturnsNull(){
        SceneNode Root=AnalyzeRoot("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new PrimitiveMesh { Type=PrimitiveType.Box }]};
            }
            """);

        Assert.Null(SceneComponentBindings.FindComponent(Root,SceneComponentRole.LocalTransform));
    }

    [Fact]
    public void Bindings_MapEveryRoleToAtLeastOneFullyQualifiedName(){
        // 消費者のない Role を宣言した(= dead abstraction を作った)ことを機械的に検出する。
        // Phase 6 で Camera/ライトを足すときは、enum メンバーと表の行を同時に追加すること。
        foreach(SceneComponentRole Role in Enum.GetValues(typeof(SceneComponentRole)).Cast<SceneComponentRole>()){
            Assert.Contains(SceneComponentBindings.All,Binding=>Binding.Role==Role);
        }
    }

    private static SceneNode AnalyzeRoot(string SceneSource){
        SceneModel Model=SceneAnalysisTestHelper.Analyze([Components,SceneSource]);
        Assert.NotNull(Model.Root);
        return Model.Root!;
    }
}
