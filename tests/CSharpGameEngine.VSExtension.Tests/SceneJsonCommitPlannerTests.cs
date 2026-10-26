using System.Linq;
using System.Numerics;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis.Tests;
using Microsoft.CodeAnalysis;
namespace CSharpGameEngine.VSExtension.Tests;
/// <summary>
/// ADR-0070：JSON シーンに対する操作は、編集後のモデルから書き戻す。書き込みは編集された行だけで、ノード ID を維持し、結果は有効なシーンドキュメントとなる。
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
    /// ADR-0070（M0095 による改訂）：Scene Designer はビルドの規則でシーンを解析する。そのため、ビルドで拒否されるドキュメント（不明なキー、メンバー、型）は解析も書き込みも行わない。JSON ビューで修正し、同じエラーを表示する。
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
    /// <summary>M0095 3-5：手作業で書式を整えたドキュメントへの操作では、編集した値だけが変更される。</summary>
    /// <summary>
    /// 起動シーンを編集しても起動シーンのまま保つ。以前はデザイナーのモデルが <c>startupOrder</c> を失い、最初の編集でドキュメントから削除されて、ゲームがシーンを読み込めなくなっていた。
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
    /// Inspector の列挙値エディターとテキストエディターは、JSON シーンへ書き戻す。以前は値が C# のテキスト編集としてしか受け入れられなかった。JSON シーンにはそれがないため、列挙値や文字列を編集するたびに失敗していた。
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
    /// null 文字列は編集可能なテキスト値のままにする。Scene JSON は参照型の null を null! として書き出すため（生成コードで警告が出ない）、モデルはそれを読み取り専用の定数式ではなく null リテラルとして読み込む。
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
        // テキストフィールドが送るのは変更された値だけで、常に引用符付き文字列となる (InspectorTextCommitPolicy, FormatText)。
        var Edit=SceneSourceCommitPlanner.Plan(Seed.Context,Seed.Snapshot,SceneInspectorCommitPlanner.CreateCommit(Key,Seed.Snapshot.DocumentVersion,Label,SceneInspectorValueFormat.FormatText("Lit")),Seed.Reconciler,CancellationToken.None);
        Assert.True(Edit.Succeeded,Edit.ErrorMessage);
        Assert.Equal(Before.Replace("\"Label\": null","\"Label\": \"Lit\""),Edit.WriteStep!.Before.WithChanges(Edit.WriteStep.Changes).ToString());
    }
    /// <summary>
    /// LogicalScalar メンバー（エンジンが作成する実数：キャラクターの移動、スプリングアーム、Transform 内のすべての RUF ベクトル）は、JSON 数値として読み取られるとおり Inspector で float として編集する。以前は読み取り専用だったため、Inspector が提供する RUF ベクトル編集をエンジン自身のベクトルで利用できなかった。
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
    /// パレットから追加したコンポーネントは、宣言された既定値で書き出す。既定値は C# テキストとしてしか保持されなかったため、float や enum の既定値が null (SCENE203) になるか例外が発生し、エンジン標準のコンポーネント（Transform、ライト、CharacterMotion など）をシーンに追加できなかった。既定値が JSON で表現できない場合（static メンバーなど）は除外し、シーンのビルド時にも同じ既定値が適用されるようにする。
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
    /// enum の既定値は、宣言式が何であっても、それが示すメンバー名で書き出す（(ShapeKind)1 は Sphere）。単一のメンバー名で表せない値（flags の組み合わせ）は Scene JSON 形式にできないため除外し、JSON で保持できない他の既定値と同様に宣言上の既定値を適用する。定数が数値で書き出され、enum のメンバー名文字列として拒否されていた (SCENE203) ため、そのようなコンポーネントを追加できなかった。
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
    /// 別の箇所を編集しても double メンバーのテキストはそのままにする。VSIX（.NET Framework）は独自の "R" 表記で double を書き出し（1/3 は 0.33333333333333331、ツールでは 0.3333333333333333）、マージ処理が値の変更と判断して、編集のたびにその行を書き直していた。
    /// </summary>
    /// <summary>
    /// ユーザーコンポーネントのコレクションメンバー（スキーマとビルドでは JSON 配列として認められる List&lt;T&gt;）があっても Designer は停止しない。Designer のソースはコレクション式として保持するが、Scene DSL モデルがそれを「配列でないコレクション」(SCENE110) として拒否していたため、ビルドできるシーンを一切編集できなかった。
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
    /// パレットコンポーネントの定数既定値が Scene JSON で保持できない場合（char など）は、他の同様な既定値と同じく省略し、ビルド時に宣言された既定値を適用する。以前はコンバーターが JSON 形式にできない定数として書き出され、追加処理全体が失敗していた。
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
    /// 整数メンバーは正確に編集する。Scene JSON は int 以外の数値をキャスト付きで書き出す（((byte)(200))）ため、オーバーロードされたコンストラクターが正しい型を受け取る。Scene DSL モデルがそのキャストを定数式として読み込んでいたため、Inspector では byte、short、uint、long の各メンバーがすべて読み取り専用になっていた。さらに編集値はすべて float として解析され、123456790 は 123456792 と書き出され、uint、long、ulong の U/L サフィックスも解析できなかった。
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
    /// コンストラクター引数で書かれたコンポーネント（位置引数を持つ record：Scene JSON はコンストラクターのパラメーターを名前にする）では、各引数をキー名付きの読み取り専用行として表示する。入れ子の値の引数と同様である。以前 Inspector は値が存在しないかのように、そのコンポーネントを行なしで表示していた。
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
    /// 別の箇所を編集しても decimal メンバーのテキストはそのままにする。.NET Framework には decimal の "R" 書式がなく例外が発生したため、decimal メンバーのあるシーンには編集を書き込めなかった。
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
    /// 作成者のキー順、および省略されているリストや ID は値ではなくレイアウトである。Cube の移動では位置の 1 行だけを書き込む。マージ処理はオブジェクトを名前順で比較していたため、別の順序で書かれたオブジェクトや空リストのないオブジェクトを含むあらゆる編集で、（子を含むノード全体を）標準形式に書き直していた。ID のないノードも同様だった。
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
    /// 入れ子のコンポーネント型は C# の名前 (Outer.Inner) でドキュメントに記述し、コンバーター、スキーマ、パレットもその名前を出力する。メタデータ名 (Outer+Inner) でしか解決できなかったため、該当するシーンを編集すると書き戻し時に毎回失敗し (SCENE201)、パレットからも追加できなかった。メタデータ名を使うドキュメントは引き続き読み込める。
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
        // メタデータ名は一度だけ C# 形式にして書き戻す。
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
    /// 同名かつ同じコンポーネント型を持つ兄弟ノードでも、編集後にノード ID を維持する。調整器は解析のたびにそのような兄弟へ新しいエディター ID を付けていた。JSON ID をエディター ID に結び付けて書き戻していたため、ノードを移動するたびに見分けのつかない兄弟すべての ID が新しい GUID に置き換わっていた。
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
    /// 書き戻しではバッファー内の編集テキストだけを置換する。Cube の移動なら 1 文字が変わる。以前は無関係なテキスト間で変更を計算し、毎回ドキュメント全体を 1 つの変更としていたため、JSON ビューは編集のたびにカーソル位置、折りたたみ、ブックマークを失っていた（C# 経路では M0044 で解消済みのちらつき）。
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
    /// 値の書き出しには .NET のツールと同じ形式を使う。ドキュメントではリテラルの表記を維持するため、VSIX は編集した float を .NET Framework の "R" 表記で書き出していた（312985.12 に対して 312985.125）。同じ値に読み戻せるが、.NET で同じ編集を行ったときの表記とは異なる。
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
    /// 別の数値については作成者の表記を維持する。float の桁数より多く記述された数値 (3.14159265358979、16777217) は、関係のない編集のたびに float の最短表記へ書き直されていた。値は同じに読み戻せるが作成者の桁が失われ、編集していない行まで変わっていた。
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
