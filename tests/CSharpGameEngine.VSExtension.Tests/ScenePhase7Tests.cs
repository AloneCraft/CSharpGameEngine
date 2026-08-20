using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class ScenePhase7Tests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");
    private const string TransformTypes="""
        namespace CSharpGameEngine.Runtime.Windows {
            public struct Transform {
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
            public struct PrimitiveMesh { public PrimitiveType Type; }
            public enum PrimitiveType { Box, Sphere }
        }
        """;

    [Fact]
    public void IncrementalPolicy_UsesSceneDiffForTransformAndNonMeshMembers(){
        EditorNodeId NodeId=new(2);
        SceneDiff TransformDiff=new([
            new SceneDiffEntry(SceneDiffKind.ComponentChanged,NodeId,SceneComponentBindings.TransformTypeFullyQualifiedName),
            new SceneDiffEntry(SceneDiffKind.MemberChanged,NodeId,SceneComponentBindings.TransformTypeFullyQualifiedName,"PositionX"),
        ]);
        SceneDiff PropertyDiff=new([
            new SceneDiffEntry(SceneDiffKind.ComponentChanged,NodeId,SceneComponentBindings.CameraTypeFullyQualifiedName),
            new SceneDiffEntry(SceneDiffKind.MemberChanged,NodeId,SceneComponentBindings.CameraTypeFullyQualifiedName,"NearZ"),
        ]);

        Assert.Equal(SceneRenderUpdateKind.Incremental,SceneIncrementalUpdatePolicy.Classify(TransformDiff));
        Assert.Equal(SceneRenderUpdateKind.Incremental,SceneIncrementalUpdatePolicy.Classify(PropertyDiff));
    }

    [Fact]
    public void IncrementalPolicy_RebuildsForMeshOrStructuralChanges(){
        EditorNodeId NodeId=new(2);
        SceneDiff MeshDiff=new([
            new SceneDiffEntry(SceneDiffKind.MemberChanged,NodeId,SceneComponentBindings.PrimitiveMeshTypeFullyQualifiedName,"Type"),
        ]);
        SceneDiff StructuralDiff=new([new SceneDiffEntry(SceneDiffKind.NodeAdded,NodeId)]);

        Assert.Equal(SceneRenderUpdateKind.Rebuild,SceneIncrementalUpdatePolicy.Classify(MeshDiff));
        Assert.Equal(SceneRenderUpdateKind.Rebuild,SceneIncrementalUpdatePolicy.Classify(StructuralDiff));
    }

    [Fact]
    public void IncrementalUpdater_ReusesMeshesAndRecomputesTheWholeWorldChain(){
        var Before=AnalyzeTransformScene(RootX:1f,ChildX:2f);
        var After=AnalyzeTransformScene(RootX:5f,ChildX:3f);
        var Existing=SceneModelRenderObjectBuilder.Build(Before,static _=>null);

        Assert.True(SceneRenderIncrementalUpdater.TryUpdateTransforms(
            After,Existing,out var Updated));

        Assert.Equal(5f,Updated[0].World.Translation.X,3);
        Assert.Equal(8f,Updated[1].World.Translation.X,3);
        Assert.Same(Existing[0].Mesh,Updated[0].Mesh);
        Assert.Same(Existing[1].Mesh,Updated[1].Mesh);
    }

    [Fact]
    public void FrameResourceTracker_WaitsOnlyWhenAFrameSlotIsReused(){
        FrameResourceTracker Tracker=new(FrameCount:2);

        Assert.Equal(0ul,Tracker.GetRequiredFenceValue(FrameIndex:0));
        var First=Tracker.MarkSubmitted(FrameIndex:0);
        Assert.Equal(0ul,Tracker.GetRequiredFenceValue(FrameIndex:1));
        var Second=Tracker.MarkSubmitted(FrameIndex:1);

        Assert.Equal(First,Tracker.GetRequiredFenceValue(FrameIndex:0));
        Assert.Equal(Second,Tracker.GetRequiredFenceValue(FrameIndex:1));
        Assert.True(Second>First);
    }

    [Fact]
    public void FrameResourceTracker_GpuIdleSynchronizesEveryFrameSlot(){
        FrameResourceTracker Tracker=new(FrameCount:2);
        Tracker.MarkSubmitted(0);
        Tracker.MarkSubmitted(1);

        var IdleFence=Tracker.MarkGpuIdle();

        Assert.Equal(IdleFence,Tracker.GetRequiredFenceValue(0));
        Assert.Equal(IdleFence,Tracker.GetRequiredFenceValue(1));
    }

    [Fact]
    public void ConflictMerger_AppliesTransformDragDeltaToTheLatestExternalValue(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreateTransformSnapshot(1,PositionX:1f,Previous:null,Reconciler);
        var Latest=CreateTransformSnapshot(2,PositionX:5f,Started,Reconciler);
        var Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;
        SceneTransformValue Original=new(new Vector3(1f,0f,0f),Quaternion.Identity,Vector3.One);
        SceneTransformValue Final=new(new Vector3(3f,0f,0f),Quaternion.Identity,Vector3.One);
        SceneEditCommitRequest Request=new(
            Key,Started.DocumentVersion,"Move Cube",
            [new SceneTransformChange(Cube,Original,Final)],SceneTransformMemberMask.Position);

        var Result=SceneEditConflictMerger.Merge(Request,Latest);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Operation=Assert.IsType<SetTransformOperation>(Assert.Single(Result.Operations));
        Assert.Equal(7f,Operation.Transform.Position.X,3);
    }

    [Fact]
    public void ConflictMerger_AppliesPropertyDragDeltaToTheLatestExternalValue(){
        SceneProjectionReconciler Reconciler=new();
        var Started=CreatePropertySnapshot(1,Intensity:3f,Previous:null,Reconciler);
        var Latest=CreatePropertySnapshot(2,Intensity:10f,Started,Reconciler);
        var Light=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SetComponentMemberOperation Edit=new(
            Key,Light,Started.DocumentVersion,"global::Sample.DirectionalLight","Intensity","4f",
            OriginalValueExpression:"3f");
        SceneEditCommitRequest Request=new(
            Key,Started.DocumentVersion,"Edit Intensity",[],SceneTransformMemberMask.None,
            DirectOperations:[Edit]);

        var Result=SceneEditConflictMerger.Merge(Request,Latest);

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Result.Operations));
        Assert.Equal("11f",Operation.ValueExpression);
    }

    private static SceneModel AnalyzeTransformScene(float RootX,float ChildX)=>SceneAnalysisTestHelper.Analyze([
        TransformTypes,
        $$"""
        using CSharpGameEngine.Entities;
        using CSharpGameEngine.Runtime.Windows;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Components=[new Transform { PositionX={{RootX}}f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }],
                Children=[new Node { Name="Cube",Components=[new Transform {
                    PositionX={{ChildX}}f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] }],
            };
        }
        """,
    ]);

    private static SceneDocumentSnapshot CreateTransformSnapshot(
        long Version,float PositionX,SceneDocumentSnapshot?Previous,SceneProjectionReconciler Reconciler){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      TransformTypes,
                                                      $$"""
                                                        using CSharpGameEngine.Entities;
                                                        using CSharpGameEngine.Runtime.Windows;
                                                        namespace Game;
                                                        public partial class MainScene:IScene {
                                                            public Node Root=>new() { Children=[new Node { Name="Cube",Components=[new Transform {
                                                                PositionX={{PositionX}}f,RotationW=1f,ScaleX=1f,ScaleY=1f,ScaleZ=1f }] }] };
                                                        }
                                                        """,
                                                  ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",Version,Version,Model,HasBlockingErrors:false,Previous,Reconciler);
    }

    private static SceneDocumentSnapshot CreatePropertySnapshot(
        long Version,float Intensity,SceneDocumentSnapshot?Previous,SceneProjectionReconciler Reconciler){
        var Model=SceneAnalysisTestHelper.Analyze($$"""
                                                    using CSharpGameEngine.Entities;
                                                    using Sample;
                                                    namespace Game;
                                                    public partial class MainScene:IScene {
                                                        public Node Root=>new() { Children=[new Node { Name="Light",Components=[
                                                            new DirectionalLight { Intensity={{Intensity}}f }] }] };
                                                    }
                                                    """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",Version,Version,Model,HasBlockingErrors:false,Previous,Reconciler);
    }
}
