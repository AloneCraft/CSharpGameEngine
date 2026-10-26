using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="SceneModelRenderObjectBuilder"/> のノード走査と値抽出ロジック (ADR-0040 フェーズ 9) を、手作りフィクスチャではなく、実際に解析された <see cref="SceneModel"/> を使って検証する（<see cref="SceneAnalysisTestHelper"/> 経由で、Source Generator と Hierarchy が使うものと同じ共有アナライザーを使用）。<see cref="SceneNodeViewModelTests"/> と同じ理由による。メッシュ解決の依存先は通常のデリゲートとして注入し、ブリッジプロセスや GPU を使わない。
/// </summary>
public sealed class SceneModelRenderObjectBuilderTests {
    /// <summary>
    /// <see cref="SceneComponentBindings"/> がバインドする完全修飾名（実エンジンの語彙に合わせた <c>CSharpGameEngine.Runtime.Windows.*</c> すべて）でコンポーネントを宣言する。共有の <c>SceneAnalysisTestHelper.CommonComponents</c> フィクスチャは別の名前空間 (<c>Sample</c>) に同じ形の型を宣言するため、一致しない。
    ///
    /// <para>
    /// M0020 フェーズ 0 までは、ビルダーが <c>UserEcsProject.Transform</c> をハードコードしていた。フィクスチャも合わせてその名前空間に Transform を宣言していたため、実際のすべてのシーンが単位ワールドで描画されてもテストは成功していた。したがってフィクスチャはバインディングテーブルに従うべきであり、その逆ではない。
    /// </para>
    /// </summary>
    private const string Components="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
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

    /// <summary><see cref="GizmoMeshFactory"/> の既定の tint に一致する。厳密な頂点形状に依存せず、テストでギズモの代替表示を識別するマーカー。</summary>
    private static readonly Vector4 GizmoTint=new(0.4f,0.65f,0.95f,1f);

    private static SceneModel Analyze(string SceneSource)=>SceneAnalysisTestHelper.Analyze([Components,SceneSource]);

