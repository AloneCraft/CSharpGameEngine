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

    [Fact]
    public void Build_FloatMember_IsNumberEditor(){
        SceneInspectorMember Member=BuildSingleMember(CreateSnapshot(),"Light","Intensity");

        Assert.Equal(InspectorEditorKind.Number,Member.EditorKind);
        Assert.Empty(Member.EnumCandidates);
    }

    [Fact]
    public void Build_EnumMember_ListsAllCandidatesByName(){
        SceneDocumentSnapshot Snapshot=CreatePrimitiveSnapshot();
        SceneInspectorMember Member=BuildSingleMember(Snapshot,"Box","Type");

        Assert.Equal(InspectorEditorKind.Enum,Member.EditorKind);
        Assert.Equal(["Cube","Sphere","Plane"],Member.EnumCandidates.Select(static C=>C.Name));
        Assert.Contains(Member.EnumCandidates,static C=>C.EmitExpression.EndsWith("PrimitiveType.Cube",StringComparison.Ordinal));
    }

    [Fact]
    public void Build_BooleanMember_IsBooleanEditorWithConstantValue(){
        SceneDocumentSnapshot Snapshot=CreateToggleSnapshot(Enabled:true);
        SceneInspectorMember Member=BuildSingleMember(Snapshot,"Switch","Enabled");

        Assert.Equal(InspectorEditorKind.Boolean,Member.EditorKind);
        Assert.Equal(true,Member.ConstantValue);
    }

    [Fact]
    public void Build_StringMember_IsTextEditor(){
        SceneDocumentSnapshot Snapshot=CreateLabelSnapshot();
        SceneInspectorMember Member=BuildSingleMember(Snapshot,"Sign","Text");

        Assert.Equal(InspectorEditorKind.Text,Member.EditorKind);
        Assert.Equal("Hello",Member.ConstantValue);
    }

    [Fact]
    public void Build_ConstantExpressionMember_IsReadOnlyEvenThoughTypeIsNumeric(){
        SceneDocumentSnapshot Snapshot=CreateConstantReferencedSnapshot();
        SceneInspectorMember Member=BuildSingleMember(Snapshot,"Light","Intensity");

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
        Assert.True(SceneInspectorValueFormat.TryFormatNumber(Type,Text,out string Expression));
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
        SceneDocumentSnapshot Snapshot=CreateSnapshot();
        SceneInspectorMember Member=BuildSingleMember(Snapshot,"Light","Intensity");

        SceneEditCommitRequest Request=SceneInspectorCommitPlanner.CreateCommit(Key,Snapshot.DocumentVersion,Member,"9f");

        SetComponentMemberOperation Operation=Assert.IsType<SetComponentMemberOperation>(Assert.Single(Request.DirectOperations));
        Assert.Null(Operation.OriginalValueExpression);
        Assert.Equal("9f",Operation.ValueExpression);
        Assert.Equal(Member.MemberName,Operation.MemberName);
        Assert.Equal(Member.ComponentTypeFullyQualifiedName,Operation.ComponentTypeFullyQualifiedName);
        Assert.Equal(Key,Request.SceneKey);
        Assert.Equal(Snapshot.DocumentVersion,Request.DocumentVersion);
    }

    private static SceneInspectorMember BuildSingleMember(SceneDocumentSnapshot Snapshot,string NodeName,string MemberName){
        EditorNodeId NodeId=Assert.Single(Snapshot.Projection!.Nodes,Node=>Node.Node.Name==NodeName).Id;
        SceneSelection Selection=new(
            Key,[NodeId],NodeId,ComponentType:null,MemberName:null,
            SceneSelectionOrigin.Inspector,Revision:1,Guid.NewGuid());
        return Assert.Single(SceneInspectorModelBuilder.Build(Snapshot,Selection),Member=>Member.MemberName==MemberName);
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

    private static SceneDocumentSnapshot CreatePrimitiveSnapshot(){
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
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
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
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
        SceneModel Model=SceneAnalysisTestHelper.Analyze("""
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
        SceneModel Model=SceneAnalysisTestHelper.Analyze([
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
