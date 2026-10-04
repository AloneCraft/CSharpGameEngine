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
            public enum LightMode { Off, Bright }
            public readonly struct RufScalarVector {
                public RufScalarVector(CSharpGameEngine.Numerics.LogicalScalar R,CSharpGameEngine.Numerics.LogicalScalar U,CSharpGameEngine.Numerics.LogicalScalar F){ this.R=R;this.U=U;this.F=F; }
                public readonly CSharpGameEngine.Numerics.LogicalScalar R; public readonly CSharpGameEngine.Numerics.LogicalScalar U; public readonly CSharpGameEngine.Numerics.LogicalScalar F;
            }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Mover { public CSharpGameEngine.Numerics.LogicalScalar Speed; public RufScalarVector Offset; }
            public enum ShapeKind { Box, Sphere }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Body() {
                public float Mass=2.5f;
                public ShapeKind Shape=ShapeKind.Sphere;
                public CSharpGameEngine.Numerics.LogicalScalar Speed=CSharpGameEngine.Numerics.LogicalScalar.FromSingle(6f);
                public RufScalarVector Offset=Origin;
                public bool Enabled=true;
                public int Count;
                public static readonly RufScalarVector Origin=new(1f,2f,3f);
            }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Lamp { public LightMode Mode; public string Label; }
        }
        namespace CSharpGameEngine.Numerics {
            public readonly struct LogicalScalar {
                public static implicit operator LogicalScalar(float Value)=>default;
                public static LogicalScalar FromSingle(float Value)=>default;
            }
        }
        """;
    private const string JsonPath=@"C:\Game\Scenes\MainScene.json";
    private const string CubeId="0f3c6c7a-0000-4000-8000-000000000002";
    private const string LampId="0f3c6c7a-0000-4000-8000-000000000003";
    private static readonly SceneKey Key=new("project-a","Game.MainScene");
    private static string Document(string PositionX)=>
        "{\n"
        +"  \"$schema\": \"../.cge/scene.schema.json\",\n"
        +"  \"schemaVersion\": 3,\n"
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
    [InlineData("  \"schemaVersion\": 3,\n","  \"schemaVersion\": 3,\n  \"editorNote\": \"x\",\n","SCENE204")]
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
    /// <summary>
    /// An edit keeps a startup scene a startup scene. The designer's model used to lose <c>startupOrder</c>, so the
    /// first edit rewrote the document without it and the game stopped loading the scene.
    /// </summary>
    [Fact]
    public void Move_KeepsTheStartupOrderOfAStartupScene(){
        var Before=Document("1").Replace("  \"className\": \"MainScene\",\n","  \"className\": \"MainScene\",\n  \"startupOrder\": 2,\n");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),After);
    }
    /// <summary>
    /// The Inspector's enum and text editors write back to a JSON scene. Their values used to be accepted only as C#
    /// text edits, which a JSON scene does not have, so every enum or string edit failed.
    /// </summary>
    [Fact]
    public void InspectorEnumAndTextEdits_WriteTheirLinesOnly(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Lamp\",\n            \"members\": {\n              \"Mode\": \"Off\",\n              \"Label\": \"a \\\"b\\\"\"\n            }");
        Assert.Contains("\"Mode\": \"Off\"",Before);
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Members=SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid()));
        var Mode=Assert.Single(Members,static Member=>Member.MemberName=="Mode");
        Assert.True(SceneInspectorCommitPlanner.TryCreateChoiceCommit(Key,Seed.Snapshot.DocumentVersion,Mode,"Bright",out var ModeRequest));
        var ModePlan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,ModeRequest,Seed.Reconciler,CancellationToken.None);
        Assert.True(ModePlan.Succeeded,ModePlan.ErrorMessage);
        Assert.Equal(Before.Replace("\"Mode\": \"Off\"","\"Mode\": \"Bright\""),ModePlan.WriteStep!.Before.WithChanges(ModePlan.WriteStep.Changes).ToString());

        var Label=Assert.Single(Members,static Member=>Member.MemberName=="Label");
        var LabelRequest=SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Label,SceneInspectorValueFormat.FormatText("c\\d"));
        var LabelPlan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,LabelRequest,Seed.Reconciler,CancellationToken.None);
        Assert.True(LabelPlan.Succeeded,LabelPlan.ErrorMessage);
        Assert.Equal(Before.Replace("\"Label\": \"a \\\"b\\\"\"","\"Label\": \"c\\\\d\""),LabelPlan.WriteStep!.Before.WithChanges(LabelPlan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// LogicalScalar members (the engine's authored real numbers: character movement, spring arm, every RUF vector of a
    /// Transform) are edited as floats in the Inspector, as their JSON numbers are read. They used to be read-only, so
    /// the RUF vector editing the Inspector offers was unavailable for the engine's own vectors.
    /// </summary>
    [Fact]
    public void InspectorEditsLogicalScalarMembersAsFloats(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Mover\",\n            \"members\": {\n              \"Speed\": 6,\n              \"Offset\": { \"R\": 1, \"U\": 2, \"F\": 3 }\n            }");
        Assert.Contains("\"Speed\": 6",Before);
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Members=SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid()));
        var Speed=Assert.Single(Members,static Member=>Member.MemberName=="Speed");
        var OffsetU=Assert.Single(Members,static Member=>Member.MemberName=="U");
        Assert.True(Speed.IsEditable);
        Assert.Equal(InspectorEditorKind.Number,Speed.EditorKind);
        Assert.True(OffsetU.IsEditable);
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(Speed.NumericType!.Value,"7.5",out var SpeedText));
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(OffsetU.NumericType!.Value,"-4",out var OffsetText));

        var SpeedPlan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Speed,SpeedText),Seed.Reconciler,CancellationToken.None);
        Assert.True(SpeedPlan.Succeeded,SpeedPlan.ErrorMessage);
        Assert.Equal(Before.Replace("\"Speed\": 6","\"Speed\": 7.5"),SpeedPlan.WriteStep!.Before.WithChanges(SpeedPlan.WriteStep.Changes).ToString());
        var OffsetPlan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,OffsetU,OffsetText),Seed.Reconciler,CancellationToken.None);
        Assert.True(OffsetPlan.Succeeded,OffsetPlan.ErrorMessage);
        Assert.Equal(Before.Replace("\"U\": 2,","\"U\": -4,"),OffsetPlan.WriteStep!.Before.WithChanges(OffsetPlan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// A component added from the palette is written with its declared defaults. The defaults were kept only as C# text,
    /// so a float or enum default came out as null (SCENE203) or threw, and the engine's own components (Transform, the
    /// lights, CharacterMovement, ...) could not be added to a scene. A default JSON cannot hold (a static member) is
    /// left out, which keeps the same default when the scene is built.
    /// </summary>
    [Fact]
    public void PaletteComponent_IsWrittenWithItsDeclaredDefaults(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Body=Seed.Context.Compilation.GetTypeByMetadataName("CSharpGameEngine.Runtime.Windows.Body")!;
        var Initializer=SceneComponentInitializerPolicy.CreatePlan(Body,"global::CSharpGameEngine.Runtime.Windows.Body",Seed.Context.Compilation);
        var Add=new AddComponentOperation(Key,Find(Seed.Snapshot,"Lamp"),Seed.Snapshot.DocumentVersion,"global::CSharpGameEngine.Runtime.Windows.Body",Initializer.Expression,Initializer.Members);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add Body",[],SceneTransformMemberMask.None,DirectOperations:[Add]),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Contains("\"type\": \"CSharpGameEngine.Runtime.Windows.Body\"",After);
        Assert.Contains("\"Mass\": 2.5",After);
        Assert.Contains("\"Shape\": \"Sphere\"",After);
        Assert.Contains("\"Speed\": 6",After);
        Assert.Contains("\"Enabled\": true",After);
        Assert.Contains("\"Count\": 0",After);
        Assert.DoesNotContain("\"Offset\"",After);
    }
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
