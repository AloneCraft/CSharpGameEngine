using System;
using System.Collections.Generic;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneEditorSessionContractsTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void BlockingAnalysis_KeepsLastKnownGoodProjection(){
        SceneProjectionReconciler Reconciler=new();
        var Valid=Analyze("Cube");
        var First=SceneDocumentSnapshot.Create(
                                               Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Valid,HasBlockingErrors:false,Previous:null,Reconciler);
        var Cube=Assert.Single(First.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        var Invalid=SceneDocumentSnapshot.Create(
                                                 Key,"document-a",DocumentVersion:2,AnalysisGeneration:2,Analyze("Incomplete"),HasBlockingErrors:true,Previous:First,Reconciler);

        Assert.True(Invalid.HasBlockingErrors);
        Assert.Same(First.Projection,Invalid.Projection);
        Assert.Same(Valid,Invalid.LastKnownGoodModel);
        Assert.Equal(Cube,Assert.Single(Invalid.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id);
        Assert.Equal(2,Invalid.DocumentVersion);
    }

    [Fact]
    public void Selection_ChangesOnlyWhenSemanticIdentityChanges(){
        SceneSelectionService Service=new();
        List<SceneSelection>Notifications=[];
        Service.SelectionChanged+=Notifications.Add;
        EditorNodeId Node=new(42);
        var FirstCorrelation=Guid.NewGuid();

        Assert.True(Service.Select(Key,[Node],Node,"global::Sample.Transform","X",SceneSelectionOrigin.Viewport,FirstCorrelation));
        Assert.False(Service.Select(Key,[Node],Node,"global::Sample.Transform","X",SceneSelectionOrigin.Source,Guid.NewGuid()));
        Assert.True(Service.Select(Key,[Node],Node,"global::Sample.Transform","Y",SceneSelectionOrigin.Source,Guid.NewGuid()));

        Assert.Equal(2,Notifications.Count);
        Assert.Equal(1,Notifications[0].Revision);
        Assert.Equal(2,Notifications[1].Revision);
        Assert.Equal(FirstCorrelation,Notifications[0].CorrelationId);
        Assert.Equal(SceneSelectionOrigin.Source,Notifications[1].Origin);
    }

    [Fact]
    public void Selection_TryClearCurrent_EmitsOneEmptySelectionOnlyWhenNeeded(){
        SceneSelectionService Service=new();
        List<SceneSelection>Notifications=[];
        Service.SelectionChanged+=Notifications.Add;
        EditorNodeId Node=new(42);

        Assert.True(Service.Select(Key,[Node],Node,"global::Sample.Transform","X",SceneSelectionOrigin.Viewport,Guid.NewGuid()));
        Assert.True(Service.TryClearCurrent(SceneSelectionOrigin.ComponentPalette,Guid.NewGuid()));
        Assert.False(Service.TryClearCurrent(SceneSelectionOrigin.Inspector,Guid.NewGuid()));

        SceneSelection Cleared=Assert.Single(Notifications.Skip(1));
        Assert.Empty(Cleared.NodeIds);
        Assert.Null(Cleared.PrimaryNodeId);
        Assert.Null(Cleared.ComponentType);
        Assert.Null(Cleared.MemberName);
        Assert.Equal(SceneSelectionOrigin.ComponentPalette,Cleared.Origin);
    }
    private static SceneModel Analyze(string Name){
        return SceneAnalysisTestHelper.Analyze(
            "using CSharpGameEngine.Entities;\n"
            +"namespace Game;\n"
            +"public partial class MainScene:IScene {\n"
            +"    public Node Root=>new() { Children=[new Node { Name=\""+Name+"\" }] };\n"
            +"}");
    }
}
