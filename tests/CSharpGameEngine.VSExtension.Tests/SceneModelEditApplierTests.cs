using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0038/M0047: 予測・正本モデル層。最重要は「モデル層の適用結果が、ソース層で書き換えて再解析した結果と
/// 一致すること」を固定する契約テスト(<see cref="ModelApply_MatchesSourceApplyThenReanalyze"/>)。
/// ここが崩れると、画面が実際のソースと食い違ったまま気付けなくなる。
/// </summary>
public sealed class SceneModelEditApplierTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private const string Source="""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() { Children=[
                new Node {
                    Name="Cube", // 手書きの大事なコメント
                    Components=[new Transform { PositionX=1f,PositionY=2f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                },
                new Node {
                    Name="Sphere",
                    Components=[new Transform { PositionX=5f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                },
            ] };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void TryApply_SetTransform_UpdatesOnlyTheTargetNode(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");
        SceneTransformValue Value=new(new Vector3(9f,2f,0f),Quaternion.Identity,Vector3.One);

        bool Applied=SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new SetTransformOperation(Key,Cube,DocumentVersion:1,Value)],
            out SceneModel Predicted,out _);

        Assert.True(Applied);
        Assert.Equal(9f,ReadPosition(Predicted,"Cube").X);
        Assert.Equal(2f,ReadPosition(Predicted,"Cube").Y);
        Assert.Equal(5f,ReadPosition(Predicted,"Sphere").X);
    }

    [Fact]
    public void TryApply_DoesNotMutateTheBaseModel(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");
        SceneTransformValue Value=new(new Vector3(9f,9f,9f),Quaternion.Identity,Vector3.One);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new SetTransformOperation(Key,Cube,DocumentVersion:1,Value)],
            out _,out _));

        // Baseはimmutableなので予測を作っても一切変化してはならない。
        Assert.Equal(1f,ReadPosition(Base,"Cube").X);
        Assert.Equal(2f,ReadPosition(Base,"Cube").Y);
    }

    [Fact]
    public void TryApply_MultipleNodes_UpdatesEveryTarget(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        SceneTransformValue CubeValue=new(new Vector3(7f,0f,0f),Quaternion.Identity,Vector3.One);
        SceneTransformValue SphereValue=new(new Vector3(8f,0f,0f),Quaternion.Identity,Vector3.One);

        bool Applied=SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetTransformOperation(Key,FindId(Projection,"Cube"),DocumentVersion:1,CubeValue),
             new SetTransformOperation(Key,FindId(Projection,"Sphere"),DocumentVersion:1,SphereValue)],
            out SceneModel Predicted,out _);

        Assert.True(Applied);
        Assert.Equal(7f,ReadPosition(Predicted,"Cube").X);
        Assert.Equal(8f,ReadPosition(Predicted,"Sphere").X);
    }

    [Fact]
    public void TryApply_MissingMember_IsAddedToThePredictedModel(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        // SphereにはPositionYが書かれていない。ソース層は初期化子へ追記するので、モデル層も同じ結果になるべき。
        SceneTransformValue Value=new(new Vector3(5f,4f,0f),Quaternion.Identity,Vector3.One);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetTransformOperation(Key,FindId(Projection,"Sphere"),DocumentVersion:1,Value)],
            out SceneModel Predicted,out _));

        Assert.Equal(4f,ReadPosition(Predicted,"Sphere").Y);
    }

    [Fact]
    public void TryApply_UnsupportedOperation_ReturnsFalseWithoutPartialApplication(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");
        SceneTransformValue Value=new(new Vector3(9f,0f,0f),Quaternion.Identity,Vector3.One);

        // 対応済みの操作と非対応の操作(AddComponent)が混ざっていたら、丸ごと諦めなければならない(部分適用の禁止)。
        bool Applied=SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetTransformOperation(Key,Cube,DocumentVersion:1,Value),
             new AddComponentOperation(Key,Cube,DocumentVersion:1,"global::Game.Foo","new Foo()")],
            out SceneModel Predicted,out _);

        Assert.False(Applied);
        Assert.Same(Base,Predicted);
    }

    [Fact]
    public void TryApply_EmptyOperations_ReturnsFalse(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);

        Assert.False(SceneModelEditApplier.TryApply(Base,Projection,Reconciler,Key,[],out SceneModel Predicted,out _));
        Assert.Same(Base,Predicted);
    }

    [Fact]
    public void TryApply_NodeWithoutTransform_ReturnsFalse(){
        const string NoTransform="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Empty" }] };
            }
            """;
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=
            Analyze(NoTransform,IncludeTransformTypes:false);
        SceneTransformValue Value=new(new Vector3(1f,0f,0f),Quaternion.Identity,Vector3.One);

        Assert.False(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetTransformOperation(Key,FindId(Projection,"Empty"),DocumentVersion:1,Value)],
            out _,out _));
    }

    [Fact]
    public void TryApply_SetComponentMember_AcceptsLiteralExpressions(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);

        bool Applied=SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetComponentMemberOperation(
                Key,FindId(Projection,"Cube"),DocumentVersion:1,
                "global::CSharpGameEngine.Runtime.Windows.Transform","PositionX","6f")],
            out SceneModel Predicted,out _);

        Assert.True(Applied);
        Assert.Equal(6f,ReadPosition(Predicted,"Cube").X);
    }

    [Fact]
    public void TryApply_SetComponentMember_DeclinesNonLiteralExpressions(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);

        // 定数式はセマンティックモデル無しでは畳めないので、素直に諦めて従来経路へ落とす。
        Assert.False(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new SetComponentMemberOperation(
                Key,FindId(Projection,"Cube"),DocumentVersion:1,
                "global::CSharpGameEngine.Runtime.Windows.Transform","PositionX","12f*MathF.PI/180f")],
            out _,out _));
    }

    /// <summary>
    /// 契約テスト。モデル層の予測と、ソース層で書き換えて再解析した真実が一致することを固定する。
    /// componentの<c>EmitExpressionText</c>だけは予測では作り直せない(解析層のemit規則の再実装に
    /// なるため)ので、<c>ComponentChanged</c>のみ許容し、それ以外の差分が出ないことを要求する。
    /// この差は再解析後にIncremental更新へ分類されるだけで、表示値には影響しない。
    /// </summary>
    [Fact]
    public void ModelApply_MatchesSourceApplyThenReanalyze(){
        (SceneModel Base,SceneProjection BaseProjection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(BaseProjection,"Cube");
        SceneTransformValue Value=new(
            new Vector3(3.5f,-2f,0.25f),
            Quaternion.CreateFromAxisAngle(Vector3.UnitY,1.1f),
            new Vector3(2f,2f,2f));
        SetTransformOperation Operation=new(Key,Cube,DocumentVersion:1,Value);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,BaseProjection,Reconciler,Key,[Operation],out SceneModel Predicted,out SceneProjection PredictedProjection));

        SceneSourceEditResult Edited=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),BaseProjection,Operation);
        Assert.True(Edited.Succeeded,Edited.ErrorMessage);
        SceneModel Truth=SceneAnalysisTestHelper.Analyze([TransformTypes,Edited.Root!.ToFullString()]);

        SceneProjection TruthProjection=Reconciler.Reconcile(Key,Truth,BaseProjection);

        SceneDiff Diff=SceneDiffEngine.Compare(PredictedProjection,TruthProjection);
        Assert.All(Diff.Entries,static Entry=>Assert.Equal(SceneDiffKind.ComponentChanged,Entry.Kind));

        // 実際に描画・Inspectorが読む値そのものが一致していることを直接確かめる。
        AssertTransformEqual(Truth,Predicted,"Cube");
        AssertTransformEqual(Truth,Predicted,"Sphere");
    }

    /// <summary>
    /// 予測snapshotのdiffが増分更新へ分類されることを固定する。ここがRebuildへ落ちると、予測のたびに
    /// D3D12デバイス再生成+シェーダ再コンパイル+全頂点バッファ再アップロードが走り、
    /// 高速化どころか今より遅くなる(<c>SceneHwndHost.Rebuild</c>参照)。
    /// </summary>
    [Fact]
    public void PredictedDiff_StaysOnTheIncrementalRenderPath(){
        (SceneModel Base,SceneProjection BaseProjection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        SceneTransformValue Value=new(new Vector3(3f,1f,2f),Quaternion.Identity,Vector3.One);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,BaseProjection,Reconciler,Key,
            [new SetTransformOperation(Key,FindId(BaseProjection,"Cube"),DocumentVersion:1,Value)],
            out _,out SceneProjection PredictedProjection));

        SceneDiff Diff=SceneDiffEngine.Compare(BaseProjection,PredictedProjection);

        Assert.NotEmpty(Diff.Entries);
        Assert.Equal(SceneRenderUpdateKind.Incremental,SceneIncrementalUpdatePolicy.Classify(Diff));
    }

    /// <summary>
    /// 予測はNode構成を変えないため、安定ID(<see cref="EditorNodeId"/>)が保たれることを固定する。
    /// ここが崩れると、予測後に発行する編集操作が別Nodeを指してソースを壊しうる。
    /// </summary>
    [Fact]
    public void PredictedProjection_KeepsStableNodeIds(){
        (SceneModel Base,SceneProjection BaseProjection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(BaseProjection,"Cube");
        SceneTransformValue Value=new(new Vector3(3f,1f,2f),Quaternion.Identity,Vector3.One);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,BaseProjection,Reconciler,Key,[new SetTransformOperation(Key,Cube,DocumentVersion:1,Value)],
            out _,out SceneProjection PredictedProjection));

        Assert.Equal(Cube,FindId(PredictedProjection,"Cube"));
        Assert.Equal(FindId(BaseProjection,"Sphere"),FindId(PredictedProjection,"Sphere"));
    }

    [Fact]
    public void TryApply_AddNode_AppendsBareChildWithUniqueName(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Root=FindId(Projection,null);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new AddNodeOperation(Key,Root,DocumentVersion:1,"Node")],
            out SceneModel Predicted,out SceneProjection PredictedProjection));

        SceneNode NewNode=Assert.Single(PredictedProjection.Nodes,static N=>N.Node.Name=="Node").Node;
        Assert.Equal(2,NewNode.Path.Indices[0]); // Cube=0, Sphere=1 の後ろに追記される
        Assert.Empty(NewNode.Components);
        Assert.Equal(3,Predicted.Root!.Children.Length);
    }

    [Fact]
    public void TryApply_AddNode_DedupesNameAgainstSiblings(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Root=FindId(Projection,null);

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new AddNodeOperation(Key,Root,DocumentVersion:1,"Cube")],
            out _,out SceneProjection PredictedProjection));

        Assert.Single(PredictedProjection.Nodes,static N=>N.Node.Name=="Cube (1)");
    }

    [Fact]
    public void TryApply_RenameNode_ChangesNameOnly(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Box")],
            out SceneModel Predicted,out SceneProjection PredictedProjection));

        Assert.Equal("Box",PredictedProjection.Nodes.Single(N=>N.Id.Equals(Cube)).Node.Name);
        Assert.Equal(1f,ReadPosition(Predicted,"Box").X);
    }

    [Fact]
    public void TryApply_RenameNode_RejectsEmptyName(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");

        Assert.False(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new RenameNodeOperation(Key,Cube,DocumentVersion:1,"   ")],out _,out _));
    }

    [Fact]
    public void TryApply_RemoveNode_DropsTargetAndRenumbersSiblings(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new RemoveNodeOperation(Key,Cube,DocumentVersion:1)],
            out SceneModel Predicted,out SceneProjection PredictedProjection));

        Assert.DoesNotContain(PredictedProjection.Nodes,N=>N.Node.Name=="Cube");
        SceneNode Sphere=Assert.Single(PredictedProjection.Nodes,static N=>N.Node.Name=="Sphere").Node;
        Assert.Equal(0,Sphere.Path.Indices[0]);
        Assert.Equal(5f,ReadPosition(Predicted,"Sphere").X);
    }

    [Fact]
    public void TryApply_RemoveNode_RejectsRoot(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Root=FindId(Projection,null);

        Assert.False(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new RemoveNodeOperation(Key,Root,DocumentVersion:1)],out _,out _));
    }

    [Fact]
    public void TryApply_ReorderNode_MovesWithinSameParent(){
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=Analyze(Source);
        EditorNodeId Cube=FindId(Projection,"Cube");

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,[new ReorderNodeOperation(Key,Cube,DocumentVersion:1,NewIndex:1)],
            out _,out SceneProjection PredictedProjection));

        Assert.Equal(0,PredictedProjection.Nodes.Single(N=>N.Node.Name=="Sphere").Node.Path.Indices[0]);
        Assert.Equal(1,PredictedProjection.Nodes.Single(N=>N.Node.Name=="Cube").Node.Path.Indices[0]);
    }

    [Fact]
    public void TryApply_ReparentNode_MovesUnderNewParentAndRebasesDescendantPaths(){
        const string Nested="""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="GroupA", Children=[
                        new Node { Name="Child", Components=[new Transform { PositionX=1f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
                    ] },
                    new Node { Name="GroupB" },
                ] };
            }
            """;
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=
            Analyze(Nested,IncludeTransformTypes:true);
        EditorNodeId Child=FindId(Projection,"Child");
        EditorNodeId GroupB=FindId(Projection,"GroupB");

        Assert.True(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new ReparentNodeOperation(Key,Child,DocumentVersion:1,GroupB,NewIndex:0)],
            out SceneModel Predicted,out SceneProjection PredictedProjection));

        SceneProjectionNode MovedChild=PredictedProjection.Nodes.Single(N=>N.Id.Equals(Child));
        Assert.Equal(GroupB,MovedChild.ParentId);
        Assert.Equal(1f,ReadPosition(Predicted,"Child").X);
    }

    [Fact]
    public void TryApply_ReparentNode_RejectsMovingIntoOwnDescendant(){
        const string Nested="""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="Parent", Children=[new Node { Name="Child" }] },
                ] };
            }
            """;
        (SceneModel Base,SceneProjection Projection,SceneProjectionReconciler Reconciler)=
            Analyze(Nested,IncludeTransformTypes:false);
        EditorNodeId Parent=FindId(Projection,"Parent");
        EditorNodeId Child=FindId(Projection,"Child");

        Assert.False(SceneModelEditApplier.TryApply(
            Base,Projection,Reconciler,Key,
            [new ReparentNodeOperation(Key,Parent,DocumentVersion:1,Child,NewIndex:0)],out _,out _));
    }

    private static void AssertTransformEqual(SceneModel Expected,SceneModel Actual,string NodeName){
        Assert.True(SceneTransformBinding.TryRead(FindNode(Expected,NodeName),out SceneTransformValue Left));
        Assert.True(SceneTransformBinding.TryRead(FindNode(Actual,NodeName),out SceneTransformValue Right));
        Assert.True((Left.Position-Right.Position).Length()<1e-5f,$"{NodeName} Position {Left.Position} != {Right.Position}");
        Assert.True((Left.Scale-Right.Scale).Length()<1e-5f,$"{NodeName} Scale {Left.Scale} != {Right.Scale}");
        Assert.True(
            System.Math.Abs(Quaternion.Dot(Left.Rotation,Right.Rotation))>1f-1e-5f,
            $"{NodeName} Rotation {Left.Rotation} != {Right.Rotation}");
    }

    private static (SceneModel Model,SceneProjection Projection,SceneProjectionReconciler Reconciler)Analyze(
        string SceneSource,bool IncludeTransformTypes=true){
        SceneModel Model=IncludeTransformTypes
            ?SceneAnalysisTestHelper.Analyze([TransformTypes,SceneSource])
            :SceneAnalysisTestHelper.Analyze(SceneSource);
        SceneProjectionReconciler Reconciler=new();
        return(Model,Reconciler.Reconcile(Key,Model,Previous:null),Reconciler);
    }

    private static EditorNodeId FindId(SceneProjection Projection,string?NodeName)=>
        Assert.Single(Projection.Nodes,Node=>Node.Node.Name==NodeName).Id;

    private static SceneNode FindNode(SceneModel Model,string NodeName)=>
        Assert.Single(Model.EnumerateNodes(),Node=>Node.Name==NodeName);

    private static Vector3 ReadPosition(SceneModel Model,string NodeName){
        Assert.True(SceneTransformBinding.TryRead(FindNode(Model,NodeName),out SceneTransformValue Value));
        return Value.Position;
    }
}
