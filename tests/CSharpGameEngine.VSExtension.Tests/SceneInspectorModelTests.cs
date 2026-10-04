using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
using CSharpGameEngine.SceneAnalysis.Json;
using CSharpGameEngine.SceneAnalysis.Tests;

namespace CSharpGameEngine.VSExtension.Tests;

public sealed class SceneInspectorModelTests {
    private static readonly SceneKey Key=new("project-a","global::Game.MainScene");
    private const string ToggleType="""
        namespace Sample { public struct Toggle { public bool Enabled; } }
        """;

    [Fact]
    public void Build_UsesSelectedComponentAndKeepsSourceExpression(){
        var Snapshot=CreateSnapshot();
        var Light=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SceneSelection Selection=new(
            Key,[Light],Light,"global::Sample.DirectionalLight",MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());

        var Member=Assert.Single(SceneInspectorModelBuilder.Build(Snapshot,Selection));

        Assert.Equal("Intensity",Member.MemberName);
        Assert.Equal("3f",Member.SourceExpression);
        Assert.True(Member.IsEditable);
    }

    [Fact]
    public void Build_RejectsSelectionForAnotherScene(){
        var Snapshot=CreateSnapshot();
        var Light=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SceneSelection Selection=new(
            new SceneKey("project-b","global::Game.MainScene"),[Light],Light,null,null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());

        Assert.Empty(SceneInspectorModelBuilder.Build(Snapshot,Selection));
    }

    [Fact]
    public void Build_FloatMember_IsNumberEditor(){
        var Member=BuildSingleMember(CreateSnapshot(),"Light","Intensity");

        Assert.Equal(InspectorEditorKind.Number,Member.EditorKind);
        Assert.Empty(Member.Candidates);
    }

    [Fact]
    public void Build_EnumMember_ListsAllCandidatesByName(){
        var Snapshot=CreatePrimitiveSnapshot();
        var Member=BuildSingleMember(Snapshot,"Box","Type");

        Assert.Equal(InspectorEditorKind.Enum,Member.EditorKind);
        Assert.Equal(["Cube","Sphere","Plane"],Member.Candidates.Select(static C=>C.Name));
        Assert.Contains(Member.Candidates,static C=>C.EmitExpression.EndsWith("PrimitiveType.Cube",StringComparison.Ordinal));
    }

    [Fact]
    public void Build_SceneAssetReference_ListsOnlyCompatibleReadOnlyAssetSymbols(){
        var Snapshot=CreateAssetReferenceSnapshot();
        var Member=BuildSingleMember(Snapshot,"Mesh","AssetPath");

        Assert.True(Member.IsEditable);
        Assert.Equal(InspectorEditorKind.AssetReference,Member.EditorKind);
        Assert.Equal(["Floor","Wall"],Member.Candidates.Select(static Candidate=>Candidate.Name));
        Assert.All(Member.Candidates,static Candidate=>Assert.StartsWith("global::Game.Assets.",Candidate.EmitExpression));
    }

