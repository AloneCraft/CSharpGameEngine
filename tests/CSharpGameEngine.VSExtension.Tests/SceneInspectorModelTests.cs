using System;
using System.Linq;
using CSharpGameEngine.SceneAnalysis;
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
        Assert.Empty(Member.EnumCandidates);
    }

    [Fact]
    public void Build_EnumMember_ListsAllCandidatesByName(){
        var Snapshot=CreatePrimitiveSnapshot();
        var Member=BuildSingleMember(Snapshot,"Box","Type");

        Assert.Equal(InspectorEditorKind.Enum,Member.EditorKind);
        Assert.Equal(["Cube","Sphere","Plane"],Member.EnumCandidates.Select(static C=>C.Name));
        Assert.Contains(Member.EnumCandidates,static C=>C.EmitExpression.EndsWith("PrimitiveType.Cube",StringComparison.Ordinal));
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
    [InlineData("5",Microsoft.CodeAnalysis.SpecialType.System_Double,"5d")]
    [InlineData("3",Microsoft.CodeAnalysis.SpecialType.System_Int32,"3")]
    [InlineData("-3",Microsoft.CodeAnalysis.SpecialType.System_Int32,"-3")]
    [InlineData("4",Microsoft.CodeAnalysis.SpecialType.System_Int64,"4L")]
    [InlineData("4",Microsoft.CodeAnalysis.SpecialType.System_Decimal,"4m")]
    public void TryFormatNumber_ValidInput_ProducesTypeCorrectLiteral(
        string Text,Microsoft.CodeAnalysis.SpecialType Type,string Expected){
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(Type,Text,out var Expression));
        Assert.Equal(Expected,Expression);
    }

    [Fact]
    public void TryFormatNumber_OutOfRangeForTargetType_FailsInsteadOfOverflowingSilently(){
        // int.TryParseなら通ってしまう値。short.TryParseへ直接投げているので範囲外を確実に弾く。
        Assert.False(SceneInspectorValueFormat.TryFormatNumber(
            Microsoft.CodeAnalysis.SpecialType.System_Int16,"99999",out _));
    }

    [Fact]
    public void TryFormatNumber_NonNumericText_Fails(){
        Assert.False(SceneInspectorValueFormat.TryFormatNumber(
            Microsoft.CodeAnalysis.SpecialType.System_Single,"abc",out _));
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

    private static IEnumerable<string>LeafNames(SceneInspectorComponentGroup Group)=>
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
