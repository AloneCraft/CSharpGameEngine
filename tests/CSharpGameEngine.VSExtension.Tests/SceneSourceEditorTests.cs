using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis.CSharp;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>Designer??????Syntax??????????comment/trivia??????????????</summary>
public sealed class SceneSourceEditorTests {
    private const string Source="""
        using CSharpGameEngine.Entities;
        using Sample;
        namespace Game;
        public partial class MainScene:IScene {
            public Node Root=>new() {
                Children=[
                    new Node {
                        Name="Cube", // ?????????
                        Components=[new DirectionalLight { Intensity=3f }],
                    },
                ],
            };
        }
        """;

    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");

    [Fact]
    public void SetComponentMember_ReplacesOnlyTheValueExpressionAndPreservesTrivia(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","4f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("Intensity=4f",Changed);
        Assert.Contains("// ?????????",Changed);
        Assert.Contains("Name=\"Cube\"",Changed);
    }

    [Fact]
    public void RenameNode_ChangesOnlyNameLiteral(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new RenameNodeOperation(Key,Cube,DocumentVersion:1,"Renamed"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        string Changed=Result.Root!.ToFullString();
        Assert.Contains("Name=\"Renamed\"",Changed);
        Assert.Contains("Intensity=3f",Changed);
        Assert.Contains("// ?????????",Changed);
    }

    [Fact]
    public void InvalidExpression_DoesNotReturnAMutatedRoot(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","not valid +"));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    [Fact]
    public void ApplyLatest_RebasesOneStaleOperationUsingStableNodeId(){
        SceneProjectionReconciler Reconciler=new();
        SceneModel FirstModel=SceneAnalysisTestHelper.Analyze(Source);
        SceneDocumentSnapshot First=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,FirstModel,HasBlockingErrors:false,Previous:null,Reconciler);
        string LatestSource=Source.Replace("Intensity=3f","Intensity=4f");
        SceneModel LatestModel=SceneAnalysisTestHelper.Analyze(LatestSource);
        SceneDocumentSnapshot Latest=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:2,AnalysisGeneration:2,LatestModel,HasBlockingErrors:false,Previous:First,Reconciler);
        EditorNodeId Cube=Assert.Single(Latest.Projection!.Nodes,static Node=>Node.Node.Name=="Cube").Id;

        SceneSourceEditResult Result=SceneSourceEditor.ApplyLatest(
            CSharpSyntaxTree.ParseText(LatestSource).GetRoot(),
            Latest,
            new SetComponentMemberOperation(Key,Cube,DocumentVersion:1,"global::Sample.DirectionalLight","Intensity","5f"));

        Assert.True(Result.Succeeded,Result.ErrorMessage);
        Assert.True(Result.WasRebased);
        Assert.Contains("Intensity=5f",Result.Root!.ToFullString());
    }

    [Fact]
    public void OtherSceneKey_IsRejectedBeforeAnySourceChange(){
        (SceneProjection Projection,EditorNodeId Cube)=CreateProjection();
        SceneSourceEditResult Result=SceneSourceEditor.Apply(
            CSharpSyntaxTree.ParseText(Source).GetRoot(),
            Projection,
            new RenameNodeOperation(new SceneKey("project-b","global::Game.MainScene"),Cube,DocumentVersion:1,"Renamed"));

        Assert.False(Result.Succeeded);
        Assert.Null(Result.Root);
    }

    private static (SceneProjection Projection,EditorNodeId Cube)CreateProjection(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze(Source);
        SceneProjection Projection=new SceneProjectionReconciler().Reconcile(Key,Model,Previous:null);
        SceneProjectionNode Cube=Assert.Single(Projection.Nodes,static Node=>Node.Node.Name=="Cube");
        return (Projection,Cube.Id);
    }
}
