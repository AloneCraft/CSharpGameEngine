using System.Collections.Immutable;

namespace CSharpGameEngine.CodeAnalysis.Tests;

/// <summary>
/// 生成層モデルの値等価性(ADR-0040 リスクO-2)。
///
/// <para>
/// <c>ImmutableArray&lt;T&gt;</c>の既定の等価性は<b>内部配列の参照比較</b>である。手書きの
/// <see cref="IEquatable{T}"/>を忘れると、内容が同じでも別インスタンスなら不一致になり、
/// インクリメンタルジェネレーターのキャッシュが毎回ミスして重い生成段が走り続ける。
/// 静かに性能だけが劣化するため、テストで固定しておく必要がある。
/// </para>
/// </summary>
public sealed class SceneEmitModelEqualityTests {
    private static SceneEmitModel BuildModel(string ComponentType="global::Sample.Transform",string?NodeName="Root"){
        SceneEmitNode Child=new(
            "Node_0",
            "0",
            "Child",
            [new SceneEmitComponent("global::Sample.Light","new global::Sample.Light()")],
            []);
        SceneEmitNode Root=new(
            "Node",
            "(root)",
            NodeName,
            [new SceneEmitComponent(ComponentType, $"new {ComponentType}()")],
            [Child]);

        return new SceneEmitModel("Game","MainScene","Game.MainScene",Root,2,IsStartupScene:false,0,HasErrors:false,"Scene.cs",42);
    }

    [Fact]
    public void StructurallyIdenticalModelsBuiltSeparatelyAreEqual(){
        var Left=BuildModel();
        var Right=BuildModel();

        Assert.Equal(Left,Right);
        Assert.Equal(Left.GetHashCode(),Right.GetHashCode());
    }

    [Fact]
    public void DifferentComponentTypeMakesModelsUnequal(){
        Assert.NotEqual(BuildModel(),BuildModel(ComponentType:"global::Sample.Mesh"));
    }

    [Fact]
    public void DifferentNodeNameMakesModelsUnequal(){
        Assert.NotEqual(BuildModel(),BuildModel(NodeName:"Renamed"));
    }

    [Fact]
    public void NullAndNonNullNodeNamesAreDistinguished(){
        Assert.NotEqual(BuildModel(NodeName:null),BuildModel(NodeName:"Root"));
        Assert.Equal(BuildModel(NodeName:null),BuildModel(NodeName:null));
    }

    [Fact]
    public void ChildCountDifferenceIsDetected(){
        SceneEmitNode WithoutChildren=new("Node","(root)","Root",[],[]);
        SceneEmitNode WithChild=new("Node","(root)","Root",[],[new SceneEmitNode("Node_0","0",null,[],[])]);

        Assert.NotEqual(WithoutChildren,WithChild);
    }

    [Fact]
    public void ImmutableArrayReferenceEqualityIsNotWhatWeRelyOn(){
        // このテストは「なぜ手書きのIEquatableが要るのか」を記録するためにある。
        ImmutableArray<SceneEmitComponent>Left=[new SceneEmitComponent("T","new T()")];
        ImmutableArray<SceneEmitComponent>Right=[new SceneEmitComponent("T","new T()")];

        Assert.False(Left.Equals(Right));
        Assert.True(SceneEmitEquality.SequenceEqual(Left,Right));
    }

    [Fact]
    public void DefaultArraysAreHandledWithoutThrowing(){
        ImmutableArray<SceneEmitComponent>Default=default;

        Assert.True(SceneEmitEquality.SequenceEqual(Default,default));
        Assert.False(SceneEmitEquality.SequenceEqual(Default,[new SceneEmitComponent("T","new T()")]));
        Assert.Equal(0,SceneEmitEquality.CombineHashCodes(Default));
    }

    [Fact]
    public void GenerationGateIsValueEqual(){
        GenerationGate Left=new(HasRuntimeMarker:true,Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,HasUserEntryPoint:false);
        GenerationGate Right=new(HasRuntimeMarker:true,Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,HasUserEntryPoint:false);

        Assert.Equal(Left,Right);
        Assert.Equal(Left.GetHashCode(),Right.GetHashCode());
        Assert.NotEqual(Left,new GenerationGate(HasRuntimeMarker:false,Microsoft.CodeAnalysis.OutputKind.ConsoleApplication,HasUserEntryPoint:false));
    }
}
