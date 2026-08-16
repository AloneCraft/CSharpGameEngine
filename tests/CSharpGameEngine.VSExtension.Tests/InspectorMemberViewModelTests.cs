using System.Reflection;
using CSharpGameEngine.SceneAnalysis;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// <see cref="InspectorMemberViewModel"/>のWPFバインディング契約。WPFのバインディングはpublicプロパティ
/// しか解決できず、internalだと例外も警告もなく黙って失敗する(SceneNodeViewModelTestsが固定している
/// のと同じ不具合クラス)。InternalsVisibleToのおかげでテストからはinternalでも読めてしまうため、
/// アクセシビリティ自体をここで明示的に固定する。
/// </summary>
public sealed class InspectorMemberViewModelTests {
    [Theory]
    [InlineData(nameof(InspectorMemberViewModel.Label))]
    [InlineData(nameof(InspectorMemberViewModel.EditorKind))]
    [InlineData(nameof(InspectorMemberViewModel.DisplayText))]
    [InlineData(nameof(InspectorMemberViewModel.BooleanValue))]
    [InlineData(nameof(InspectorMemberViewModel.SelectedEnumName))]
    [InlineData(nameof(InspectorMemberViewModel.EnumCandidateNames))]
    public void PropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(InspectorMemberViewModel)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }

    [Fact]
    public void Constructor_NumberMember_ShowsSourceExpressionAsDisplayText(){
        SceneInspectorMember Member=new(
            new EditorNodeId(1),"global::Sample.DirectionalLight","Intensity","3f",
            IsEditable:true,InspectorEditorKind.Number,ConstantValue:3f,
            Microsoft.CodeAnalysis.SpecialType.System_Single,EnumCandidates:[]);

        InspectorMemberViewModel ViewModel=new(Member);

        Assert.Equal("DirectionalLight.Intensity",ViewModel.Label);
        Assert.Equal("3f",ViewModel.DisplayText);
    }

    [Fact]
    public void Constructor_TextMember_UsesConstantValueNotTheQuotedSourceExpression(){
        SceneInspectorMember Member=new(
            new EditorNodeId(1),"global::Sample.Label","Text","\"Hello\"",
            IsEditable:true,InspectorEditorKind.Text,ConstantValue:"Hello",
            NumericType:null,EnumCandidates:[]);

        InspectorMemberViewModel ViewModel=new(Member);

        Assert.Equal("Hello",ViewModel.DisplayText);
    }

    [Fact]
    public void Constructor_EnumMember_SelectsTheTrailingIdentifierFromTheSourceExpression(){
        SceneInspectorMember Member=new(
            new EditorNodeId(1),"global::Sample.PrimitiveMesh","Type","Sample.PrimitiveType.Sphere",
            IsEditable:true,InspectorEditorKind.Enum,ConstantValue:null,NumericType:null,
            EnumCandidates:[
                new InspectorEnumCandidate("Cube","global::Sample.PrimitiveType.Cube"),
                new InspectorEnumCandidate("Sphere","global::Sample.PrimitiveType.Sphere"),
            ]);

        InspectorMemberViewModel ViewModel=new(Member);

        Assert.Equal("Sphere",ViewModel.SelectedEnumName);
        Assert.Equal(["Cube","Sphere"],ViewModel.EnumCandidateNames);
    }
}