    [Fact]
    public void Build_NodeWithTransform_ExtractsPositionAndScaleIntoWorldMatrix(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[
                                  new Transform { PositionX=3f,PositionY=4f,PositionZ=5f,RotationW=1f,ScaleX=2f,ScaleY=2f,ScaleZ=2f },
                              ]};
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        var Object=Assert.Single(Result);
        Assert.True(Matrix4x4.Decompose(Object.World,out var Scale,out _,out var Translation));
        Assert.Equal(new Vector3(3f,4f,5f),Translation);
        Assert.Equal(new Vector3(2f,2f,2f),Scale);
    }

    [Fact]
    public void Build_NodeWithoutTransform_UsesIdentityWorld(){
        var Model=Analyze("""
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
        // RotationX/Y/Z/W はすべて既定で 0f（未設定）となる。これは有効な回転ではなく、長さ 0 のクォータニオンである。
        // それに対して CreateFromQuaternion を実行すると、行列全体が NaN で汚染される。
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[new Transform { PositionX=1f }] };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        var World=Assert.Single(Result).World;
        Assert.True(Matrix4x4.Decompose(World,out _,out var Rotation,out var Translation));
        Assert.False(float.IsNaN(World.M11));
        Assert.Equal(Quaternion.Identity,Rotation);
        Assert.Equal(1f,Translation.X);
    }

    [Fact]
    public void Build_NodeWithPrimitiveMesh_ResolvesThroughTheInjectedResolverByEnumMemberName(){
        var Model=Analyze("""
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
        var Model=Analyze("""
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
        var Model=Analyze("""
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
    public void Build_PrimitiveResolverReturnsNull_RejectsTheDeclaredMesh(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[new PrimitiveMesh { Type=PrimitiveType.Box }] };
                          }
                          """);

        var Exception=Assert.Throws<InvalidOperationException>(
            ()=>SceneModelRenderObjectBuilder.Build(Model,static _=>null));

        Assert.Contains("Box",Exception.Message,StringComparison.Ordinal);
    }

    [Fact]
    public void Build_StaticMeshResolverReturnsNull_RejectsTheDeclaredAsset(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() { Components=[new StaticMesh { AssetPath=Assets.Stage }] };
                          }
                          """);

        var Exception=Assert.Throws<FileNotFoundException>(()=>
            SceneModelRenderObjectBuilder.Build(Model,static _=>null,static _=>null));

        Assert.Equal("Assets/Stage/Test.glb",Exception.FileName);
    }

    [Fact]
    public void Build_NodeIdentity_IsNodePath(){
        var Model=Analyze("""
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

    [Fact]
    public void Build_ChildNode_ComposesLocalTransformWithParentWorld(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Components=[new Transform { PositionX=10f,RotationW=1f }],
                                  Children=[new Node { Components=[new Transform { PositionY=3f,RotationW=1f }] }],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(new Vector3(10f,0f,0f),Result[0].World.Translation);
        Assert.Equal(new Vector3(10f,3f,0f),Result[1].World.Translation);
    }

    [Fact]
    public void Build_GrandchildNode_FoldsTheWholeAncestorChain(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Components=[new Transform { PositionX=1f,RotationW=1f }],
                                  Children=[new Node {
                                      Components=[new Transform { PositionX=2f,RotationW=1f }],
                                      Children=[new Node { Components=[new Transform { PositionX=4f,RotationW=1f }] }],
                                  }],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(3,Result.Count);
        Assert.Equal(7f,Result[2].World.Translation.X,3);
    }

    [Fact]
    public void Build_ChildWithoutTransform_InheritsTheParentWorld(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Components=[new Transform { PositionZ=5f,RotationW=1f }],
                                  Children=[new Node { Name="NoTransform" }],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Equal(Result[0].World,Result[1].World);
        Assert.Equal(new Vector3(0f,0f,5f),Result[1].World.Translation);
    }

    [Fact]
    public void Build_RotatedParentWithTranslatedChild_PlacesTheChildInTheParentsRotatedFrame(){
        // 親をY軸+90°(RotationY=RotationW=sin/cos 45°)回した下で子を+Xへ1動かすと、子は親の回転後の
        // 座標系に乗るので world translation は概ね (0,0,-1) になる。乗算順を ParentWorld*Local に
        // 取り違えるとここが (1,0,0) のままになるため、順序を固定する唯一のテスト。
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Components=[new Transform { RotationY=0.7071068f,RotationW=0.7071068f }],
                                  Children=[new Node { Components=[new Transform { PositionX=1f,RotationW=1f }] }],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        var Child=Result[1].World.Translation;
        Assert.Equal(0f,Child.X,3);
        Assert.Equal(0f,Child.Y,3);
        Assert.Equal(-1f,Child.Z,3);
    }

    [Fact]
    public void Build_DeepChildren_PreservesPreOrderNodePathOrdering(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Name="Root",
                                  Children=[
                                      new Node { Name="A",Children=[new Node { Name="A0" }] },
                                      new Node { Name="B" },
                                  ],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(Model,static _=>null);

        Assert.Collection(Result,
            Object=>Assert.Equal(NodePath.Root,Object.NodeId),
            Object=>Assert.Equal(NodePath.Root.Append(0),Object.NodeId),
            Object=>Assert.Equal(NodePath.Root.Append(0).Append(0),Object.NodeId),
            Object=>Assert.Equal(NodePath.Root.Append(1),Object.NodeId));
    }

    [Fact]
    public void Build_SkySphereStaticMesh_IsExcludedFromAutomaticFraming(){
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Children=[
                                      new Node { Components=[new StaticMesh { AssetPath="Assets/Stage/SM_SkySphere.glb" }] },
                                      new Node { Components=[new StaticMesh { AssetPath="Assets/Stage/SM_Floor.glb" }] },
                                  ],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(
            Model,static _=>null,static _=>GizmoMeshFactory.Create());

        Assert.False(Result[1].IncludeInBounds);
        Assert.True(Result[2].IncludeInBounds);
        // M0042: Sky domeはピッキング対象外(「何もない空間」のクリックが背景に当たって
        // 選択解除にならない不具合の原因だった)。Result[0]はRoot自身(メッシュ未宣言)で
        // これも対象外 — 次のテストで別途固定する。
        Assert.False(Result[1].IsPickable);
        Assert.True(Result[2].IsPickable);
    }

    [Fact]
    public void Build_NodeWithoutDeclaredMesh_IsExcludedFromPicking(){
        // M0042: StaticMesh/PrimitiveMeshを持たないノード(Root自身や整理用の空グループノード)は
        // GizmoMeshFactoryのプレースホルダー立方体で代替描画されるが、ワールド原点付近に描画され
        // ステージの実オブジェクトへ紛れ込みやすいため、クリック・ドラッグの対象から外す。
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          using CSharpGameEngine.Runtime.Windows;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>new() {
                                  Children=[
                                      new Node { Name="EmptyGroup" },
                                      new Node { Components=[new StaticMesh { AssetPath="Assets/Stage/SM_Floor.glb" }] },
                                  ],
                              };
                          }
                          """);

        var Result=SceneModelRenderObjectBuilder.Build(
            Model,static _=>null,static _=>GizmoMeshFactory.Create());

        Assert.False(Result[0].IsPickable); // Root自身。
        Assert.False(Result[1].IsPickable); // EmptyGroup(メッシュ未宣言)。
        Assert.True(Result[2].IsPickable); // 通常のStaticMeshノード。
    }

    [Fact]
    public void Build_ModelWithoutRoot_ReturnsEmptyList(){
        // Root を解析できないシーンでは空リストを返す。Preview Host/SceneHwndHost が0件を
        // 例外にしなくなった(M0020 Phase 0)ことの、GPU 非依存な唯一の証跡。
        var Model=Analyze("""
                          using CSharpGameEngine.Entities;
                          namespace Game;
                          public partial class MainScene:IScene {
                              public Node Root=>default;
                          }
                          """);

        Assert.Empty(SceneModelRenderObjectBuilder.Build(Model,static _=>null));
    }
    [Fact]
    public void ThumbnailModeOmitsEmptyGroupsButRetainsTheirTransformForRealChildren(){
        var Model=Analyze("""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new(){Components=[new Transform{PositionX=7f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f}],Children=[
                    new Node{Name="Empty"},
                    new Node{Components=[new StaticMesh{AssetPath="Assets/Actual.glb"}]}
                ]};
            }
            """);
        var Mesh=GizmoMeshFactory.Create();
        var Objects=SceneModelRenderObjectBuilder.Build(Model,static _=>null,_=>Mesh,IncludePlaceholders:false);
        var Actual=Assert.Single(Objects);Assert.Same(Mesh,Actual.Mesh);Assert.Equal(7f,Actual.World.M41);
        Assert.Throws<FileNotFoundException>(()=>SceneModelRenderObjectBuilder.Build(Model,static _=>null,static _=>null,IncludePlaceholders:false));
    }
}