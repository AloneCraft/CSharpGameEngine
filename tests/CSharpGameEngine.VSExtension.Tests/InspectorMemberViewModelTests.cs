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
    [InlineData(nameof(InspectorMemberViewModel.MemberName))]
    [InlineData(nameof(InspectorMemberViewModel.EditorKind))]
    [InlineData(nameof(InspectorMemberViewModel.DisplayText))]
    [InlineData(nameof(InspectorMemberViewModel.BooleanValue))]
    [InlineData(nameof(InspectorMemberViewModel.SelectedEnumName))]
    [InlineData(nameof(InspectorMemberViewModel.EnumCandidateNames))]
    public void PropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(InspectorMemberViewModel)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }

    [Theory]
    [InlineData(nameof(InspectorComponentGroupViewModel.Header))]
    [InlineData(nameof(InspectorComponentGroupViewModel.FullTypeName))]
    [InlineData(nameof(InspectorComponentGroupViewModel.Rows))]
    [InlineData(nameof(InspectorComponentGroupViewModel.IsExpanded))]
    [InlineData(nameof(InspectorComponentGroupViewModel.PendingMessageVisibility))]
    public void GroupPropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(InspectorComponentGroupViewModel)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }

    /// <summary>
    /// <c>IsExpanded</c>はTwoWayバインドで、ユーザーの開閉をViewModelへ書き戻してもらう必要がある。
    /// setterが無いとWPFはバインディングを一方向に落として黙って動かなくなる。
    /// </summary>
    [Fact]
    public void GroupIsExpandedHasAPublicSetterSoTwoWayBindingCanWriteBack(){
        var Property=typeof(InspectorComponentGroupViewModel)
            .GetProperty(nameof(InspectorComponentGroupViewModel.IsExpanded),BindingFlags.Public|BindingFlags.Instance);

        Assert.NotNull(Property!.GetSetMethod());
    }

    [Theory]
    [InlineData(nameof(InspectorNestedRowViewModel.Header))]
    [InlineData(nameof(InspectorNestedRowViewModel.TypeDisplayName))]
    [InlineData(nameof(InspectorNestedRowViewModel.Rows))]
    [InlineData(nameof(InspectorNestedRowViewModel.IsExpanded))]
    public void NestedRowPropertiesBoundFromXamlArePublicSoWpfCanResolveThem(string PropertyName){
        Assert.NotNull(typeof(InspectorNestedRowViewModel)
            .GetProperty(PropertyName,BindingFlags.Public|BindingFlags.Instance));
    }

    [Fact]
    public void NestedRowIsExpandedHasAPublicSetterSoTwoWayBindingCanWriteBack(){
        var Property=typeof(InspectorNestedRowViewModel)
            .GetProperty(nameof(InspectorNestedRowViewModel.IsExpanded),BindingFlags.Public|BindingFlags.Instance);

        Assert.NotNull(Property!.GetSetMethod());
    }

    [Fact]
    public void GroupWithoutRows_ShowsThePendingAnalysisMessage(){
        // Componentを追加した直後の状態。memberが空でも「追加が消えた」ように見せてはいけない。
        InspectorComponentGroupViewModel Empty=new(
            new SceneInspectorComponentGroup(new EditorNodeId(1),"global::Sample.Label","Label",[]),
            Rows:[],IsExpanded:true);

        Assert.Equal(System.Windows.Visibility.Visible,Empty.PendingMessageVisibility);
        Assert.Empty(Empty.Rows);
    }

    [Fact]
    public void GroupWithRows_HidesThePendingAnalysisMessage(){
        SceneInspectorMember Member=new(
            new EditorNodeId(1),"global::Sample.Label","Text","\"Hi\"",IsEditable:true,
            InspectorEditorKind.Text,ConstantValue:"Hi",NumericType:null,EnumCandidates:[]);
        InspectorMemberViewModel RowViewModel=new(Member);
        InspectorComponentGroupViewModel Group=new(
            new SceneInspectorComponentGroup(new EditorNodeId(1),"global::Sample.Label","Label",[
                new SceneInspectorMemberRow(Member),
            ]),
            Rows:[RowViewModel],IsExpanded:true);

        Assert.Equal(System.Windows.Visibility.Collapsed,Group.PendingMessageVisibility);
        Assert.Equal("Text",Assert.IsType<InspectorMemberViewModel>(Assert.Single(Group.Rows)).MemberName);
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
