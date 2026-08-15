using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class DuplicateNodePlannerTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void TryCreate_AppendsSelectedRootsAfterExistingSiblingsInSelectionOrder(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[
                    new Node { Name="A" },new Node { Name="B" },new Node { Name="C" },
                ]};
            }
            """);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        SceneProjectionNode A=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="A");
        SceneProjectionNode C=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="C");

        Assert.True(DuplicateNodePlanner.TryCreate([A,C],Projection,out IReadOnlyList<DuplicateNodePlanItem>Plan));

        Assert.Equal(NodePath.Root.Append(3),Plan[0].ResultPath);
        Assert.Equal(NodePath.Root.Append(4),Plan[1].ResultPath);
        Assert.Equal(A.Id,Plan[0].NodeId);
        Assert.Equal(C.Id,Plan[1].NodeId);
    }

    [Fact]
    public void TryCreate_RootSelection_ReturnsFalse(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene { public Node Root=>new(); }
            """);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);

        Assert.False(DuplicateNodePlanner.TryCreate([Projection.Root!],Projection,out _));
    }

    [Fact]
    public void RemapPath_PreservesDescendantSuffixUnderPredictedRoot(){
        NodePath SourceRoot=NodePath.Root.Append(1).Append(2);
        NodePath ResultRoot=NodePath.Root.Append(1).Append(5);

        NodePath Result=DuplicateNodePlanner.RemapPath(
            SourceRoot.Append(3).Append(4),SourceRoot,ResultRoot);

        Assert.Equal(ResultRoot.Append(3).Append(4),Result);
    }

    [Fact]
    public void TryResolveResultSelection_WaitsUntilEveryPredictedPathExists(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="A" },new Node { Name="B" }]};
            }
            """);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        NodePath A=NodePath.Root.Append(0);
        NodePath B=NodePath.Root.Append(1);

        Assert.True(DuplicateNodePlanner.TryResolveResultSelection(
            Projection,[A,B],out IReadOnlyList<EditorNodeId>Ids));
        Assert.Equal(2,Ids.Count);
        Assert.False(DuplicateNodePlanner.TryResolveResultSelection(
            Projection,[A,NodePath.Root.Append(2)],out _));
    }
}
