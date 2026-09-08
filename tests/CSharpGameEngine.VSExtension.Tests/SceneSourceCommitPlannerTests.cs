using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneSourceCommitPlannerTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct LogicalVector3 {
                public float R; public float U; public float F;
                public LogicalVector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; }
            }
            public struct Transform {
                public LogicalVector3 Position;
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
        }
        """;

    private static readonly SceneKey Key=new("project-a","Game.MainScene");

    [Fact]
    public void Plan_OneGestureProducesOneVerifiedMinimalSourceWrite(){
        Seed Seed=CreateSeed("1f",Version:1,Previous:null,new SceneProjectionReconciler());
        EditorNodeId Cube=FindCube(Seed.Snapshot);
        SceneEditCommitRequest Request=new(
            Key,Seed.Snapshot.DocumentVersion,"Move Cube",
            [new SceneTransformChange(Cube,At(1f),At(3f))],SceneTransformMemberMask.Position);

        SceneSourceCommitPlan Plan=SceneSourceCommitPlanner.Plan(
            Seed.Context,Seed.Snapshot,Request,Seed.Reconciler,CancellationToken.None);

        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.False(Plan.WasRebased);
        Assert.NotNull(Plan.WriteStep);
        Assert.NotEmpty(Plan.WriteStep!.Changes);
        Assert.Equal(
            Plan.FinalRoot!.ToFullString(),
            Plan.WriteStep.Before.WithChanges(Plan.WriteStep.Changes).ToString());
        Assert.Contains("hand-written comment",Plan.FinalRoot.ToFullString());
        Assert.Equal(3f,ReadCubeX(Plan));
    }

    [Fact]
    public void Plan_StaleTransformRebasesDeltaOntoLatestSource(){
        SceneProjectionReconciler Reconciler=new();
        Seed Started=CreateSeed("1f",Version:1,Previous:null,Reconciler);
        EditorNodeId Cube=FindCube(Started.Snapshot);
        Seed Latest=CreateSeed("10f",Version:2,Started.Snapshot,Reconciler);
        SceneEditCommitRequest Request=new(
            Key,Started.Snapshot.DocumentVersion,"Move Cube",
            [new SceneTransformChange(Cube,At(1f),At(3f))],SceneTransformMemberMask.Position);

        SceneSourceCommitPlan Plan=SceneSourceCommitPlanner.Plan(
            Latest.Context,Latest.Snapshot,Request,Reconciler,CancellationToken.None);

        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.True(Plan.WasRebased);
        Assert.Equal(12f,ReadCubeX(Plan));
    }

    [Fact]
    public void Plan_ConsecutiveGesturesReconcileWrittenSourceWithPredictedIdentity(){
        SceneProjectionReconciler Reconciler=new();
        Seed Started=CreateSeed("1f",Version:1,Previous:null,Reconciler);
        EditorNodeId Cube=FindCube(Started.Snapshot);
        SceneEditCommitRequest FirstRequest=new(
            Key,Started.Snapshot.DocumentVersion,"Move Cube",
            [new SceneTransformChange(Cube,At(1f),At(3f))],SceneTransformMemberMask.Position);

        SceneSourceCommitPlan First=SceneSourceCommitPlanner.Plan(
            Started.Context,Started.Snapshot,FirstRequest,Reconciler,CancellationToken.None);

        Assert.True(First.Succeeded,First.ErrorMessage);
        SceneDocumentSnapshot Predicted=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:2,AnalysisGeneration:2,First.PredictedModel!,
            HasBlockingErrors:false,Started.Snapshot,Reconciler,First.PredictedProjection);
        Seed SourceAfterFirstGesture=CreateSeedFromSource(
            First.FinalRoot!.ToFullString(),Predicted.DocumentVersion,Predicted,Reconciler);
        Assert.True(SourceAfterFirstGesture.Snapshot.Diff.IsEmpty);
        Assert.Equal(Cube,FindCube(SourceAfterFirstGesture.Snapshot));

        SceneEditCommitRequest SecondRequest=new(
            Key,Predicted.DocumentVersion,"Move Cube again",
            [new SceneTransformChange(Cube,At(3f),At(5f))],SceneTransformMemberMask.Position);
        SceneSourceCommitPlan Second=SceneSourceCommitPlanner.Plan(
            SourceAfterFirstGesture.Context,SourceAfterFirstGesture.Snapshot,SecondRequest,
            Reconciler,CancellationToken.None);

        Assert.True(Second.Succeeded,Second.ErrorMessage);
        Assert.False(Second.WasRebased);
        Assert.Single(Second.WriteStep!.Changes);
        Assert.Equal(5f,ReadCubeX(Second));
        Assert.Equal(Cube,FindCube(SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:3,AnalysisGeneration:3,Second.PredictedModel!,
            HasBlockingErrors:false,SourceAfterFirstGesture.Snapshot,Reconciler,
            Second.PredictedProjection)));
    }

    [Fact]
    public void Merge_StaleMemberWithoutObservedOriginalValueIsRejected(){
        SceneProjectionReconciler Reconciler=new();
        Seed Started=CreateSeed("1f",Version:1,Previous:null,Reconciler);
        Seed Latest=CreateSeed("2f",Version:2,Started.Snapshot,Reconciler);
        EditorNodeId Cube=FindCube(Latest.Snapshot);
        SetComponentMemberOperation Operation=new(
            Key,Cube,Started.Snapshot.DocumentVersion,
            "global::CSharpGameEngine.Runtime.Windows.Transform","PositionX","9f");
        SceneEditCommitRequest Request=new(
            Key,Started.Snapshot.DocumentVersion,"Set PositionX",[],SceneTransformMemberMask.None,
            DirectOperations:[Operation]);

        SceneEditConflictMergeResult Result=SceneEditConflictMerger.Merge(Request,Latest.Snapshot);

        Assert.False(Result.Succeeded);
        Assert.Contains("PositionX",Result.ErrorMessage);
        Assert.Empty(Result.Operations);
    }

    private static SceneTransformValue At(float X)=>
        new(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One);

    private static EditorNodeId FindCube(SceneDocumentSnapshot Snapshot)=>
        Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

    private static float ReadCubeX(SceneSourceCommitPlan Plan){
        SceneProjectionNode Cube=Assert.Single(
            Plan.PredictedProjection!.Nodes,static Node=>Node.Node.Name=="Cube");
        Assert.True(SceneTransformBinding.TryRead(Cube.Node,out SceneTransformValue Transform));
        return Transform.Position.X;
    }

    private static Seed CreateSeed(
        string PositionExpression,long Version,SceneDocumentSnapshot?Previous,
        SceneProjectionReconciler Reconciler){
        string Source=$$"""
            using CSharpGameEngine.Entities;
            using CSharpGameEngine.Runtime.Windows;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node {
                    Name="Cube", // hand-written comment
                    Components=[new Transform { PositionX={{PositionExpression}},RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                }] };
            }
            """;
        return CreateSeedFromSource(Source,Version,Previous,Reconciler);
    }

    private static Seed CreateSeedFromSource(
        string Source,long Version,SceneDocumentSnapshot?Previous,
        SceneProjectionReconciler Reconciler){
        var Compilation=SceneAnalysisTestHelper.CreateCompilation(TransformTypes,Source);
        SyntaxNode Root=Compilation.SyntaxTrees
            .Single(Tree=>Tree.GetRoot().ToFullString().Contains("class MainScene"))
            .GetRoot();
        var SceneClass=SceneAnalysisTestHelper.FindSceneClass(Compilation,"MainScene")!;
        SceneModel Model=SceneDeclarationAnalyzer.Analyze(
            SceneClass,Compilation,CancellationToken.None);
        SceneDocumentSnapshot Snapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",Version,Version,Model,HasBlockingErrors:false,Previous,Reconciler);
        SceneSourceCommitContext Context=new(
            Model,Root,Root.SyntaxTree.GetText(),Compilation);
        return new Seed(Context,Snapshot,Reconciler);
    }

    private sealed record Seed(
        SceneSourceCommitContext Context,SceneDocumentSnapshot Snapshot,
        SceneProjectionReconciler Reconciler);
}
