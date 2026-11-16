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
        var First=HierarchySelectionPlanner.Select(HierarchyNodeSelection.Empty,Order,A,false,false);
        var Added=HierarchySelectionPlanner.Select(First,Order,B,true,false);
        var Ranged=HierarchySelectionPlanner.Select(Added,Order,Root,false,true);

        Assert.Equal([A,B],Added.Paths);
        Assert.Equal(B,Added.PrimaryPath);
        Assert.Equal([Root,A,A0,B],Ranged.Paths);
        Assert.Equal(Root,Ranged.PrimaryPath);
        Assert.Equal(B,Ranged.RangeAnchor);
    }

    [Fact]
    public void SelectionPlanner_RemovingPrimaryKeepsTheRemainingPrimary(){
        HierarchyNodeSelection Existing=new([A,B],B,B);

        var Result=HierarchySelectionPlanner.Select(Existing,Order,B,true,false);

        Assert.Equal([A],Result.Paths);
        Assert.Equal(A,Result.PrimaryPath);
    }

    [Fact]
    public void MarkerPlanner_UsesNodeCreationSpansAndCoalescesSelectedParentAndChild(){
        var Model=Analyze();

        var Ranges=SceneSourceMarkerPlanner.Create(Model,[A,A0,B]);

        Assert.Equal(2,Ranges.Count);
        Assert.Equal(Model.FindNode(A)!.Location.Span,Ranges[0].Span);
        Assert.Equal(Model.FindNode(B)!.Location.Span,Ranges[1].Span);
    }

    [Fact]
    public void NavigationPlanner_RequiresAnOpenDocumentAndTargetsTheNewExpression(){
        var Model=Analyze();

        Assert.False(SceneSourceMarkerPlanner.TryCreateNavigation(Model,A,IsDocumentOpen:false,out _,out _));
        Assert.True(SceneSourceMarkerPlanner.TryCreateNavigation(Model,A,IsDocumentOpen:true,out var FilePath,out var Position));
        Assert.Equal(Model.FilePath,FilePath);
        Assert.Equal(Model.FindNode(A)!.Location.Span.Start,Position);
    }

    [Fact]
    public void MarkerPlanner_ClearsForAnInvalidOrEmptyScene(){
        Assert.Empty(SceneSourceMarkerPlanner.Create(null,[A]));
        Assert.Empty(SceneSourceMarkerPlanner.Create(Analyze(),[Root.Append(99)]));
    }

    [Fact]
    public void MarkerAndNavigation_ForAJsonScene_PointAtTheNodeObjectInTheJson(){
        var Model=Analyze();
        const string Json="{ \"root\": { \"name\": \"Root\", \"children\": [ { \"name\": \"A\", \"children\": [ { \"name\": \"A0\" } ] }, { \"name\": \"B\" } ] } }";
        var AStart=Json.IndexOf("{ \"name\": \"A\"",System.StringComparison.Ordinal);
        var BStart=Json.IndexOf("{ \"name\": \"B\"",System.StringComparison.Ordinal);

        var Ranges=SceneSourceMarkerPlanner.Create(Model,[A,A0,B],Json);

        Assert.Equal(2,Ranges.Count);
        Assert.Equal(AStart,Ranges[0].Span.Start);
        Assert.Equal('}',Json[Ranges[0].Span.End-1]);
        Assert.Equal(BStart,Ranges[1].Span.Start);
        Assert.True(SceneSourceMarkerPlanner.TryCreateNavigation(Model,B,IsDocumentOpen:true,out _,out var Position,Json));
        Assert.Equal(BStart,Position);
        Assert.Empty(SceneSourceMarkerPlanner.Create(Model,[A],"{ not json"));
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
