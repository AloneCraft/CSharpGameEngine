using System.Collections.Immutable;

namespace CSharpGameEngine.SceneAnalysis.Tests;

/// <summary>
/// <see cref="NodePath"/>はScene DSLにおけるノードの唯一の同一性であり、Inspectorの選択復元・Hierarchyの
/// 展開状態・Scene Viewのピッキング結果すべてのキーになる。等価性とハッシュが壊れると、それら全部が
/// 静かに壊れる。
/// </summary>
public sealed class NodePathTests {
    [Fact]
    public void DefaultInstance_BehavesAsRoot(){
        NodePath Default=default;

        Assert.True(Default.IsRoot);
        Assert.Equal(0,Default.Depth);
        Assert.Empty(Default.Indices);
        Assert.Equal("(root)",Default.ToDisplayString());
        Assert.Equal(NodePath.Root,Default);
    }

    [Fact]
    public void Append_BuildsTheChildPath(){
        var Path=NodePath.Root.Append(2).Append(0).Append(5);

        Assert.Equal(3,Path.Depth);
        Assert.Equal("2/0/5",Path.ToDisplayString());

        // ImmutableArray<T>の既定の等価性は内部配列の参照比較なので、必ず要素列として比較する
        // (ADR-0040 リスクO-2。SceneEmitModelのIEquatableを手書きする理由でもある)。
        Assert.Equal([2,0,5],Path.Indices.ToArray());
    }

    [Fact]
    public void Parent_WalksBackUpAndIsNullAtTheRoot(){
        var Path=NodePath.Root.Append(1).Append(4);

        Assert.Equal("1",Path.Parent?.ToDisplayString());
        Assert.Equal("(root)",Path.Parent?.Parent?.ToDisplayString());
        Assert.Null(NodePath.Root.Parent);
    }

    [Fact]
    public void EqualityIsByValueNotByReference(){
        var Left=NodePath.Root.Append(1).Append(2);
        NodePath Right=new(ImmutableArray.Create(1,2));

        Assert.Equal(Left,Right);
        Assert.Equal(Left.GetHashCode(),Right.GetHashCode());
        Assert.NotEqual(Left,NodePath.Root.Append(1).Append(3));
        Assert.NotEqual(Left,NodePath.Root.Append(1));
    }

    [Fact]
    public void UsableAsADictionaryKey(){
        Dictionary<NodePath,string>Names=new(){
            [NodePath.Root]="Root",
            [NodePath.Root.Append(0)]="First",
            [NodePath.Root.Append(0).Append(0)]="Grandchild",
        };

        Assert.Equal("First",Names[new NodePath(ImmutableArray.Create(0))]);
        Assert.Equal("Grandchild",Names[new NodePath(ImmutableArray.Create(0,0))]);
        Assert.False(Names.ContainsKey(NodePath.Root.Append(1)));
    }

    [Fact]
    public void IsDescendantOf_IsStrict(){
        var Ancestor=NodePath.Root.Append(1);
        var Child=Ancestor.Append(0);

        Assert.True(Child.IsDescendantOf(Ancestor));
        Assert.True(Child.IsDescendantOf(NodePath.Root));
        Assert.False(Ancestor.IsDescendantOf(Child));
        Assert.False(Ancestor.IsDescendantOf(Ancestor));
        Assert.False(NodePath.Root.Append(2).IsDescendantOf(Ancestor));
    }

    [Theory]
    [InlineData("",true,0)]
    [InlineData("(root)",true,0)]
    [InlineData("0",true,1)]
    [InlineData("2/0/5",true,3)]
    [InlineData("a/b",false,0)]
    [InlineData("1//2",false,0)]
    [InlineData("-1",false,0)]
    public void TryParse_RoundTripsTheDisplayForm(string Text,bool ExpectedSuccess,int ExpectedDepth){
        var Parsed=NodePath.TryParse(Text,out var Path);

        Assert.Equal(ExpectedSuccess,Parsed);

        if(ExpectedSuccess){
            Assert.Equal(ExpectedDepth,Path.Depth);
            Assert.Equal(Text.Length==0?"(root)":Text,Path.ToDisplayString());
        }
    }

    [Fact]
    public void TryParse_RejectsNull(){
        Assert.False(NodePath.TryParse(null,out _));
    }
}
