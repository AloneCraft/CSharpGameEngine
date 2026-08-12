using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneModelRenderObjectBuilder"/>'s node-walking/value-extraction logic (ADR-0040 Phase
/// 9), against a real analysed <see cref="SceneModel"/> (via <see cref="SceneAnalysisTestHelper"/>,
/// the same shared analyser the Source Generator and Hierarchy use) rather than hand-built fixtures -
/// same rationale as <see cref="SceneNodeViewModelTests"/>. The mesh-resolver dependency is injected
/// as a plain delegate so no bridge process/GPU is involved.
/// </summary>
public sealed class SceneModelRenderObjectBuilderTests {
    /// <summary>
    /// Declares components under the exact fully-qualified names <see cref="SceneModelRenderObjectBuilder"/>
    /// hardcodes (<c>UserEcsProject.Transform</c>, <c>CSharpGameEngine.Runtime.Windows.PrimitiveMesh</c>)
    /// - the shared <c>SceneAnalysisTestHelper.CommonComponents</c> fixture declares its own
    /// same-shaped types under a different namespace (<c>Sample</c>), which would not match.
    /// </summary>
    private const string Components="""
        namespace UserEcsProject {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }

        namespace CSharpGameEngine.Runtime.Windows {
            public struct PrimitiveMesh { public PrimitiveType Type; }
            public struct StaticMesh { public string AssetPath; }
            public enum PrimitiveType { Plane, Box, Sphere, Cylinder, Cone, Torus, Disk, Capsule }
        }

        namespace Game {
            using CSharpGameEngine.Authoring;
            [SceneAssets]
            public static class Assets { public const string Stage="Assets/Stage/Test.glb"; }
        }
        """;

    /// <summary>Matches <see cref="GizmoMeshFactory"/>'s default tint - the marker this test uses to recognize a gizmo fallback without depending on exact vertex geometry.</summary>
    private static readonly Vector4 GizmoTint=new(0.4f,0.65f,0.95f,1f);

    private static SceneModel Analyze(string SceneSource)=>SceneAnalysisTestHelper.Analyze([Components,SceneSource]);

    [Fact]
    public void Build_NodeWithTransform_ExtractsPositionAndScaleIntoWorldMatrix(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            using UserEcsProject;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[
                    new Transform { PositionX=3f,PositionY=4f,PositionZ=5f,RotationW=1f,ScaleX=2f,ScaleY=2f,ScaleZ=2f },
                ]};
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        SceneRenderObject Object=Assert.Single(Result);
        Assert.True(Matrix4x4.Decompose(Object.World,out Vector3 Scale,out _,out Vector3 Translation));
        Assert.Equal(new Vector3(3f,4f,5f),Translation);
        Assert.Equal(new Vector3(2f,2f,2f),Scale);
    }

    [Fact]
    public void Build_NodeWithoutTransform_UsesIdentityWorld(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(Matrix4x4.Identity,Assert.Single(Result).World);
    }

    [Fact]
    public void Build_TransformWithUnsetRotation_FallsBackToIdentityRotationInsteadOfNaN(){
        // RotationX/Y/Z/W all default to 0f (never assigned) - a zero-length quaternion, not a valid
        // rotation. CreateFromQuaternion on it would poison the whole matrix with NaN.
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            using UserEcsProject;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new Transform { PositionX=1f }] };
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Matrix4x4 World=Assert.Single(Result).World;
        Assert.True(Matrix4x4.Decompose(World,out _,out Quaternion Rotation,out Vector3 Translation));
        Assert.False(float.IsNaN(World.M11));
        Assert.Equal(Quaternion.Identity,Rotation);
        Assert.Equal(1f,Translation.X);
    }

    [Fact]
    public void Build_NodeWithPrimitiveMesh_ResolvesThroughTheInjectedResolverByEnumMemberName(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new PrimitiveMesh { Type=PrimitiveType.Sphere }] };
            }
            """);

        SimpleMesh Marker=new() { Positions=[Vector3.Zero],Normals=[Vector3.UnitY],Indices=[0,0,0],BaseColorFactor=new Vector4(1,0,0,1),BoundsCenter=Vector3.Zero,BoundsRadius=1f };
        string?RequestedName=null;

        var Result=SceneModelRenderObjectBuilder.Build(Model,Name=>{
            RequestedName=Name;
            return Marker;
        });

        Assert.Equal("Sphere",RequestedName);
        Assert.Same(Marker,Assert.Single(Result).Mesh);
    }

    [Fact]
    public void Build_NodeWithoutPrimitiveMesh_FallsBackToGizmoWithoutCallingTheResolver(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root" };
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>throw new Xunit.Sdk.XunitException("resolver should not be called for a node with no PrimitiveMesh component"));

        Assert.Equal(GizmoTint,Assert.Single(Result).Mesh.BaseColorFactor);
    }

    [Fact]
    public void Build_NodeWithStaticMesh_ResolvesSceneAssetsConstPath(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new StaticMesh { AssetPath=Assets.Stage }] };
            }
            """);
        SimpleMesh Marker=new() { Positions=[Vector3.Zero],Normals=[Vector3.UnitY],Indices=[0,0,0],BaseColorFactor=Vector4.One,BoundsCenter=Vector3.Zero,BoundsRadius=1f };
        string?RequestedPath=null;

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null,Path=>{ RequestedPath=Path;return Marker; });

        Assert.Equal("Assets/Stage/Test.glb",RequestedPath);
        Assert.Same(Marker,Assert.Single(Result).Mesh);
    }

    [Fact]
    public void Build_ResolverReturnsNull_FallsBackToGizmo(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Components=[new PrimitiveMesh { Type=PrimitiveType.Box }] };
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(GizmoTint,Assert.Single(Result).Mesh.BaseColorFactor);
    }

    [Fact]
    public void Build_NodeIdentity_IsNodePath(){
        SceneModel Model=Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Name="Root",Children=[new Node { Name="Child" }] };
            }
            """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(2,Result.Count);
        Assert.Equal(NodePath.Root,Result[0].NodeId);
        Assert.Equal(NodePath.Root.Append(0),Result[1].NodeId);
    }
}
