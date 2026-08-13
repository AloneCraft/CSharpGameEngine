using System;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneInspectorModelTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void Build_UsesSelectedComponentAndKeepsSourceExpression(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot();
        EditorNodeId Light=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SceneSelection Selection=new(
            Key,[Light],Light,"global::Sample.DirectionalLight",MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());

        SceneInspectorMember Member=Assert.Single(SceneInspectorModelBuilder.Build(Snapshot,Selection));

        Assert.Equal("Intensity",Member.MemberName);
        Assert.Equal("3f",Member.SourceExpression);
        Assert.True(Member.IsEditable);
    }

    [Fact]
    public void Build_RejectsSelectionForAnotherScene(){
        SceneDocumentSnapshot Snapshot=CreateSnapshot();
        EditorNodeId Light=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SceneSelection Selection=new(
            new SceneKey("project-b","global::Game.MainScene"),[Light],Light,null,null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());

        Assert.Empty(SceneInspectorModelBuilder.Build(Snapshot,Selection));
    }

    private static SceneDocumentSnapshot CreateSnapshot(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Light",Components=[new DirectionalLight { Intensity=3f }] }] };
            }
            """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }
}
