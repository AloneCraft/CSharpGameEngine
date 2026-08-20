using System.Linq;

namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0052 Step 2-1: Inspectorの再構築判定・内容署名・展開キー・自動展開の純粋関数。
/// UIを起こさずに固定できる部分をここに閉じ込める。
/// </summary>
public sealed class InspectorRefreshPolicyTests {
    [Fact]
    public void Decide_Force_RebuildsEvenWhileTypingAndEvenIfNothingChanged(){
        Assert.Equal(
            InspectorRefreshAction.Apply,
            InspectorRefreshPolicy.Decide(HasKeyboardFocusWithin:true,ContentUnchanged:true,Force:true));
    }

    [Fact]
    public void Decide_ContentUnchanged_Skips(){
        // ここが本丸。Expanderの開閉はマウスだけで行うのでフォーカスはパネル外にあり、
        // Skipが無いと再解析のたびに展開状態とスクロール位置が飛ぶ。
        Assert.Equal(
            InspectorRefreshAction.Skip,
            InspectorRefreshPolicy.Decide(HasKeyboardFocusWithin:false,ContentUnchanged:true,Force:false));
    }

    [Fact]
    public void Decide_ChangedWhileTyping_Defers(){
        Assert.Equal(
            InspectorRefreshAction.Defer,
            InspectorRefreshPolicy.Decide(HasKeyboardFocusWithin:true,ContentUnchanged:false,Force:false));
    }

    [Fact]
    public void Decide_ChangedWithoutFocus_Applies(){
        Assert.Equal(
            InspectorRefreshAction.Apply,
            InspectorRefreshPolicy.Decide(HasKeyboardFocusWithin:false,ContentUnchanged:false,Force:false));
    }