    [Fact]
    public void ChoiceCommit_AcceptsDeclaredAssetAndRejectsUndeclaredAssetWithoutAnOperation(){
        var Snapshot=CreateAssetReferenceSnapshot();
        var Member=BuildSingleMember(Snapshot,"Mesh","AssetPath");

        Assert.True(SceneInspectorCommitPlanner.TryCreateChoiceCommit(
            Key,Snapshot.DocumentVersion,Member,"Wall",out var Request));
        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Request.DirectOperations));
        Assert.EndsWith("Assets.Wall",Operation.ValueExpression,StringComparison.Ordinal);
        Assert.False(SceneInspectorCommitPlanner.TryCreateChoiceCommit(
            Key,Snapshot.DocumentVersion,Member,"Missing",out _));
        Assert.True(SceneInspectorCommitPlanner.IsCurrentChoice(Member,"Floor"));
        Assert.False(SceneInspectorCommitPlanner.TryCreateChoiceCommit(
            Key,Snapshot.DocumentVersion,Member,"Floor",out _));
    }

    [Theory]
    [InlineData("double")]
    [InlineData("decimal")]
    public void Build_NonCanonicalRealMember_IsReadOnly(string TypeName){
        var Model=SceneAnalysisTestHelper.Analyze([
            $"namespace Sample {{ public struct Legacy {{ public {TypeName} Value; }} }}",
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Legacy",Components=[new Legacy { Value=1 }] }] };
            }
            """,
        ]);
        var Snapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",1,1,Model,false,null,new SceneProjectionReconciler());
        var Member=BuildSingleMember(Snapshot,"Legacy","Value");

        Assert.Null(Member.NumericType);
        Assert.False(Member.IsEditable);
        Assert.Equal(InspectorEditorKind.ReadOnly,Member.EditorKind);
    }

    [Fact]
    public void Build_BooleanMember_IsBooleanEditorWithConstantValue(){
        var Snapshot=CreateToggleSnapshot(Enabled:true);
        var Member=BuildSingleMember(Snapshot,"Switch","Enabled");

        Assert.Equal(InspectorEditorKind.Boolean,Member.EditorKind);
        Assert.Equal(true,Member.ConstantValue);
    }

    [Fact]
    public void Build_StringMember_IsTextEditor(){
        var Snapshot=CreateLabelSnapshot();
        var Member=BuildSingleMember(Snapshot,"Sign","Text");

        Assert.Equal(InspectorEditorKind.Text,Member.EditorKind);
        Assert.Equal("Hello",Member.ConstantValue);
    }

    [Fact]
    public void Build_ConstantExpressionMember_IsReadOnlyEvenThoughTypeIsNumeric(){
        var Snapshot=CreateConstantReferencedSnapshot();
        var Member=BuildSingleMember(Snapshot,"Light","Intensity");

        Assert.False(Member.IsEditable);
        Assert.Equal(InspectorEditorKind.ReadOnly,Member.EditorKind);
    }

    [Theory]
    [InlineData("5",Microsoft.CodeAnalysis.SpecialType.System_Single,"5f")]
    [InlineData("-1.5",Microsoft.CodeAnalysis.SpecialType.System_Single,"-1.5f")]
    [InlineData("3",Microsoft.CodeAnalysis.SpecialType.System_Int32,"3")]
    [InlineData("-3",Microsoft.CodeAnalysis.SpecialType.System_Int32,"-3")]
    [InlineData("4",Microsoft.CodeAnalysis.SpecialType.System_Int64,"4L")]
    public void TryFormatNumber_ValidInput_ProducesTypeCorrectLiteral(
        string Text,Microsoft.CodeAnalysis.SpecialType Type,string Expected){
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(Type,Text,out var Expression));
        Assert.Equal(Expected,Expression);
    }

    [Theory]
    [InlineData(Microsoft.CodeAnalysis.SpecialType.System_Double)]
    [InlineData(Microsoft.CodeAnalysis.SpecialType.System_Decimal)]
    public void TryFormatNumber_NonCanonicalRealType_IsRejected(Microsoft.CodeAnalysis.SpecialType Type){
        Assert.False(SceneInspectorValueFormat.TryFormatNumber(Type,"5",out _));
    }

    [Fact]
    public void TryFormatNumber_OutOfRangeForTargetType_FailsInsteadOfOverflowingSilently(){
        // int.TryParseなら通ってしまう値。short.TryParseへ直接投げているので範囲外を確実に弾く。
        Assert.False(SceneInspectorValueFormat.TryFormatNumber(
            Microsoft.CodeAnalysis.SpecialType.System_Int16,"99999",out _));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void TryFormatNumber_TextWithoutAFiniteNumber_Fails(string Text){
        // float.TryParse accepts NaN and Infinity; their text would reach the source as the identifier "Infinityf".
        Assert.False(SceneInspectorValueFormat.TryFormatNumber(
            Microsoft.CodeAnalysis.SpecialType.System_Single,Text,out _));
    }

    [Fact]
    public void FormatText_EscapesQuotesAndBackslashes(){
        Assert.Equal("\"Hi \\\"there\\\"\"",SceneInspectorValueFormat.FormatText("Hi \"there\""));
    }

    [Fact]
    public void FormatBoolean_MapsToCSharpKeywords(){
        Assert.Equal("true",SceneInspectorValueFormat.FormatBoolean(true));
        Assert.Equal("false",SceneInspectorValueFormat.FormatBoolean(false));
    }

    [Fact]
    public void CreateCommit_OmitsOriginalValueExpressionSoTheWrittenValueIsAbsoluteNotRelative(){
        var Snapshot=CreateSnapshot();
        var Member=BuildSingleMember(Snapshot,"Light","Intensity");

        var Request=SceneInspectorCommitPlanner.CreateCommit(Key,Snapshot.DocumentVersion,Member,"9f");

        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Request.DirectOperations));
        Assert.Null(Operation.OriginalValueExpression);
        Assert.Equal("9f",Operation.ValueExpression);
        Assert.Equal(Member.MemberName,Operation.MemberName);
        Assert.Equal(Member.ComponentTypeFullyQualifiedName,Operation.ComponentTypeFullyQualifiedName);
        Assert.Equal(Key,Request.SceneKey);
        Assert.Equal(Snapshot.DocumentVersion,Request.DocumentVersion);
    }

    [Fact]
    public void BuildGroups_SplitsMembersPerComponentAndKeepsDeclarationOrder(){
        var Snapshot=CreateMultiComponentSnapshot();
        var Groups=SceneInspectorModelBuilder.BuildGroups(Snapshot,SelectAllOf(Snapshot,"Light"));

        Assert.Equal(
            ["global::Sample.DirectionalLight","global::Sample.Label"],
            Groups.Select(static Group=>Group.ComponentTypeFullyQualifiedName));
        Assert.Equal(["Intensity"],LeafNames(Groups[0]));
        Assert.Equal(["Text"],LeafNames(Groups[1]));
    }

    [Fact]
    public void BuildGroups_ShortensTheDisplayNameForTheExpanderHeader(){
        var Snapshot=CreateMultiComponentSnapshot();
        var Groups=SceneInspectorModelBuilder.BuildGroups(Snapshot,SelectAllOf(Snapshot,"Light"));

        Assert.Equal(["DirectionalLight","Label"],Groups.Select(static Group=>Group.DisplayName));
    }

    [Fact]
    public void BuildGroups_HonoursTheComponentTypeFilter(){
        var Snapshot=CreateMultiComponentSnapshot();
        var NodeId=Assert.Single(Snapshot.Projection!.Nodes,static Node=>Node.Node.Name=="Light").Id;
        SceneSelection Selection=new(
            Key,[NodeId],NodeId,"global::Sample.Label",MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());

        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(Snapshot,Selection));

        Assert.Equal("global::Sample.Label",Group.ComponentTypeFullyQualifiedName);
    }

    [Fact]
    public void BuildGroups_NoSelection_IsEmpty()=>
        Assert.Empty(SceneInspectorModelBuilder.BuildGroups(CreateSnapshot(),Selection:null));

    /// <summary>
    /// グルーピングの有無で行の集合が変わってはいけない。<c>Build</c>は<c>BuildGroups</c>の
    /// 平坦化として定義されているので、その関係をここで固定する。
    /// </summary>
    [Fact]
    public void Build_IsExactlyTheFlattenedFormOfBuildGroups(){
        var Snapshot=CreateMultiComponentSnapshot();
        var Selection=SelectAllOf(Snapshot,"Light");

        Assert.Equal(
            SceneInspectorModelBuilder.BuildGroups(Snapshot,Selection)
                .SelectMany(static Group=>FlattenLeaves(Group.Rows))
                .Select(static Member=>(Member.ComponentTypeFullyQualifiedName,Member.MemberName)),
            SceneInspectorModelBuilder.Build(Snapshot,Selection)
                .Select(static Member=>(Member.ComponentTypeFullyQualifiedName,Member.MemberName)));
    }

    [Fact]
    public void BuildGroups_NestedObjectCreationWithInitializer_BecomesAnExpandableGroupOfReadOnlyChildren(){
        var Snapshot=CreateVector3PositionSnapshot(UseInitializer:true);
        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(Snapshot,SelectAllOf(Snapshot,"Thing")));

        var Nested=Assert.IsType<SceneInspectorNestedRow>(Assert.Single(Group.Rows));
        Assert.Equal("Position",Nested.DisplayName);
        Assert.Equal("Vector3",Nested.TypeDisplayName);
        Assert.Equal(["Position"],Nested.MemberPath);

        var Children=Nested.Rows.Cast<SceneInspectorMemberRow>().Select(static Row=>Row.Member).ToArray();
        Assert.Equal(["X","Y","Z"],Children.Select(static Child=>Child.MemberName));
        // 安全装置: ネストの葉はSceneValue.IsEditable(=true, Literal)であっても強制的に読み取り専用になる。
        // SceneModelEditApplierはComponent直下でしか名前一致を探さず、見つからなければ黙って新規
        // メンバーを生やすため、ここが編集可能に見えると偽メンバー混入バグを踏む。
        Assert.All(Children,static Child=>{
            Assert.False(Child.IsEditable);
            Assert.Equal(InspectorEditorKind.ReadOnly,Child.EditorKind);
        });
    }

    [Fact]
    public void BuildGroups_NestedObjectCreationWithPositionalArguments_ResolvesConstructorParameterNames(){
        var Snapshot=CreateVector3PositionSnapshot(UseInitializer:false);
        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(Snapshot,SelectAllOf(Snapshot,"Thing")));

        var Nested=Assert.IsType<SceneInspectorNestedRow>(Assert.Single(Group.Rows));
        var ChildNames=Nested.Rows.Cast<SceneInspectorMemberRow>().Select(static Row=>Row.Member.MemberName).ToArray();
        // Sample.Vector3のコンストラクタは(float X,float Y,float Z)の1つしか無いので一意に解決できる。
        Assert.Equal(["X","Y","Z"],ChildNames);
    }

    [Fact]
    public void BuildGroups_NestedObjectCreationMixingArgumentsAndInitializer_ListsBoth(){
        // `new Vector3(1f,2f,3f) { Y=9f }`のように、コンストラクタ引数と初期化子は同じ式に共存できる。
        var Model=SceneAnalysisTestHelper.Analyze([
                                                       """
                                                       namespace Sample {
                                                           public struct Vector3 {
                                                               public Vector3(float X,float Y,float Z){ this.X=X;this.Y=Y;this.Z=Z; }
                                                               public float X;public float Y;public float Z;
                                                           }
                                                           public struct Thing { public Vector3 Position; }
                                                       }
                                                       """,
                                                       """
                                                       using CSharpGameEngine.Entities;
                                                       using Sample;
                                                       namespace Game;
                                                       public partial class MainScene:IScene {
                                                           public Node Root=>new() { Children=[new Node { Name="Thing",Components=[new Thing { Position=new Vector3(1f,2f,3f) { Y=9f } }] }] };
                                                       }
                                                       """,
                                                   ]);
        var DocumentSnapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(DocumentSnapshot,SelectAllOf(DocumentSnapshot,"Thing")));

        var Nested=Assert.IsType<SceneInspectorNestedRow>(Assert.Single(Group.Rows));
        var ChildNames=Nested.Rows.Cast<SceneInspectorMemberRow>().Select(static Row=>Row.Member.MemberName).ToArray();
        // 初期化子(Members)が先、位置引数(Arguments)が後: BuildGroups_NestedObjectCreationWithInitializer
        // と同じ順で組み立てているので、混在時もその順で両方載る。
        Assert.Equal(["Y","X","Y","Z"],ChildNames);
    }

    [Fact]
    public void BuildGroups_EmptyObjectCreation_StaysAsAPlainReadOnlyLeafInsteadOfAnEmptyGroup(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                       "namespace Sample { public struct Empty { } public struct Thing { public Empty Value; } }",
                                                       """
                                                       using CSharpGameEngine.Entities;
                                                       using Sample;
                                                       namespace Game;
                                                       public partial class MainScene:IScene {
                                                           public Node Root=>new() { Children=[new Node { Name="Thing",Components=[new Thing { Value=new Empty() }] }] };
                                                       }
                                                       """,
                                                   ]);
        var DocumentSnapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(DocumentSnapshot,SelectAllOf(DocumentSnapshot,"Thing")));

        var Leaf=Assert.IsType<SceneInspectorMemberRow>(Assert.Single(Group.Rows));
        Assert.Equal("Value",Leaf.Member.MemberName);
        Assert.Equal(InspectorEditorKind.ReadOnly,Leaf.Member.EditorKind);
    }

    private static IEnumerable<SceneInspectorMember>FlattenLeaves(IReadOnlyList<SceneInspectorRow>Rows){
        foreach(var Row in Rows){
            switch(Row){
                case SceneInspectorMemberRow Leaf:
                    yield return Leaf.Member;
                    break;
                case SceneInspectorNestedRow Nested:
                    foreach(var Member in FlattenLeaves(Nested.Rows)){
                        yield return Member;
                    }

                    break;
            }
        }
    }

    [Fact]
    public void BuildGroups_RufTransformVectors_AreEditableNestedLeavesWithMemberPaths(){
        const string Types="""
            namespace Sample {
                public struct Vector3 { public Vector3(float R,float U,float F){} }
                public struct Transform { public Vector3 Position; public Vector3 Rotation; public Vector3 Scale; }
            }
            """;
        var Model=SceneAnalysisTestHelper.Analyze([
            Types,
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Thing",Components=[new Transform {
                    Position=new Vector3(1f,2f,3f), Rotation=new Vector3(0f,0f,0f), Scale=new Vector3(1f,1f,1f),
                }] }] };
            }
            """,
        ]);
        var Snapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
        var Group=Assert.Single(SceneInspectorModelBuilder.BuildGroups(Snapshot,SelectAllOf(Snapshot,"Thing")));

        Assert.Equal(["Position","Rotation","Scale"],Group.Rows.Cast<SceneInspectorNestedRow>().Select(static Row=>Row.DisplayName));
        var Position=Assert.IsType<SceneInspectorNestedRow>(Group.Rows[0]);
        var Leaves=Position.Rows.Cast<SceneInspectorMemberRow>().Select(static Row=>Row.Member).ToArray();
        Assert.Equal(["R","U","F"],Leaves.Select(static Member=>Member.MemberName));
        Assert.All(Leaves,static Member=>Assert.True(Member.IsEditable));
        Assert.Equal(["Position","R"],Leaves[0].DefinitionMemberPath);

        var Request=SceneInspectorCommitPlanner.CreateCommit(Snapshot.Key,Snapshot.DocumentVersion,Leaves[0],"9f");
        var Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Request.DirectOperations));
        Assert.Equal(["Position","R"],Operation.MemberPath.ToArray());
    }    private static IEnumerable<string>LeafNames(SceneInspectorComponentGroup Group)=>
        FlattenLeaves(Group.Rows).Select(static Member=>Member.MemberName);

    private static SceneDocumentSnapshot CreateVector3PositionSnapshot(bool UseInitializer){
        const string Vector3Type="""
            namespace Sample { public struct Vector3 { public Vector3(float X,float Y,float Z){ this.X=X;this.Y=Y;this.Z=Z; } public float X;public float Y;public float Z; } }
            """;
        const string ThingType="""
            namespace Sample { public struct Thing { public Vector3 Position; } }
            """;
        var PositionExpression=UseInitializer
            ?"new Vector3 { X=1f,Y=2f,Z=3f }"
            :"new Vector3(1f,2f,3f)";
        var Model=SceneAnalysisTestHelper.Analyze([
                                                       Vector3Type,
                                                       ThingType,
                                                       $$"""
                                                         using CSharpGameEngine.Entities;
                                                         using Sample;
                                                         namespace Game;
                                                         public partial class MainScene:IScene {
                                                             public Node Root=>new() { Children=[new Node { Name="Thing",Components=[new Thing { Position={{PositionExpression}} }] }] };
                                                         }
                                                         """,
                                                   ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    private static SceneSelection SelectAllOf(SceneDocumentSnapshot Snapshot,string NodeName){
        var NodeId=Assert.Single(Snapshot.Projection!.Nodes,Node=>Node.Node.Name==NodeName).Id;
        return new SceneSelection(
            Key,[NodeId],NodeId,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());
    }

    private static SceneDocumentSnapshot CreateMultiComponentSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Children=[new Node { Name="Light",Components=[new DirectionalLight { Intensity=3f },new Label { Text="Hi" }] }] };
                                                  }
                                                  """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    /// <summary>
    /// ADR-0074: an angle member, and every number inside one (an Euler rotation's R, U, F), shows and takes degrees
    /// while the source keeps the member's unit.
    /// </summary>
    [Fact]
    public void Build_AngleMembers_ShowAndTakeDegrees(){
        var Model=SceneAnalysisTestHelper.Analyze([SceneAnalysisTestHelper.CommonComponents,AngleTypes,"""
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Eye",Components=[new Aim { FovRadians=1.5707964f,Plain=2f },new Pose { Rotation=new Turn(EulerRuf: new Euler(R: 0.7853982f,U: 0f,F: 0f)) }] }] };
            }
            """]);
        var Snapshot=SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
        var Fov=BuildSingleMember(Snapshot,"Eye","FovRadians");
        Assert.Equal(SceneAngleUnit.Radians,Fov.AngleUnit);
        var FovRow=new InspectorMemberViewModel(Fov);
        Assert.Equal("90",FovRow.DisplayText);
        // The label is the scene JSON key, which leaves out the C# unit word (ADR-0074); edits still name the C# member.
        Assert.Equal("Fov (°)",FovRow.MemberName);
        Assert.Equal("FovRadians",Fov.MemberName);
        Assert.True(SceneInspectorValueFormat.TryFormatAngle(Fov.NumericType!.Value,"45",Fov.AngleUnit!.Value,out var Expression));
        Assert.Equal("0.7853982f",Expression);
        Assert.Null(BuildSingleMember(Snapshot,"Eye","Plain").AngleUnit);
        var Pitch=BuildSingleMember(Snapshot,"Eye","R");
        Assert.Equal(SceneAngleUnit.Radians,Pitch.AngleUnit);
        Assert.Equal("45",new InspectorMemberViewModel(Pitch).DisplayText);
    }

    private const string AngleTypes="""
        namespace CSharpGameEngine.Contracts {
            public enum AngleUnit:byte { Radians,Degrees }
            [System.AttributeUsage(System.AttributeTargets.Field|System.AttributeTargets.Property|System.AttributeTargets.Parameter)]
            public sealed class AngleAttribute:System.Attribute { public AngleAttribute(AngleUnit Unit){ this.Unit=Unit; } public AngleUnit Unit { get; } }
        }
        namespace Sample {
            using CSharpGameEngine.Contracts;
            public struct Aim { [Angle(AngleUnit.Radians)] public float FovRadians; public float Plain; }
            public struct Euler { public Euler(float R,float U,float F){ this.R=R; this.U=U; this.F=F; } public float R; public float U; public float F; }
            public struct Turn { public Turn([Angle(AngleUnit.Radians)] Euler EulerRuf){ this.EulerRuf=EulerRuf; } public Euler EulerRuf; }
            public struct Pose { public Turn Rotation; }
        }
        """;

    private static SceneInspectorMember BuildSingleMember(SceneDocumentSnapshot Snapshot,string NodeName,string MemberName){
        var NodeId=Assert.Single(Snapshot.Projection!.Nodes,Node=>Node.Node.Name==NodeName).Id;
        SceneSelection Selection=new(
            Key,[NodeId],NodeId,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());
        return Assert.Single(SceneInspectorModelBuilder.Build(Snapshot,Selection),Member=>Member.MemberName==MemberName);
    }

    private static SceneDocumentSnapshot CreateSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze("""
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

    private static SceneDocumentSnapshot CreatePrimitiveSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Children=[new Node { Name="Box",Components=[new PrimitiveMesh { Type=PrimitiveType.Cube }] }] };
                                                  }
                                                  """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    private static SceneDocumentSnapshot CreateAssetReferenceSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze([
            """
            using CSharpGameEngine.Authoring;
            namespace Game;
            [SceneAssets]
            public static class Assets {
                public const string Floor="Assets/Floor.glb";
                public const string Wall="Assets/Wall.glb";
                public static string Mutable="Assets/Mutable.glb";
                public const int WrongType=1;
            }
            """,
            "namespace Sample { public struct StaticMesh { public string AssetPath; } }",
            """
            using CSharpGameEngine.Entities;
            using Sample;
            namespace Game;
            public partial class MainScene:IScene {
                public Node Root=>new() { Children=[new Node { Name="Mesh",Components=[new StaticMesh { AssetPath=Assets.Floor }] }] };
            }
            """,
        ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    private static SceneDocumentSnapshot CreateToggleSnapshot(bool Enabled){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      ToggleType,
                                                      $$"""
                                                        using CSharpGameEngine.Entities;
                                                        using Sample;
                                                        namespace Game;
                                                        public partial class MainScene:IScene {
                                                            public Node Root=>new() { Children=[new Node { Name="Switch",Components=[new Toggle { Enabled={{(Enabled?"true":"false")}} }] }] };
                                                        }
                                                        """,
                                                  ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    private static SceneDocumentSnapshot CreateLabelSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze("""
                                                  using CSharpGameEngine.Entities;
                                                  using Sample;
                                                  namespace Game;
                                                  public partial class MainScene:IScene {
                                                      public Node Root=>new() { Children=[new Node { Name="Sign",Components=[new Label { Text="Hello" }] }] };
                                                  }
                                                  """);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }

    private static SceneDocumentSnapshot CreateConstantReferencedSnapshot(){
        var Model=SceneAnalysisTestHelper.Analyze([
                                                      SceneAnalysisTestHelper.CommonComponents,
                                                      "namespace Game { public static class Constants { public const float Intensity=7f; } }",
                                                      """
                                                      using CSharpGameEngine.Entities;
                                                      using Sample;
                                                      namespace Game;
                                                      public partial class MainScene:IScene {
                                                          public Node Root=>new() { Children=[new Node { Name="Light",Components=[new DirectionalLight { Intensity=Constants.Intensity }] }] };
                                                      }
                                                      """,
                                                  ]);
        return SceneDocumentSnapshot.Create(
            Key,"document-a",DocumentVersion:1,AnalysisGeneration:1,Model,HasBlockingErrors:false,Previous:null,new SceneProjectionReconciler());
    }
}
