namespace CSharpGameEngine.VSExtension.Tests;

/// <summary>
/// M0052 Step 2-3: パレットからのドロップ可否。<c>DragOver</c>のカーソルと<c>Drop</c>後の説明文を
/// 同じ関数から作ることで、プレビューと結果が食い違わないことを固定する。
/// </summary>
public sealed class ComponentDropPolicyTests {
    [Fact]
    public void Evaluate_NoScene_IsRejectedBeforeAnythingElse()=>
        Assert.Equal(
            ComponentDropVerdict.RejectNoScene,
            ComponentDropPolicy.Evaluate(
                HasScene:false,TargetResolved:true,TargetHasComponentsList:true,TargetAlreadyHasType:false));

    [Fact]
    public void Evaluate_NoTargetNode_IsRejected()=>
        Assert.Equal(
            ComponentDropVerdict.RejectNoTarget,
            ComponentDropPolicy.Evaluate(
                HasScene:true,TargetResolved:false,TargetHasComponentsList:false,TargetAlreadyHasType:false));

    [Fact]
    public void Evaluate_DuplicateType_IsRejected()=>
        Assert.Equal(
            ComponentDropVerdict.RejectDuplicate,
            ComponentDropPolicy.Evaluate(
                HasScene:true,TargetResolved:true,TargetHasComponentsList:true,TargetAlreadyHasType:true));

    /// <summary>
    /// <c>Components=[…]</c> が書かれていないノードは <c>SceneModelEditApplier.TryApplyAddComponent</c>
    /// が拒否する。離す前に不可カーソルで伝えるため、同じ条件をここでも見る。
    /// </summary>
    [Fact]
    public void Evaluate_NodeWithoutAComponentsList_IsRejected()=>
        Assert.Equal(
            ComponentDropVerdict.RejectNoComponentsList,
            ComponentDropPolicy.Evaluate(
                HasScene:true,TargetResolved:true,TargetHasComponentsList:false,TargetAlreadyHasType:false));

    [Fact]
    public void Evaluate_UsableTarget_IsAccepted(){
        var Verdict=ComponentDropPolicy.Evaluate(
            HasScene:true,TargetResolved:true,TargetHasComponentsList:true,TargetAlreadyHasType:false);

        Assert.Equal(ComponentDropVerdict.Accept,Verdict);
        Assert.True(ComponentDropPolicy.Accepts(Verdict));
    }

    /// <summary>判定を増やしたときに文言を足し忘れると<c>Describe</c>が例外を投げる。</summary>
    [Fact]
    public void Describe_EveryVerdictHasAMessage(){
        foreach(ComponentDropVerdict Verdict in System.Enum.GetValues(typeof(ComponentDropVerdict))){
            Assert.False(string.IsNullOrWhiteSpace(ComponentDropPolicy.Describe(Verdict,"Transform")));
        }
    }

    [Fact]
    public void Describe_RejectionsNameTheComponentSoTheStatusBarIsActionable(){
        Assert.Contains("Transform",ComponentDropPolicy.Describe(ComponentDropVerdict.RejectDuplicate,"Transform"));
        Assert.Contains(
            "Transform",ComponentDropPolicy.Describe(ComponentDropVerdict.RejectNoComponentsList,"Transform"));
    }

    [Fact]
    public void Accepts_IsTrueOnlyForAccept(){
        Assert.True(ComponentDropPolicy.Accepts(ComponentDropVerdict.Accept));
        Assert.False(ComponentDropPolicy.Accepts(ComponentDropVerdict.RejectDuplicate));
        Assert.False(ComponentDropPolicy.Accepts(ComponentDropVerdict.RejectNoScene));
    }

    /// <summary>
    /// Node Reparent用の<c>HierarchyDragPayload</c>とは別型でなければならない。同じ型を流用すると
    /// Hierarchyのドロップ側で2種類のドラッグを見分けられなくなる。
    /// </summary>
    [Fact]
    public void ComponentDragPayload_CarriesEverythingNeededToApplyWithoutTheCatalog(){
        ComponentDragPayload Payload=new(
            "global::Sample.Transform","new global::Sample.Transform()","Transform");

        Assert.Equal("global::Sample.Transform",Payload.ComponentTypeFullyQualifiedName);
        Assert.Equal("new global::Sample.Transform()",Payload.ComponentExpression);
        Assert.Equal("Transform",Payload.DisplayName);
    }
}
