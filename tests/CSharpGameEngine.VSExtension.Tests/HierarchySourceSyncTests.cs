using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class HierarchySourceSyncTests {
    private static readonly NodePath Root=NodePath.Root;
    private static readonly NodePath A=Root.Append(0);
    private static readonly NodePath A0=A.Append(0);
    private static readonly NodePath B=Root.Append(1);
    private static readonly NodePath[] Order=[Root,A,A0,B];

    [Fact]
    public void SelectionPlanner_UsesCtrlForToggleAndShiftForDeclarationOrderRange(){
        HierarchyNodeSelection First=HierarchySelectionPlanner.Select(HierarchyNodeSelection.Empty,Order,A,false,false);
        HierarchyNodeSelection Added=HierarchySelectionPlanner.Select(First,Order,B,true,false);
        HierarchyNodeSelection Ranged=HierarchySelectionPlanner.Select(Added,Order,Root,false,true);

        Assert.Equal([A,B],Added.Paths);
        Assert.Equal(B,Added.PrimaryPath);
        Assert.Equal([Root,A,A0,B],Ranged.Paths);
        Assert.Equal(Root,Ranged.PrimaryPath);
        Assert.Equal(B,Ranged.RangeAnchor);
    }

    [Fact]
    public void SelectionPlanner_RemovingPrimaryKeepsTheRemainingPrimary(){
        HierarchyNodeSelection Existing=new([A,B],B,B);

        HierarchyNodeSelection Result=HierarchySelectionPlanner.Select(Existing,Order,B,true,false);

        Assert.Equal([A],Result.Paths);
        Assert.Equal(A,Result.PrimaryPath);
    }

    [Fact]
    public void MarkerPlanner_UsesNodeCreationSpansAndCoalescesSelectedParentAndChild(){
        SceneModel Model=Analyze();

        IReadOnlyList<SceneSourceMarkerRange> Ranges=SceneSourceMarkerPlanner.Create(Model,[A,A0,B]);

        Assert.Equal(2,Ranges.Count);
        Assert.Equal(Model.FindNode(A)!.Location.Span,Ranges[0].Span);
        Assert.Equal(Model.FindNode(B)!.Location.Span,Ranges[1].Span);
    }

    [Fact]
    public void NavigationPlanner_RequiresAnOpenDocumentAndTargetsTheNewExpression(){
        SceneModel Model=Analyze();

        Assert.False(SceneSourceMarkerPlanner.TryCreateNavigation(Model,A,IsDocumentOpen:false,out _,out _));
        Assert.True(SceneSourceMarkerPlanner.TryCreateNavigation(Model,A,IsDocumentOpen:true,out string FilePath,out int Position));
        Assert.Equal(Model.FilePath,FilePath);
        Assert.Equal(Model.FindNode(A)!.Location.Span.Start,Position);
    }

    [Fact]
    public void MarkerPlanner_ClearsForAnInvalidOrEmptyScene(){
        Assert.Empty(SceneSourceMarkerPlanner.Create(null,[A]));
        Assert.Empty(SceneSourceMarkerPlanner.Create(Analyze(),[Root.Append(99)]));
    }

    private static SceneModel Analyze()=>SceneAnalysisTestHelper.Analyze("""
        using CSharpGameEngine.Entities;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new Node {
                Name="Root",
                Children=[
                    new Node { Name="A", Children=[new Node { Name="A0" }] },
                    new Node { Name="B" },
                ],
            };
        }
        """);
}
