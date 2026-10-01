using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070: a gesture on a JSON scene is written back from the edited model. The write is the edited
/// line only, node ids survive, and the result is a valid scene document.
/// </summary>
public sealed class SceneJsonCommitPlannerTests {
    private const string TransformTypes="""
        namespace CSharpGameEngine.Entities {
            public sealed class SceneComponentOrderAttribute:System.Attribute { }
        }
        namespace CSharpGameEngine.Runtime.Windows {
            public struct LogicalVector3 {
                public float R; public float U; public float F;
                public LogicalVector3(float R,float U,float F){ this.R=R;this.U=U;this.F=F; }
            }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Transform {
                public LogicalVector3 Position;
                public float PositionX; public float PositionY; public float PositionZ;
                public float RotationX; public float RotationY; public float RotationZ; public float RotationW;
                public float ScaleX; public float ScaleY; public float ScaleZ;
            }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Light { public float Intensity; }
        }
        """;
    private const string JsonPath=@"C:\Game\Scenes\MainScene.json";
    private const string CubeId="0f3c6c7a-0000-4000-8000-000000000002";
    private const string LampId="0f3c6c7a-0000-4000-8000-000000000003";
    private static readonly SceneKey Key=new("project-a","Game.MainScene");
    private static string Document(string PositionX)=>
        "{\n"
        +"  \"$schema\": \"../.cge/scene.schema.json\",\n"
        +"  \"schemaVersion\": 1,\n"
        +"  \"namespace\": \"Game\",\n"
        +"  \"className\": \"MainScene\",\n"
        +"  \"root\": {\n"
        +"    \"id\": \"0f3c6c7a-0000-4000-8000-000000000001\",\n"
        +"    \"components\": [],\n"
        +"    \"children\": [\n"
        +"      {\n"
        +"        \"id\": \""+CubeId+"\",\n"
        +"        \"name\": \"Cube\",\n"
        +"        \"components\": [\n"
        +"          {\n"
        +"            \"type\": \"CSharpGameEngine.Runtime.Windows.Transform\",\n"
        +"            \"members\": {\n"
        +"              \"Position\": { \"R\": "+PositionX+", \"U\": 0, \"F\": 0 },\n"
        +"              \"RotationW\": 1,\n"
        +"              \"ScaleX\": 1,\n"
        +"              \"ScaleY\": 1,\n"
        +"              \"ScaleZ\": 1\n"
        +"            }\n"
        +"          }\n"
        +"        ],\n"
        +"        \"children\": []\n"
        +"      },\n"
        +"      {\n"
        +"        \"id\": \""+LampId+"\",\n"
        +"        \"name\": \"Lamp\",\n"
        +"        \"components\": [\n"
        +"          {\n"
        +"            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n"
        +"            \"members\": {\n"
        +"              \"Intensity\": 3\n"
        +"            }\n"
        +"          }\n"
        +"        ],\n"
        +"        \"children\": []\n"
        +"      }\n"
        +"    ]\n"
        +"  }\n"
        +"}\n";
    [Fact]
    public void Move_WritesOnlyTheEditedLineAndKeepsNodeIds(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Equal(Document("3"),After);
        Assert.Equal(["              \"Position\": { \"R\": 1, \"U\": 0, \"F\": 0 },"],ChangedLines(Document("1"),After));
    }
    [Fact]
    public void ConsecutiveGesturesAndRename_KeepIdsAndStayOneLineEach(){
        var Reconciler=new SceneProjectionReconciler();
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,Reconciler);
        var Cube=Find(Seed.Snapshot,"Cube");
        var First=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Cube,At(1f),At(2.5f))],SceneTransformMemberMask.Position),
            Reconciler,CancellationToken.None);
        Assert.True(First.Succeeded,First.ErrorMessage);
        var FirstText=First.WriteStep!.Before.WithChanges(First.WriteStep.Changes).ToString();
        var Predicted=SceneDocumentSnapshot.Create(Key,"document-a",2,2,First.PredictedModel!,false,Seed.Snapshot,Reconciler,First.PredictedProjection);
        var Next=CreateSeed(FirstText,Version:2,Predicted,Reconciler);
        Assert.Equal(Cube,Find(Next.Snapshot,"Cube"));
        var Rename=new RenameNodeOperation(Key,Cube,2,"Crate");
        var Second=SceneSourceCommitPlanner.Plan(Next.Context,Next.Snapshot,
            new SceneEditCommitRequest(Key,2,"Rename",[],SceneTransformMemberMask.None,DirectOperations:[Rename]),
            Reconciler,CancellationToken.None);
        Assert.True(Second.Succeeded,Second.ErrorMessage);
        var SecondText=Second.WriteStep!.Before.WithChanges(Second.WriteStep.Changes).ToString();
        Assert.Equal(["        \"name\": \"Cube\","],ChangedLines(FirstText,SecondText));
        Assert.Contains("\"Position\": { \"R\": 2.5, \"U\": 0, \"F\": 0 },",SecondText);
        Assert.Contains(CubeId,SecondText);
        Assert.Contains(LampId,SecondText);
    }
    [Fact]
    public void AddedNode_GetsAFreshIdWhileExistingIdsStay(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Root=Seed.Snapshot.Projection!.Nodes.Single(static Node=>Node.ParentId is null).Id;
        var Add=new AddNodeOperation(Key,Root,Seed.Snapshot.DocumentVersion,"Empty");
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add",[],SceneTransformMemberMask.None,DirectOperations:[Add]),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        var Scene=SceneJsonReader.Parse(After).Get("root")!;
        var Ids=Scene.Get("children")!.Items.Select(static Child=>Child.Get("id")!.Text).ToArray();
        Assert.Equal(3,Ids.Length);
        Assert.Equal(CubeId,Ids[0]);
        Assert.Equal(LampId,Ids[1]);
        Assert.True(Guid.TryParse(Ids[2],out _));
        Assert.Equal("Empty",Scene.Get("children")!.Items[2].Get("name")!.Text);
    }
    [Fact]
    public void AddNodeWithAComponentTheSceneCannotHold_FailsAndWritesNothing(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Root=Seed.Snapshot.Projection!.Nodes.Single(static Node=>Node.ParentId is null).Id;
        var Add=new AddNodeOperation(Key,Root,Seed.Snapshot.DocumentVersion,"Lamp",
            [new NewNodeComponent("global::Sample.Missing","new global::Sample.Missing()")]);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add",[],SceneTransformMemberMask.None,DirectOperations:[Add]),
            Seed.Reconciler,CancellationToken.None);
        Assert.False(Plan.Succeeded);
        Assert.Null(Plan.WriteStep);
        Assert.Contains("SCENE201",Plan.ErrorMessage,StringComparison.Ordinal);
    }
    /// <summary>
    /// ADR-0070 (revised by M0095): the Scene Designer analyzes a scene with the build's rules, so a document
    /// the build rejects (an unknown key, member or type) is not analyzed and nothing is written to it; it is
    /// fixed in the JSON view, where the same errors show.
    /// </summary>
    [Theory]
    [InlineData("  \"schemaVersion\": 1,\n","  \"schemaVersion\": 1,\n  \"editorNote\": \"x\",\n","SCENE204")]
    [InlineData("              \"RotationW\": 1,\n","              \"RotationW\": 1,\n              \"Bogus\": 1,\n","SCENE202")]
    [InlineData("CSharpGameEngine.Runtime.Windows.Light","CSharpGameEngine.Runtime.Windows.Missing","SCENE201")]
    public void DocumentTheBuildRejects_IsNotAnalyzed(string Find,string Replace,string Diagnostic){
        var Json=Document("1").Replace(Find,Replace);
        Assert.NotEqual(Document("1"),Json);
        var Base=SceneAnalysisTestHelper.CreateCompilation(TransformTypes);
        Assert.False(SceneJsonCommitPlanner.TryAnalyze(Json,JsonPath,Base,"Game.MainScene",CancellationToken.None,
            out _,out _,out _,out var Error));
        Assert.Contains(Diagnostic,Error,StringComparison.Ordinal);
    }
    /// <summary>M0095 3-5: a gesture on a hand-formatted document changes the edited value only.</summary>
    [Fact]
    public void Move_KeepsHandFormatting(){
        var Before=Document("1")
            .Replace("              \"ScaleX\": 1,\n","              \"ScaleX\":1,\n")
            .Replace("              \"ScaleY\": 1,\n              \"ScaleZ\": 1\n","              \"ScaleY\": 1, \"ScaleZ\": 1\n");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),After);
    }
    private static string[]ChangedLines(string Before,string After){
        var Old=Before.Split('\n');
        var New=After.Split('\n');
        Assert.Equal(Old.Length,New.Length);
        return Old.Where((Line,Index)=>Line!=New[Index]).ToArray();
    }
    private static SceneTransformValue At(float X)=>new(new Vector3(X,0f,0f),Quaternion.Identity,Vector3.One);
    private static EditorNodeId Find(SceneDocumentSnapshot Snapshot,string Name)=>
        Assert.Single(Snapshot.Projection!.Nodes,Node=>Node.Node.Name==Name).Id;
    private static Seed CreateSeed(string Json,long Version,SceneDocumentSnapshot?Previous,SceneProjectionReconciler Reconciler){
        var Base=SceneAnalysisTestHelper.CreateCompilation(TransformTypes);
        Assert.True(SceneJsonCommitPlanner.TryAnalyze(Json,JsonPath,Base,"Game.MainScene",CancellationToken.None,
            out var Model,out var Root,out var Compilation,out var Error),Error);
        var Snapshot=SceneDocumentSnapshot.Create(Key,"document-a",Version,Version,Model!,HasBlockingErrors:false,Previous,Reconciler);
        return new Seed(new SceneSourceCommitContext(Model!,Root!,Microsoft.CodeAnalysis.Text.SourceText.From(Json),Compilation!),Snapshot,Reconciler);
    }
    private sealed record Seed(SceneSourceCommitContext Context,SceneDocumentSnapshot Snapshot,SceneProjectionReconciler Reconciler);
}
