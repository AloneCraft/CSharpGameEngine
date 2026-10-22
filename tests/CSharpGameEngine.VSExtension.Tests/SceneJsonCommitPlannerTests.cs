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
            public struct Precise { public double Ratio; public double Tiny; }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Priced { public decimal Price; public decimal Huge; }
            [System.Flags]
            public enum Layers { None=0, A=1, B=2 }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public record struct Tuning(float Gain,LightMode Mode);
            public static class Outer {
                [CSharpGameEngine.Entities.SceneComponentOrder]
                public struct Inner { public float Speed; }
            }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Counters { public byte Tiny; public sbyte Signed; public short Small; public ushort Port; public int Count; public uint Big; public long Wide; public ulong Huge; }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Lettered() { public char Letter='x'; public float Size=1.5f; }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Route { public System.Collections.Generic.List<float> Speeds; public int[] Ids; }
            [CSharpGameEngine.Entities.SceneComponentOrder]
            public struct Tagged() {
                public ShapeKind Cast=(ShapeKind)1;
                public Layers Mask=Layers.A|Layers.B;
                public ShapeKind Unnamed=(ShapeKind)7;
                public Layers Single=Layers.B;
            }
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
    /// A null string stays an editable text value. Scene JSON writes a reference type's null as null! (so the generated
    /// code does not warn), and the model reads that as the null literal, not as a read-only constant expression.
    /// </summary>
    [Fact]
    public void NullText_StaysEditable(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Lamp\",\n            \"members\": {\n              \"Label\": null\n            }");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Label=Assert.Single(SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid())),
            static Member=>Member.MemberName=="Label");
        Assert.True(Label.IsEditable);
        Assert.Equal(InspectorEditorKind.Text,Label.EditorKind);
        Assert.Equal("null",Label.SourceExpression);
        // The text field sends only a changed value, always as a quoted string (InspectorTextCommitPolicy, FormatText).
        var Edit=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Label,SceneInspectorValueFormat.FormatText("Lit")),Seed.Reconciler,CancellationToken.None);
        Assert.True(Edit.Succeeded,Edit.ErrorMessage);
        Assert.Equal(Before.Replace("\"Label\": null","\"Label\": \"Lit\""),Edit.WriteStep!.Before.WithChanges(Edit.WriteStep.Changes).ToString());
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
    /// lights, CharacterMotion, ...) could not be added to a scene. A default JSON cannot hold (a static member) is
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
    /// <summary>
    /// An enum default is written as the member it names, whatever expression declares it ((ShapeKind)1 is Sphere). A
    /// value no single member names (a flags combination) has no scene JSON form, so it is left out and the declared
    /// default applies, as for any default JSON cannot hold. The constant was written as a number, which the enum's
    /// member-name string rejected (SCENE203), so such a component could not be added.
    /// </summary>
    [Fact]
    public void PaletteComponent_WritesEnumDefaultsByName(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Tagged=Seed.Context.Compilation.GetTypeByMetadataName("CSharpGameEngine.Runtime.Windows.Tagged")!;
        var Initializer=SceneComponentInitializerPolicy.CreatePlan(Tagged,"global::CSharpGameEngine.Runtime.Windows.Tagged",Seed.Context.Compilation);
        var Add=new AddComponentOperation(Key,Find(Seed.Snapshot,"Lamp"),Seed.Snapshot.DocumentVersion,"global::CSharpGameEngine.Runtime.Windows.Tagged",Initializer.Expression,Initializer.Members);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add Tagged",[],SceneTransformMemberMask.None,DirectOperations:[Add]),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Contains("\"Cast\": \"Sphere\"",After);
        Assert.Contains("\"Single\": \"B\"",After);
        Assert.DoesNotContain("\"Mask\"",After);
        Assert.DoesNotContain("\"Unnamed\"",After);
    }
    /// <summary>
    /// An edit elsewhere leaves a double member's text as it is. The VSIX (.NET Framework) wrote a double with its own
    /// "R" text (1/3 as 0.33333333333333331, where the tools write 0.3333333333333333), so the merge saw a changed value
    /// and every edit rewrote the line.
    /// </summary>
    /// <summary>
    /// A user component's collection member (List&lt;T&gt;, which the schema and the build accept as a JSON array) does not
    /// stop the Designer: the designer source holds it as a collection expression, which the Scene DSL model rejected as
    /// a "non-array collection" (SCENE110), so the whole scene could not be edited although it built.
    /// </summary>
    [Fact]
    public void ListMember_KeepsTheSceneEditable(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Route\",\n            \"members\": {\n              \"Speeds\": [0.5, 2],\n              \"Ids\": [1, 2]\n            }");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Members=SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid()));
        Assert.Contains(Members,static Member=>Member.MemberName=="Speeds");
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// A palette component's constant default that scene JSON cannot hold (a char) is left out, as any such default, so
    /// the declared default applies at build. It was written as a constant the converter has no JSON form for, and the
    /// whole add failed.
    /// </summary>
    [Fact]
    public void PaletteComponent_LeavesOutAConstantJsonCannotHold(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Type=Seed.Context.Compilation.GetTypeByMetadataName("CSharpGameEngine.Runtime.Windows.Lettered")!;
        var Initializer=SceneComponentInitializerPolicy.CreatePlan(Type,"global::CSharpGameEngine.Runtime.Windows.Lettered",Seed.Context.Compilation);
        var Add=new AddComponentOperation(Key,Find(Seed.Snapshot,"Lamp"),Seed.Snapshot.DocumentVersion,"global::CSharpGameEngine.Runtime.Windows.Lettered",Initializer.Expression,Initializer.Members);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add Lettered",[],SceneTransformMemberMask.None,DirectOperations:[Add]),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var After=Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString();
        Assert.Contains("\"type\": \"CSharpGameEngine.Runtime.Windows.Lettered\"",After);
        Assert.Contains("\"Size\": 1.5",After);
        Assert.DoesNotContain("\"Letter\"",After);
    }
    /// <summary>
    /// Integer members are edited exactly. Scene JSON writes one other than int with a cast ((byte)(200)) so an
    /// overloaded constructor gets the right type, and the Scene DSL model read that cast as a constant expression, so
    /// the Inspector showed every byte, short, uint or long member as read-only; the edit then parsed every number as a
    /// float, so 123456790 was written as 123456792 and the U/L suffixes of uint, long and ulong did not parse.
    /// </summary>
    [Theory]
    [InlineData("Tiny","200","201")]
    [InlineData("Signed","-100","-99")]
    [InlineData("Small","-5","-4")]
    [InlineData("Port","65534","65535")]
    [InlineData("Count","123456789","123456790")]
    [InlineData("Big","4000000000","4000000001")]
    [InlineData("Wide","-9000000000","-8999999999")]
    [InlineData("Huge","18446744073709551614","18446744073709551615")]
    public void IntegerMembers_AreEditable(string Name,string Value,string Next){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Counters\",\n            \"members\": {\n              \""+Name+"\": "+Value+"\n            }");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Member=Assert.Single(SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid())),
            Candidate=>Candidate.MemberName==Name);
        Assert.True(Member.IsEditable);
        Assert.Equal(InspectorEditorKind.Number,Member.EditorKind);
        Assert.Equal(Value,Member.SourceExpression);
        var Same=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Member,Member.SourceExpression),Seed.Reconciler,CancellationToken.None);
        Assert.True(Same.Succeeded,Same.ErrorMessage);
        Assert.Equal(Before,Same.WriteStep!.Before.WithChanges(Same.WriteStep.Changes).ToString());
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(Member.NumericType!.Value,Next,out var Expression));
        var Edit=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Member,Expression),Seed.Reconciler,CancellationToken.None);
        Assert.True(Edit.Succeeded,Edit.ErrorMessage);
        Assert.Equal(Before.Replace("\""+Name+"\": "+Value,"\""+Name+"\": "+Next),Edit.WriteStep!.Before.WithChanges(Edit.WriteStep.Changes).ToString());
    }
    [Theory]
    [InlineData("Tiny","256")]
    [InlineData("Signed","-129")]
    [InlineData("Count","1.5")]
    [InlineData("Huge","-1")]
    public void IntegerMember_RejectsAValueOutOfItsType(string Name,string Expression){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Counters\",\n            \"members\": {\n              \""+Name+"\": 1\n            }");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Member=Assert.Single(SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid())),
            Candidate=>Candidate.MemberName==Name);
        var Edit=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Member,Expression),Seed.Reconciler,CancellationToken.None);
        Assert.False(Edit.Succeeded);
    }
    /// <summary>
    /// A component written with constructor arguments (a positional record: scene JSON names the constructor's
    /// parameters) shows each argument as a read-only row named by its key, as a nested value's arguments are shown.
    /// The Inspector showed such a component with no rows at all, as if the document held no value for it.
    /// </summary>
    [Fact]
    public void ConstructorArguments_AreShownReadOnly(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Tuning\",\n            \"members\": {\n              \"Gain\": 0.25,\n              \"Mode\": \"Bright\"\n            }");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Lamp=Find(Seed.Snapshot,"Lamp");
        var Members=SceneInspectorModelBuilder.Build(Seed.Snapshot,new SceneSelection(Key,[Lamp],Lamp,null,null,SceneSelectionOrigin.Inspector,1,System.Guid.NewGuid()));
        Assert.Equal(["Gain","Mode"],Members.Select(static Member=>Member.DisplayName));
        Assert.Equal(["0.25f","global::CSharpGameEngine.Runtime.Windows.LightMode.Bright"],Members.Select(static Member=>Member.SourceExpression));
        Assert.All(Members,static Member=>Assert.False(Member.IsEditable));
        Assert.All(Members,static Member=>Assert.Equal(InspectorEditorKind.ReadOnly,Member.EditorKind));
    }
    [Fact]
    public void Move_KeepsDoubleMembersAsWritten(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Precise\",\n            \"members\": {\n              \"Ratio\": 0.3333333333333333,\n              \"Tiny\": 1E-300\n            }");
        Assert.Contains("\"Ratio\": 0.3333333333333333",Before);
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// An edit elsewhere leaves a decimal member as it is. .NET Framework has no "R" format for decimal and threw, so
    /// no edit could be written to a scene with a decimal member.
    /// </summary>
    [Fact]
    public void Move_KeepsDecimalMembersAsWritten(){
        var Before=Document("1").Replace(
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Light\",\n            \"members\": {\n              \"Intensity\": 3\n            }",
            "            \"type\": \"CSharpGameEngine.Runtime.Windows.Priced\",\n            \"members\": {\n              \"Price\": 1.50,\n              \"Huge\": 79228162514264337593543950335\n            }");
        Assert.Contains("\"Price\": 1.50",Before);
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
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
    /// <summary>
    /// The author's key order and the lists or ids they leave out are layout, not values: moving the Cube writes its one
    /// position line. The merge compared objects by their names in order, so any edit rewrote, in canonical form, every
    /// object written in another order or without an empty list (a whole node, with its children), and a node without an id.
    /// </summary>
    [Theory]
    [InlineData("        \"id\": \"LAMP\",\n        \"name\": \"Lamp\",\n","        \"name\": \"Lamp\",\n        \"id\": \"LAMP\",\n")]
    [InlineData("        ],\n        \"children\": []\n      }\n    ]","        ]\n      }\n    ]")]
    [InlineData("              \"RotationW\": 1,\n              \"ScaleX\": 1,\n","              \"ScaleX\": 1,\n              \"RotationW\": 1,\n")]
    [InlineData("    \"components\": [],\n    \"children\": [\n","    \"children\": [\n")]
    public void Move_KeepsTheAuthorsKeyOrderAndOmittedLists(string Original,string Replacement){
        var Before=Document("1").Replace(Original.Replace("LAMP",LampId),Replacement.Replace("LAMP",LampId));
        Assert.NotEqual(Document("1"),Before);
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// A nested component type is named in the document as C# names it (Outer.Inner), which the converter, the schema and
    /// the palette write. Only its metadata name (Outer+Inner) resolved, so every edit of a scene with one failed once
    /// written back (SCENE201) and the palette could not add one. A document that uses the metadata name still reads.
    /// </summary>
    [Theory]
    [InlineData("CSharpGameEngine.Runtime.Windows.Outer.Inner",true)]
    [InlineData("CSharpGameEngine.Runtime.Windows.Outer+Inner",false)]
    public void NestedComponentType_IsEditedAndAddedInItsCSharpForm(string TypeName,bool CSharpForm){
        var Before=Document("1").Replace("\"type\": \"CSharpGameEngine.Runtime.Windows.Light\",","\"type\": \""+TypeName+"\",")
            .Replace("\"Intensity\": 3","\"Speed\": 2");
        var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
        var Move=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Move.Succeeded,Move.ErrorMessage);
        // The metadata name is written back in the C# form, once.
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,").Replace("Outer+Inner","Outer.Inner"),Move.WriteStep!.Before.WithChanges(Move.WriteStep.Changes).ToString());
        if(!CSharpForm){ return; }
        var Type=Seed.Context.Compilation.GetTypeByMetadataName("CSharpGameEngine.Runtime.Windows.Outer+Inner")!;
        var Initializer=SceneComponentInitializerPolicy.CreatePlan(Type,"global::CSharpGameEngine.Runtime.Windows.Outer.Inner",Seed.Context.Compilation);
        var Add=new AddComponentOperation(Key,Find(Seed.Snapshot,"Cube"),Seed.Snapshot.DocumentVersion,"global::CSharpGameEngine.Runtime.Windows.Outer.Inner",Initializer.Expression,Initializer.Members);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Add Inner",[],SceneTransformMemberMask.None,DirectOperations:[Add]),Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
    }
    /// <summary>
    /// Node ids stay across edits also for siblings with the same name and component types. The reconciler gives such
    /// siblings a new editor id on every analysis, and the write-back carried JSON ids by editor id, so moving any node
    /// replaced every look-alike's id with a new GUID.
    /// </summary>
    [Fact]
    public void Move_KeepsTheIdsOfLookAlikeSiblings(){
        var LampBlock="      {\n        \"id\": \""+LampId+"\",\n        \"name\": \"Lamp\",";
        var Before=Document("1");
        var Start=Before.IndexOf(LampBlock,StringComparison.Ordinal);
        var End=Before.IndexOf("\n    ]\n  }\n}",StringComparison.Ordinal);
        var Twin=Before.Substring(Start,End-Start).Replace(LampId,"0f3c6c7a-0000-4000-8000-0000000000aa");
        Before=Before.Substring(0,End)+",\n"+Twin+Before.Substring(End);
        var Reconciler=new SceneProjectionReconciler();
        var Seed=CreateSeed(Before,Version:1,Previous:null,Reconciler);
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// A write-back replaces only the edited text in the buffer: moving the Cube changes one character. The changes were
    /// computed between unrelated texts, which gave one change over the whole document on every edit, so the JSON view
    /// lost its place, folding and bookmarks each time (the flicker M0044 removed from the C# path).
    /// </summary>
    [Fact]
    public void Move_ChangesOnlyTheEditedTextInTheBuffer(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        var Change=Assert.Single(Plan.WriteStep!.Changes);
        Assert.Equal((Document("1").IndexOf("\"R\": 1,",StringComparison.Ordinal)+5,1,"3"),(Change.Span.Start,Change.Span.Length,Change.NewText));
    }
    /// <summary>
    /// An edit writes a value as the tools on .NET write it. Since the document keeps a literal's spelling, the VSIX wrote
    /// .NET Framework's "R" text of the edited float (312985.125 for 312985.12), which reads back the same but differs from
    /// what the same edit writes on .NET.
    /// </summary>
    [Fact]
    public void Move_WritesTheValueAsDotNetSpellsIt(){
        var Seed=CreateSeed(Document("1"),Version:1,Previous:null,new SceneProjectionReconciler());
        var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
            new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(312985.125f))],SceneTransformMemberMask.Position),
            Seed.Reconciler,CancellationToken.None);
        Assert.True(Plan.Succeeded,Plan.ErrorMessage);
        Assert.Equal(Document("1").Replace("\"R\": 1,","\"R\": 312985.12,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// An edit leaves the author's spelling of other numbers alone. A float the author wrote with more digits than a float
    /// holds (3.14159265358979, 16777217) was rewritten as the float's shortest text on every unrelated edit, which read
    /// back the same but lost the author's digits and changed lines the edit had not touched.
    /// </summary>
    [Fact]
    public void Move_KeepsTheAuthorsSpellingOfOtherNumbers(){
        foreach(var Value in new[]{"3.14159265358979","16777217","0.1","1.0","1e2","0.30000001192092896","100.0000001","-0","-3.14159265358979","-16777217","1E+20","-1.5e-45"}){
            var Before=Document("1").Replace("\"Intensity\": 3","\"Intensity\": "+Value);
            var Seed=CreateSeed(Before,Version:1,Previous:null,new SceneProjectionReconciler());
            var Plan=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,
                new SceneEditCommitRequest(Key,Seed.Snapshot.DocumentVersion,"Move Cube",[new SceneTransformChange(Find(Seed.Snapshot,"Cube"),At(1f),At(3f))],SceneTransformMemberMask.Position),
                Seed.Reconciler,CancellationToken.None);
            Assert.True(Plan.Succeeded,Plan.ErrorMessage);
            Assert.Equal(Before.Replace("\"R\": 1,","\"R\": 3,"),Plan.WriteStep!.Before.WithChanges(Plan.WriteStep.Changes).ToString());
        }
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
