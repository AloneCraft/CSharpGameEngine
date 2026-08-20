using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0030 監査。<see cref="SceneEditConflictMerger"/>の<b>失敗経路</b>を固定する。成功経路(rebase)は
/// <see cref="ScenePhase7Tests"/>が持つため重複させない。ここで守っているのは M0030 仕様 §8 の
/// 「対象ノード消失時は commit しない」「解析失敗時は確定しない」であり、いずれもドラッグ確定と
/// 実ソース書き換えの間に挟まる最後の関門になる。
/// </summary>
public sealed class SceneEditConflictMergerTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        namespace Game { public static class Constants { public const float OffsetX=5f; } }
        """;

    [Fact]
    public void Merge_NodeRemovedFromLatestScene_Fails(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreateTransformSnapshot(1,IncludeCube:true,"1f",Previous:null,Reconciler);
        var Cube=Assert.Single(Started.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        var Latest=CreateTransformSnapshot(2,IncludeCube:false,"1f",Started,Reconciler);

        var Result=SceneEditConflictMerger.Merge(CreateMoveRequest(Cube,Started),Latest);

        Assert.False(Result.Succeeded);
        Assert.Contains("最新 Transform を解決できません",Result.ErrorMessage);
        Assert.Empty(Result.Operations);
    }

    [Fact]
    public void Merge_TransformMemberBecameNonLiteralExpression_Fails(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreateTransformSnapshot(1,IncludeCube:true,"1f",Previous:null,Reconciler);
        var Cube=Assert.Single(Started.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        var Latest=CreateTransformSnapshot(
                                           2,IncludeCube:true,"Constants.OffsetX",Started,Reconciler);

        var Result=SceneEditConflictMerger.Merge(CreateMoveRequest(Cube,Started),Latest);

        Assert.False(Result.Succeeded);
        Assert.Contains("PositionX",Result.ErrorMessage);
        Assert.Contains("編集できません",Result.ErrorMessage);
        Assert.Empty(Result.Operations);
    }

    [Fact]
    public void Merge_MismatchedSceneKey_Fails(){
        SceneProjectionReconciler Reconciler=new();
        var Latest=CreateTransformSnapshot(2,IncludeCube:true,"1f",Previous:null,Reconciler);
        var Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneKey Other=new("project-b","global::Game.OtherScene");
        SceneEditCommitRequest Request=new(
            Other,1,"Move Cube",
            [new SceneTransformChange(Cube,IdentityAt(1f),IdentityAt(3f))],SceneTransformMemberMask.Position);

        var Result=SceneEditConflictMerger.Merge(Request,Latest);

        Assert.False(Result.Succeeded);
        Assert.Contains("編集対象を解決できません",Result.ErrorMessage);
    }

    [Fact]
    public void Merge_SameDocumentVersion_SkipsRebaseAndKeepsTheDraggedValue(){
        SceneProjectionReconciler Reconciler=new();
        var Latest=CreateTransformSnapshot(1,IncludeCube:true,"1f",Previous:null,Reconciler);
        var Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        var Result=SceneEditConflictMerger.Merge(CreateMoveRequest(Cube,Latest),Latest);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Operation=Assert.IsType<SetTransformOperation>(Assert.Single(Result.Operations));
        // 外部変更が無いのでドラッグ終値がそのまま通る(rebase 経路の +delta 合成は起きない)。
        Assert.Equal(3f,Operation.Transform.Position.X,3);
    }

    [Fact]
    public void Merge_PropertyOperation_ComponentMissingFromNode_Fails(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreatePropertySnapshot(1,"3f",Previous:null,Reconciler);
        var Latest=CreatePropertySnapshot(2,"9f",Started,Reconciler);
        var Light=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SetComponentMemberOperation Edit=new(
            Key,Light,Started.DocumentVersion,"global::Sample.SpotLight","Intensity","4f",
            OriginalValueExpression:"3f");
        SceneEditCommitRequest Request=new(
            Key,Started.DocumentVersion,"Edit Intensity",[],SceneTransformMemberMask.None,
            DirectOperations:[Edit]);

        var Result=SceneEditConflictMerger.Merge(Request,Latest);

        Assert.False(Result.Succeeded);
        Assert.Contains("global::Sample.SpotLight",Result.ErrorMessage);
        Assert.Contains("最新 Scene にありません",Result.ErrorMessage);
    }

    [Fact]
    public void Merge_PropertyMemberBecameNonLiteralExpression_Fails(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreatePropertySnapshot(1,"3f",Previous:null,Reconciler);
        var Latest=CreatePropertySnapshot(2,"Constants.Intensity",Started,Reconciler);
        var Light=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SetComponentMemberOperation Edit=new(
            Key,Light,Started.DocumentVersion,"global::Sample.DirectionalLight","Intensity","4f",
            OriginalValueExpression:"3f");
        SceneEditCommitRequest Request=new(
            Key,Started.DocumentVersion,"Edit Intensity",[],SceneTransformMemberMask.None,
            DirectOperations:[Edit]);

        var Result=SceneEditConflictMerger.Merge(Request,Latest);

        Assert.False(Result.Succeeded);
        Assert.Contains("Intensity",Result.ErrorMessage);
        Assert.Contains("編集できません",Result.ErrorMessage);
    }

    private static SceneTransformValue IdentityAt(float PositionX)=>
        new(new Vector3(PositionX,0f,0f),Quaternion.Identity,Vector3.One);

    private static SceneEditCommitRequest CreateMoveRequest(EditorNodeId Node,SceneDocumentSnapshot Started)=>
        new(Key,Started.DocumentVersion,"Move Cube",
            [new SceneTransformChange(Node,IdentityAt(1f),IdentityAt(3f))],SceneTransformMemberMask.Position);

    private static SceneDocumentSnapshot CreateTransformSnapshot(
        long Version,bool IncludeCube,string PositionXExpression,SceneDocumentSnapshot?Previous,
        SceneProjectionReconciler Reconciler){
        var Cube=IncludeCube
                     ?$$"""
                        new Node { Name="Cube",Components=[new Transform {
                            PositionX={{PositionXExpression}},RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
                        """
                     :string.Empty;
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      TransformTypes,
                                                      $$"""
                                                        using CSharpGameEngine.Entities;
                                                        using CSharpGameEngine.Runtime.Windows;
                                                        namespace Game;
                                                        public partial class MainScene:IScene {
                                                            public Node Root=>new() { Children=[
                                                                {{Cube}}
                                                                new Node { Name="Anchor",Components=[new Transform { RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] },
                                                            ] };
                                                        }
                                                        """,
                                                  ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",Version,Version,Model,HasBlockingErrors:false,Previous,Reconciler);
    }

    private static SceneDocumentSnapshot CreatePropertySnapshot(
        long Version,string IntensityExpression,SceneDocumentSnapshot?Previous,
        SceneProjectionReconciler Reconciler){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      SceneAnalysisTestHelper.CommonComponents,
                                                      "namespace Game { public static class Constants { public const float Intensity=7f; } }",
                                                      $$"""
                                                        using CSharpGameEngine.Entities;
                                                        using Sample;
                                                        namespace Game;
                                                        public partial class MainScene:IScene {
                                                            public Node Root=>new() { Children=[new Node { Name="Light",Components=[
                                                                new DirectionalLight { Intensity={{IntensityExpression}} }] }] };
                                                        }
                                                        """,
                                                  ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",Version,Version,Model,HasBlockingErrors:false,Previous,Reconciler);
    }
}