    [Fact]
    public void Compute_SameContent_ProducesTheSameSignature(){
        Assert.Equal(
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","3f"))]),
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","3f"))]));
    }

    [Fact]
    public void Compute_ChangedValue_ProducesADifferentSignature(){
        Assert.NotEqual(
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","3f"))]),
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","9f"))]));
    }

    [Fact]
    public void Compute_AddedComponent_ProducesADifferentSignature(){
        Assert.NotEqual(
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","3f"))]),
            InspectorContentSignature.Compute([
                Group(1,"global::Sample.DirectionalLight",("Intensity","3f")),
                Group(1,"global::Sample.Label"),
            ]));
    }

    [Fact]
    public void Compute_MembersLandingOnAPendingComponent_ProducesADifferentSignature(){
        // 追加直後はmemberが空で、Roslynの再解析が着地して初めて値を編集できるようになる。
        // ここが同じ署名だとSkipされ、いつまでも「解析待ち」のままになる。
        Assert.NotEqual(
            InspectorContentSignature.Compute([Group(1,"global::Sample.Label")]),
            InspectorContentSignature.Compute([Group(1,"global::Sample.Label",("Text","\"Hi\""))]));
    }

    [Fact]
    public void Compute_SameComponentOnADifferentNode_ProducesADifferentSignature(){
        Assert.NotEqual(
            InspectorContentSignature.Compute([Group(1,"global::Sample.DirectionalLight",("Intensity","3f"))]),
            InspectorContentSignature.Compute([Group(2,"global::Sample.DirectionalLight",("Intensity","3f"))]));
    }

    /// <summary>
    /// 同じ署名にすると、一方から他方への遷移がSkipされて描き直されなくなる。
    /// どちらも空メッセージを出すので、見た目からは気づきにくい。
    /// </summary>
    [Fact]
    public void Compute_NoSessionAndSessionWithoutComponents_AreDistinguished(){
        Assert.NotEqual(InspectorContentSignature.Compute(null),InspectorContentSignature.Compute([]));
    }

    [Fact]
    public void ExpansionKey_IncludesTheNodeSoCollapsingOneNodeDoesNotAffectAnother(){
        Assert.NotEqual(
            InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform"),
            InspectorExpansionKey.Compute(new EditorNodeId(2),"global::Sample.Transform"));
    }

    [Fact]
    public void ExpansionKey_IsStableForTheSameNodeAndComponent(){
        Assert.Equal(
            InspectorExpansionKey.Compute(new EditorNodeId(7),"global::Sample.Transform"),
            InspectorExpansionKey.Compute(new EditorNodeId(7),"global::Sample.Transform"));
    }

    [Fact]
    public void ShouldExpand_MatchingPendingKey_ExpandsAndOtherwiseDefersToStoredState(){
        Assert.True(InspectorRevealPolicy.ShouldExpand("1@global::Sample.Label","1@global::Sample.Label"));
        Assert.False(InspectorRevealPolicy.ShouldExpand("1@global::Sample.Label","1@global::Sample.Transform"));
        Assert.False(InspectorRevealPolicy.ShouldExpand(PendingKey:null,"1@global::Sample.Label"));
    }

    [Fact]
    public void ShouldClearPending_ComponentPresentButStillEmpty_KeepsWaiting(){
        // SceneModelEditApplierはmemberが空のComponentを挿す。ここで消すと「展開されたが中身は空」で
        // 終わってしまい、値を設定できるようになる瞬間(=再解析の着地)を待てない。
        Assert.False(InspectorRevealPolicy.ShouldClearPending(GroupPresent:true,GroupMemberCount:0));
    }

    [Fact]
    public void ShouldClearPending_MembersLanded_Clears(){
        Assert.True(InspectorRevealPolicy.ShouldClearPending(GroupPresent:true,GroupMemberCount:2));
    }

    [Fact]
    public void ShouldClearPending_ComponentGoneForExampleByUndo_StopsWaiting(){
        Assert.True(InspectorRevealPolicy.ShouldClearPending(GroupPresent:false,GroupMemberCount:0));
    }

    [Fact]
    public void Compute_ChangedNestedValue_ProducesADifferentSignature(){
        Assert.NotEqual(
            InspectorContentSignature.Compute([GroupWithNested(1,"global::Sample.Transform","Position","Vector3",("X","1f"))]),
            InspectorContentSignature.Compute([GroupWithNested(1,"global::Sample.Transform","Position","Vector3",("X","9f"))]));
    }

    [Fact]
    public void Compute_SameNestedContent_ProducesTheSameSignature(){
        Assert.Equal(
            InspectorContentSignature.Compute([GroupWithNested(1,"global::Sample.Transform","Position","Vector3",("X","1f"))]),
            InspectorContentSignature.Compute([GroupWithNested(1,"global::Sample.Transform","Position","Vector3",("X","1f"))]));
    }

    [Fact]
    public void NestedExpansionKey_DiffersFromTheComponentKeyAndIsStable(){
        var ComponentKey=InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform");
        var NestedKey=InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform",["Position"]);

        Assert.NotEqual(ComponentKey,NestedKey);
        Assert.Equal(
            NestedKey,
            InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform",["Position"]));
    }

    [Fact]
    public void NestedExpansionKey_DiffersPerMemberPath(){
        Assert.NotEqual(
            InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform",["Position"]),
            InspectorExpansionKey.Compute(new EditorNodeId(1),"global::Sample.Transform",["Scale"]));
    }

    private static SceneInspectorComponentGroup Group(
        long NodeId,string ComponentType,params (string Name,string Expression)[]Members){
        EditorNodeId Id=new(NodeId);
        return new SceneInspectorComponentGroup(
            Id,ComponentType,SceneInspectorTypeName.Simplify(ComponentType),
            [..Members.Select(Member=>(SceneInspectorRow)new SceneInspectorMemberRow(new SceneInspectorMember(
                Id,ComponentType,Member.Name,Member.Expression,IsEditable:true,
                InspectorEditorKind.Text,ConstantValue:null,NumericType:null,EnumCandidates:[])))]);
    }

    private static SceneInspectorComponentGroup GroupWithNested(
        long NodeId,string ComponentType,string NestedName,string NestedType,
        params (string Name,string Expression)[]NestedMembers){
        EditorNodeId Id=new(NodeId);
        SceneInspectorNestedRow Nested=new(
            Id,ComponentType,[NestedName],NestedName,NestedType,
            [..NestedMembers.Select(Member=>(SceneInspectorRow)new SceneInspectorMemberRow(new SceneInspectorMember(
                Id,ComponentType,Member.Name,Member.Expression,IsEditable:false,
                InspectorEditorKind.ReadOnly,ConstantValue:null,NumericType:null,EnumCandidates:[])))]);
        return new SceneInspectorComponentGroup(
            Id,ComponentType,SceneInspectorTypeName.Simplify(ComponentType),[Nested]);
    }
}

/// <summary>
/// 完全修飾型名の短縮。<c>LastIndexOf('.')</c>ではgeneric instantiationを誤って切るため、
/// パレットから追加できるようになった閉じたgeneric型を正しく表示できることを固定する。
/// </summary>
public sealed class SceneInspectorTypeNameTests {
    [Theory]
    [InlineData("global::Sample.Transform","Transform")]
    [InlineData("Sample.Transform","Transform")]
    [InlineData("Transform","Transform")]
    [InlineData("global::Sample.StatusEffect<global::Sample.Kind>","StatusEffect<Kind>")]
    [InlineData("global::Sample.Pair<global::Sample.Kind1, global::Sample.Kind2>","Pair<Kind1, Kind2>")]
    [InlineData("global::Sample.Outer<global::Sample.Inner<global::Sample.Kind>>","Outer<Inner<Kind>>")]
    public void Simplify_DropsNamespacesFromEveryTypeArgumentToo(string FullyQualified,string Expected)=>
        Assert.Equal(Expected,SceneInspectorTypeName.Simplify(FullyQualified));
}
